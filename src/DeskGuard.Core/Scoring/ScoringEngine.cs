namespace DeskGuard.Core.Scoring;

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DeskGuard.Core.Models;

public sealed partial class ScoringEngine
{
    public static readonly Dictionary<string, double> DefaultWeights = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spf"] = 10,
        ["dmarc"] = 10,
        ["dkim"] = 5,
        ["alignment"] = 5,
        ["rdns"] = 4,
        ["rbl"] = 10,
        ["body"] = 5,
        ["heuristics"] = 6,
        ["spamflag"] = 4,
        ["mime"] = 6,
        ["msscl"] = 10,
        ["linkmismatch"] = 8,
        ["displayspoof"] = 7,
        ["lookalike"] = 5,
        ["replyto"] = 3,
        ["firstcontact"] = 2
    };

    public static readonly ThresholdSettings DefaultThresholds = new()
    {
        SpamThresholdPercent = 40.0,
        MaliciousThresholdPercent = 20.0,
        SuspiciousThresholdPercent = 55.0
    };

    private static readonly string[] DefaultBrands =
    [
        "paypal", "microsoft", "apple", "amazon", "google", "netflix", "dhl", "fedex", "chase", "bank of america"
    ];

    private static readonly string[] DefaultLookalikeTargets =
    [
        "paypal.com", "microsoft.com", "apple.com", "amazon.com", "google.com"
    ];

    private static readonly HashSet<string> FreeWebmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "yahoo.com", "hotmail.com", "outlook.com", "protonmail.com"
    };

    private static readonly string[] DangerousExtensions =
    [
        ".exe", ".scr", ".vbs", ".iso", ".bat", ".cmd", ".js", ".hta"
    ];

    private static readonly string[] SuspiciousBodyKeywords =
    [
        "urgent wire", "suspended account", "password expired", "verify your identity immediately", "bitcoin payment", "crypto transfer"
    ];

    private static readonly HashSet<string> NewsletterTrackers = new(StringComparer.OrdinalIgnoreCase)
    {
        "sendgrid.net", "list-manage.com", "mcusercontent.com", "hubspotlinks.com", "hs-sites.com",
        "rs6.net", "exacttarget.com", "mailchimp.com", "constantcontact.com", "mandrillapp.com",
        "sparkpostmail.com", "mailgun.org", "amazonses.com", "awstrack.me", "cmail19.com",
        "cmail20.com", "createsend.com", "salesforce.com", "pardot.com", "marketo.com", "mktoweb.com"
    };

    [GeneratedRegex(@"\bspf=(pass|fail|softfail|none|permerror)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SpfRegex();

    [GeneratedRegex(@"^Received-SPF:\s*(pass|fail|softfail|none|permerror)\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ReceivedSpfRegex();

    [GeneratedRegex(@"\bdmarc=(pass|fail|none|permerror)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DmarcRegex();

    [GeneratedRegex(@"\bdkim=(pass|fail|none|permerror)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DkimRegex();

    [GeneratedRegex(@"^Return-Path:\s*<?([^>\s]+)>?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ReturnPathRegex();

    [GeneratedRegex(@"^X-MS-Exchange-Organization-SCL:\s*(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex OrgSclRegex();

    [GeneratedRegex(@"X-Forefront-Antispam-Report:.*?SCL:(-?\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ForefrontSclRegex();

    [GeneratedRegex(@"X-Microsoft-Antispam:.*?BCL:(\d+)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BclRegex();

    [GeneratedRegex(@"<a\s+(?:[^>]*?\s+)?href=[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"^https?://([^/?#:]+)", RegexOptions.IgnoreCase)]
    private static partial Regex HrefDomainRegex();

    [GeneratedRegex(@"\b([a-zA-Z0-9.-]+\.[a-zA-Z]{2,})\b")]
    private static partial Regex TextDomainRegex();

    [GeneratedRegex(@"^(?:""?([^""<]*)""?\s*)?<?([^>]+@[^>]+)>?$")]
    private static partial Regex FromRegex();

    [GeneratedRegex(@"([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})")]
    private static partial Regex InnerEmailRegex();

    [GeneratedRegex(@"^Reply-To:\s*<?([^>\s]+)>?", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ReplyToRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlStripRegex();

    public SecurityVerdict ScoreEmail(
        EmailRecord email,
        ScanConfiguration? config = null,
        string? rawEml = null,
        int? explicitScl = null,
        IEnumerable<AttachmentInfo>? explicitAttachments = null)
    {
        ScanConfiguration effectiveConfig = config ?? new ScanConfiguration();
        Dictionary<string, double> weights = new(DefaultWeights, StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in effectiveConfig.Weights)
        {
            weights[k] = v;
        }

        Dictionary<string, bool> toggles = new(effectiveConfig.Toggles, StringComparer.OrdinalIgnoreCase);
        ThresholdSettings thresholds = effectiveConfig.Thresholds ?? DefaultThresholds;
        Dictionary<string, int> knownDomains = effectiveConfig.KnownDomains ?? [];

        string rawHeaders = email.Headers;
        string body = email.Body;
        string sender = email.Sender;
        string subject = email.Subject;

        if (!string.IsNullOrWhiteSpace(rawEml))
        {
            ParsedEml parsed = EmlParser.Parse(rawEml);
            rawHeaders = parsed.RawHeaders;
            body = parsed.Body;
            if (parsed.Headers.TryGetValue("from", out string? parsedFrom) && !string.IsNullOrWhiteSpace(parsedFrom))
            {
                sender = parsedFrom;
            }
            if (parsed.Headers.TryGetValue("subject", out string? parsedSubject) && !string.IsNullOrWhiteSpace(parsedSubject))
            {
                subject = parsedSubject;
            }
        }

        List<SecurityPenalty> penalties = [];

        bool IsEnabled(string key) => !toggles.TryGetValue(key, out bool enabled) || enabled;

        void AddPenalty(string rule, string key, double fraction = 1.0, string evidence = "")
        {
            if (!IsEnabled(key)) return;
            double w = weights.GetValueOrDefault(key, 0.0);
            double pts = Math.Round(w * fraction * 10.0) / 10.0;
            if (pts <= 0) return;
            penalties.Add(new SecurityPenalty
            {
                Rule = rule,
                Key = key,
                Points = pts,
                Evidence = evidence
            });
        }

        // Extract sender domain and display name
        string senderDomain = string.Empty;
        string displayName = string.Empty;
        Match fromMatch = FromRegex().Match(sender);
        if (fromMatch.Success)
        {
            displayName = fromMatch.Groups[1].Value.Trim();
            string emailAddr = fromMatch.Groups[2].Value;
            int atIdx = emailAddr.IndexOf('@');
            if (atIdx != -1) senderDomain = emailAddr[(atIdx + 1)..].ToLowerInvariant();
        }
        else
        {
            int atIdx = sender.IndexOf('@');
            if (atIdx != -1) senderDomain = sender[(atIdx + 1)..].Replace(">", "").Trim().ToLowerInvariant();
        }
        string senderRegDomain = RegisteredDomainParser.GetRegisteredDomain(senderDomain);

        // 1. SPF Check
        if (IsEnabled("spf"))
        {
            Match spfMatch = SpfRegex().Match(rawHeaders);
            if (!spfMatch.Success) spfMatch = ReceivedSpfRegex().Match(rawHeaders);
            if (spfMatch.Success)
            {
                string res = spfMatch.Groups[1].Value.ToLowerInvariant();
                switch (res)
                {
                    case "fail":
                        AddPenalty("SPF:fail", "spf", 1.0, "SPF record authentication failed");
                        break;
                    case "softfail":
                        AddPenalty("SPF:softfail", "spf", 0.8, "SPF softfail (sender questionable)");
                        break;
                    case "none":
                        AddPenalty("SPF:none", "spf", 0.4, "No SPF record published");
                        break;
                    case "permerror":
                        AddPenalty("SPF:permerror", "spf", 0.6, "SPF syntax permanent error");
                        break;
                }
            }
        }

        // 2. DMARC Check
        if (IsEnabled("dmarc"))
        {
            Match dmarcMatch = DmarcRegex().Match(rawHeaders);
            if (dmarcMatch.Success)
            {
                string res = dmarcMatch.Groups[1].Value.ToLowerInvariant();
                switch (res)
                {
                    case "fail":
                        AddPenalty("DMARC:fail", "dmarc", 1.0, "DMARC alignment check failed");
                        break;
                    case "none":
                        AddPenalty("DMARC:none", "dmarc", 0.5, "No DMARC policy defined");
                        break;
                    case "permerror":
                        AddPenalty("DMARC:permerror", "dmarc", 0.7, "DMARC permanent error");
                        break;
                }
            }
        }

        // 3. DKIM Check
        if (IsEnabled("dkim"))
        {
            Match dkimMatch = DkimRegex().Match(rawHeaders);
            if (dkimMatch.Success)
            {
                string res = dkimMatch.Groups[1].Value.ToLowerInvariant();
                switch (res)
                {
                    case "fail":
                        AddPenalty("DKIM:fail", "dkim", 1.0, "DKIM cryptographic signature verification failed");
                        break;
                    case "none":
                        AddPenalty("DKIM:none", "dkim", 0.4, "No DKIM signature present");
                        break;
                }
            }
        }

        // 4. Alignment
        if (IsEnabled("alignment"))
        {
            Match rpMatch = ReturnPathRegex().Match(rawHeaders);
            if (rpMatch.Success && !string.IsNullOrEmpty(senderRegDomain))
            {
                string rpAddress = rpMatch.Groups[1].Value;
                string[] rpParts = rpAddress.Split('@');
                string rpDomain = rpParts.Length > 1 ? rpParts[1].ToLowerInvariant() : string.Empty;
                string rpRegDomain = RegisteredDomainParser.GetRegisteredDomain(rpDomain);
                if (!string.IsNullOrEmpty(rpRegDomain) && !string.Equals(rpRegDomain, senderRegDomain, StringComparison.OrdinalIgnoreCase))
                {
                    AddPenalty("SENDER_MISALIGNMENT", "alignment", 1.0, $"Return-Path ({rpRegDomain}) does not match From ({senderRegDomain})");
                }
            }
        }

        // 5. MS SCL / BCL
        if (IsEnabled("msscl"))
        {
            int? sclVal = explicitScl;
            int? bclVal = null;

            Match orgScl = OrgSclRegex().Match(rawHeaders);
            if (orgScl.Success && int.TryParse(orgScl.Groups[1].Value, out int os))
            {
                sclVal = os;
            }

            Match ffScl = ForefrontSclRegex().Match(rawHeaders);
            if (ffScl.Success && int.TryParse(ffScl.Groups[1].Value, out int fs))
            {
                sclVal = fs;
            }

            Match bclMatch = BclRegex().Match(rawHeaders);
            if (bclMatch.Success && int.TryParse(bclMatch.Groups[1].Value, out int bc))
            {
                bclVal = bc;
            }

            if (sclVal.HasValue)
            {
                if (sclVal.Value >= 9)
                {
                    AddPenalty("MS_SPAM_SCORE", "msscl", 1.0, $"Microsoft SCL is {sclVal.Value} (High spam confidence)");
                }
                else if (sclVal.Value >= 5)
                {
                    AddPenalty("MS_SPAM_SCORE", "msscl", 0.7, $"Microsoft SCL is {sclVal.Value} (Suspected spam)");
                }
            }
            else if (bclVal.HasValue && bclVal.Value >= 7)
            {
                AddPenalty("MS_BULK_SCORE", "msscl", 0.5, $"Microsoft BCL is {bclVal.Value} (High-volume marketing / bulk)");
            }
        }

        // 6. Link Mismatch
        if (IsEnabled("linkmismatch"))
        {
            MatchCollection linkMatches = LinkRegex().Matches(body);
            foreach (Match match in linkMatches)
            {
                string href = match.Groups[1].Value;
                string text = HtmlStripRegex().Replace(match.Groups[2].Value, "").Trim();

                string unwrappedHref = SafeLinksDecoder.UnwrapUrl(href);
                Match hrefDomainMatch = HrefDomainRegex().Match(unwrappedHref);
                Match textDomainMatch = TextDomainRegex().Match(text);

                if (hrefDomainMatch.Success && textDomainMatch.Success)
                {
                    string hrefReg = RegisteredDomainParser.GetRegisteredDomain(hrefDomainMatch.Groups[1].Value);
                    string textReg = RegisteredDomainParser.GetRegisteredDomain(textDomainMatch.Groups[1].Value);

                    if (!string.IsNullOrEmpty(textReg) && !string.IsNullOrEmpty(hrefReg) &&
                        !string.Equals(textReg, hrefReg, StringComparison.OrdinalIgnoreCase) &&
                        !NewsletterTrackers.Contains(hrefReg))
                    {
                        AddPenalty("LINK_MISMATCH", "linkmismatch", 1.0, $"Visible link \"{textReg}\" actually points to \"{hrefReg}\"");
                        break;
                    }
                }
            }
        }

        // 7. Display Name Spoofing
        if (IsEnabled("displayspoof") && !string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(senderRegDomain))
        {
            string lowerName = displayName.ToLowerInvariant();
            bool brandSpoofed = false;
            foreach (string brand in DefaultBrands)
            {
                if (lowerName.Contains(brand) && !senderRegDomain.Contains(brand))
                {
                    AddPenalty("DISPLAY_NAME_SPOOF", "displayspoof", 1.0, $"Display name claims brand \"{brand}\" but domain is \"{senderRegDomain}\"");
                    brandSpoofed = true;
                    break;
                }
            }

            if (!brandSpoofed)
            {
                Match innerEmail = InnerEmailRegex().Match(displayName);
                if (innerEmail.Success && !innerEmail.Groups[1].Value.EndsWith("@" + senderDomain, StringComparison.OrdinalIgnoreCase))
                {
                    AddPenalty("DISPLAY_NAME_SPOOF", "displayspoof", 1.0, $"Display name contains address \"{innerEmail.Groups[1].Value}\" different from \"{senderDomain}\"");
                }
            }
        }

        // 8. Lookalike Domain
        if (IsEnabled("lookalike") && !string.IsNullOrWhiteSpace(senderRegDomain))
        {
            if (senderRegDomain.StartsWith("xn--", StringComparison.OrdinalIgnoreCase))
            {
                AddPenalty("LOOKALIKE_DOMAIN", "lookalike", 1.0, $"Punycode internationalized domain \"{senderRegDomain}\"");
            }
            else
            {
                string foldedSender = RegisteredDomainParser.FoldHomoglyphs(senderRegDomain);
                IEnumerable<string> targets = knownDomains.Count > 0 ? knownDomains.Keys : DefaultLookalikeTargets;
                foreach (string target in targets)
                {
                    string regTarget = RegisteredDomainParser.GetRegisteredDomain(target);
                    if (!string.IsNullOrEmpty(regTarget) && !string.Equals(regTarget, senderRegDomain, StringComparison.OrdinalIgnoreCase))
                    {
                        string foldedTarget = RegisteredDomainParser.FoldHomoglyphs(regTarget);
                        int dist = StringDistance.LevenshteinDistance(foldedSender, foldedTarget);
                        if (string.Equals(foldedSender, foldedTarget, StringComparison.Ordinal) || (dist > 0 && dist <= 2))
                        {
                            AddPenalty("LOOKALIKE_DOMAIN", "lookalike", 1.0, $"Domain \"{senderRegDomain}\" is dangerously similar to \"{regTarget}\"");
                            break;
                        }
                    }
                }
            }
        }

        // 9. Reply-To Mismatch
        if (IsEnabled("replyto"))
        {
            Match replyToMatch = ReplyToRegex().Match(rawHeaders);
            if (replyToMatch.Success && !string.IsNullOrEmpty(senderDomain))
            {
                string replyAddr = replyToMatch.Groups[1].Value;
                string[] parts = replyAddr.Split('@');
                string replyDomain = parts.Length > 1 ? parts[1].ToLowerInvariant() : string.Empty;
                string replyReg = RegisteredDomainParser.GetRegisteredDomain(replyDomain);

                if (!string.IsNullOrEmpty(replyReg) && !string.Equals(replyReg, senderRegDomain, StringComparison.OrdinalIgnoreCase))
                {
                    bool isFree = FreeWebmailDomains.Contains(replyReg);
                    AddPenalty("REPLYTO_MISMATCH", "replyto", isFree ? 1.0 : 0.5, $"Reply-To domain \"{replyReg}\" differs from From domain \"{senderRegDomain}\"");
                }
            }
        }

        // 10. First Contact
        if (IsEnabled("firstcontact") && !string.IsNullOrWhiteSpace(senderRegDomain))
        {
            if (knownDomains.Count > 0 && !knownDomains.ContainsKey(senderRegDomain))
            {
                AddPenalty("FIRST_CONTACT", "firstcontact", 1.0, $"Sender domain \"{senderRegDomain}\" has never been seen in the mailbox");
            }
        }

        // 11. Body keywords / Phishing keywords
        if (IsEnabled("body") && !string.IsNullOrWhiteSpace(body))
        {
            string lowerBody = body.ToLowerInvariant();
            foreach (string kw in SuspiciousBodyKeywords)
            {
                if (lowerBody.Contains(kw))
                {
                    AddPenalty("SUSPICIOUS_KEYWORD", "body", 1.0, $"Body contains high-risk phrasing: \"{kw}\"");
                    break;
                }
            }
        }

        // 12. MIME / Dangerous attachments
        if (IsEnabled("mime"))
        {
            bool hasDangerous = false;
            string dangerousName = string.Empty;

            if (explicitAttachments != null)
            {
                foreach (AttachmentInfo att in explicitAttachments)
                {
                    string attName = (att.Name ?? string.Empty).ToLowerInvariant();
                    foreach (string ext in DangerousExtensions)
                    {
                        if (attName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                        {
                            hasDangerous = true;
                            dangerousName = att.Name ?? string.Empty;
                            break;
                        }
                    }
                    if (hasDangerous) break;
                }
            }
            else if (!string.IsNullOrWhiteSpace(email.AttachmentsJson))
            {
                string lowerJson = email.AttachmentsJson.ToLowerInvariant();
                foreach (string ext in DangerousExtensions)
                {
                    if (lowerJson.Contains(ext))
                    {
                        hasDangerous = true;
                        dangerousName = ext;
                        break;
                    }
                }
            }

            if (hasDangerous)
            {
                AddPenalty("DANGEROUS_ATTACHMENT", "mime", 1.0, $"Suspicious attachment detected: \"{dangerousName}\"");
            }
        }

        // Calculate score
        double totalDeduction = 0.0;
        bool hasDangerousAttachment = false;
        foreach (SecurityPenalty p in penalties)
        {
            totalDeduction += p.Points;
            if (p.Rule == "DANGEROUS_ATTACHMENT") hasDangerousAttachment = true;
        }

        double score = Math.Max(0.0, Math.Min(100.0, Math.Round(100.0 - totalDeduction)));
        if (hasDangerousAttachment)
        {
            score = Math.Min(score, 10.0);
        }

        // Determine verdict
        string verdict = "Safe";
        if (score <= thresholds.MaliciousThresholdPercent)
        {
            verdict = "Malicious";
        }
        else if (score <= thresholds.SpamThresholdPercent)
        {
            verdict = "Spam";
        }
        else if (score <= thresholds.SuspiciousThresholdPercent)
        {
            verdict = "Suspicious";
        }

        return new SecurityVerdict
        {
            Score = score,
            Verdict = verdict,
            Penalties = penalties,
            TotalDeduction = totalDeduction
        };
    }
}
