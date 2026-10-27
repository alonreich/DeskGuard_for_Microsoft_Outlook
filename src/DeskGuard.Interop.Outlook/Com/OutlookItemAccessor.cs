namespace DeskGuard.Interop.Outlook.Com;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DeskGuard.Core.Models;
using Microsoft.Office.Interop.Outlook;

public static partial class OutlookItemAccessor
{
    [GeneratedRegex(@"(?im)^Subject:\s*(.*?)$")]
    private static partial Regex SubjectHeaderRegex();

    [GeneratedRegex(@"(?im)^From:\s*(?:.*?<)?([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})>?")]
    private static partial Regex FromHeaderRegex();

    [GeneratedRegex(@"(?im)^Return-Path:\s*<([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})>")]
    private static partial Regex ReturnPathHeaderRegex();

    [GeneratedRegex(@"^(Re:|Fwd:|FW:|RE:)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex CleanSubjectRegex();

    [GeneratedRegex(@"\bfrom\s+(?:[a-zA-Z0-9.-]+\s+)?\[?(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\]?", RegexOptions.IgnoreCase)]
    private static partial Regex ReceivedIpRegex();

    public static string ComputeSha256(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "N/A";
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string ComputeMd5(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "N/A";
        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = MD5.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    public static string GetPropertySafe(object? item, string hexTag, string fallback = "")
    {
        if (item == null) return fallback;
        PropertyAccessor? pa = null;
        try
        {
            if (item is _MailItem mi) pa = mi.PropertyAccessor;
            else if (item is MailItem m) pa = m.PropertyAccessor;
            else if (item is Recipient r) pa = r.PropertyAccessor;
            else if (item is Folder f) pa = f.PropertyAccessor;

            if (pa == null) return fallback;

            string url = MapiPropertyTags.ToPropTagUrl(hexTag);
            object val = pa.GetProperty(url);
            if (val == null) return fallback;

            if (val is byte[] bytes)
            {
                return Convert.ToHexString(bytes);
            }

            string str = val.ToString() ?? string.Empty;
            return string.IsNullOrWhiteSpace(str) ? fallback : str;
        }
        catch
        {
            return fallback;
        }
        finally
        {
            ComUtils.Release(pa);
        }
    }

    public static string ResolveEmail(Recipient? recipient)
    {
        if (recipient == null) return "Unknown";

        string addr = GetPropertySafe(recipient, MapiPropertyTags.PrSmtpAddressA, string.Empty);
        if (string.IsNullOrWhiteSpace(addr) || addr.Contains("/o="))
        {
            AddressEntry? ae = null;
            ExchangeUser? user = null;
            try
            {
                ae = recipient.AddressEntry;
                if (ae != null)
                {
                    try
                    {
                        user = ae.GetExchangeUser();
                        if (user != null)
                        {
                            addr = user.PrimarySmtpAddress;
                        }
                    }
                    catch { }

                    if (string.IsNullOrWhiteSpace(addr))
                    {
                        addr = ae.Name;
                    }
                }
            }
            catch { }
            finally
            {
                ComUtils.Release(user);
                ComUtils.Release(ae);
            }
        }

        if (string.IsNullOrWhiteSpace(addr))
        {
            try { addr = recipient.Address; } catch { }
        }

        if (!string.IsNullOrWhiteSpace(addr) && addr.Contains("/o="))
        {
            int cnIdx = addr.LastIndexOf("cn=", StringComparison.OrdinalIgnoreCase);
            if (cnIdx != -1)
            {
                addr = addr[(cnIdx + 3)..];
            }
            else
            {
                string[] parts = addr.Split('=');
                addr = parts[^1];
            }
        }

        return string.IsNullOrWhiteSpace(addr) ? "Internal User" : addr.Trim();
    }

    public static string GetItemSubject(MailItem? mailItem)
    {
        if (mailItem == null) return "(No Subject)";
        string su = string.Empty;

        try { su = mailItem.Subject; } catch { }

        if (string.IsNullOrWhiteSpace(su)) su = GetPropertySafe(mailItem, MapiPropertyTags.PrSubjectW, string.Empty);
        if (string.IsNullOrWhiteSpace(su)) su = GetPropertySafe(mailItem, MapiPropertyTags.PrSubjectA, string.Empty);
        if (string.IsNullOrWhiteSpace(su)) su = GetPropertySafe(mailItem, MapiPropertyTags.PrConversationTopicW, string.Empty);
        if (string.IsNullOrWhiteSpace(su)) su = GetPropertySafe(mailItem, MapiPropertyTags.PrConversationTopicA, string.Empty);

        if (string.IsNullOrWhiteSpace(su))
        {
            string hdrs = GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersA, string.Empty);
            if (string.IsNullOrWhiteSpace(hdrs)) hdrs = GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersW, string.Empty);
            if (!string.IsNullOrWhiteSpace(hdrs))
            {
                Match match = SubjectHeaderRegex().Match(hdrs);
                if (match.Success) su = match.Groups[1].Value.Trim();
            }
        }

        return string.IsNullOrWhiteSpace(su) ? "(No Subject)" : su;
    }

    public static string GetItemSender(MailItem? mailItem)
    {
        if (mailItem == null) return "Unknown";

        string smtpSender = GetPropertySafe(mailItem, MapiPropertyTags.PrSenderSmtpAddressW, string.Empty);
        if (string.IsNullOrWhiteSpace(smtpSender)) smtpSender = GetPropertySafe(mailItem, MapiPropertyTags.PrSenderSmtpAddressA, string.Empty);
        if (!string.IsNullOrWhiteSpace(smtpSender) && smtpSender.Contains('@'))
        {
            return smtpSender.Trim();
        }

        string headers = GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersA, string.Empty);
        if (string.IsNullOrWhiteSpace(headers)) headers = GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersW, string.Empty);
        if (!string.IsNullOrWhiteSpace(headers))
        {
            Match fromMatch = FromHeaderRegex().Match(headers);
            if (fromMatch.Success)
            {
                return fromMatch.Groups[1].Value.ToLowerInvariant().Trim();
            }
            Match rpMatch = ReturnPathHeaderRegex().Match(headers);
            if (rpMatch.Success)
            {
                return rpMatch.Groups[1].Value.ToLowerInvariant().Trim();
            }
        }

        string repSmtp = GetPropertySafe(mailItem, MapiPropertyTags.PrSentRepresentingSmtpAddressW, string.Empty);
        if (string.IsNullOrWhiteSpace(repSmtp)) repSmtp = GetPropertySafe(mailItem, MapiPropertyTags.PrSentRepresentingSmtpAddressA, string.Empty);
        if (!string.IsNullOrWhiteSpace(repSmtp) && repSmtp.Contains('@'))
        {
            return repSmtp.Trim();
        }

        try
        {
            string sea = mailItem.SenderEmailAddress;
            if (!string.IsNullOrWhiteSpace(sea) && !sea.Contains("/o=") && sea.Contains('@'))
            {
                return sea.Trim();
            }
        }
        catch { }

        AddressEntry? snd = null;
        try
        {
            snd = mailItem.Sender;
            if (snd != null)
            {
                string resolved = string.Empty;
                ExchangeUser? eu = null;
                try
                {
                    eu = snd.GetExchangeUser();
                    if (eu != null) resolved = eu.PrimarySmtpAddress;
                }
                catch { }
                finally { ComUtils.Release(eu); }

                if (string.IsNullOrWhiteSpace(resolved)) resolved = snd.Address;
                if (!string.IsNullOrWhiteSpace(resolved) && resolved != "Unknown" && !resolved.Contains("/o="))
                {
                    return resolved.Trim();
                }
            }
        }
        catch { }
        finally
        {
            ComUtils.Release(snd);
        }

        string pSender = GetPropertySafe(mailItem, MapiPropertyTags.PrSenderEmailAddressA, string.Empty);
        if (string.IsNullOrWhiteSpace(pSender)) pSender = GetPropertySafe(mailItem, MapiPropertyTags.PrSenderEmailAddressW, string.Empty);
        if (!string.IsNullOrWhiteSpace(pSender) && pSender != "Unknown" && !pSender.Contains("/o="))
        {
            return pSender.Trim();
        }

        try
        {
            string sName = mailItem.SenderName;
            if (!string.IsNullOrWhiteSpace(sName) && sName != "Unknown" && !sName.Contains("/o="))
            {
                return sName.Trim();
            }
        }
        catch { }

        string pName = GetPropertySafe(mailItem, MapiPropertyTags.PrSentRepresentingNameA, string.Empty);
        if (string.IsNullOrWhiteSpace(pName)) pName = GetPropertySafe(mailItem, MapiPropertyTags.PrSentRepresentingNameW, string.Empty);
        if (!string.IsNullOrWhiteSpace(pName) && pName != "Unknown" && !pName.Contains("/o="))
        {
            return pName.Trim();
        }

        return "Unknown";
    }

    public static string GetStableKey(string messageId, string searchKeyHex, string sender, string sentTime, string size)
    {
        if (!string.IsNullOrWhiteSpace(messageId)) return "mid:" + messageId.Trim();
        if (!string.IsNullOrWhiteSpace(searchKeyHex)) return "sk:" + searchKeyHex.Trim().ToUpperInvariant();
        string se = !string.IsNullOrWhiteSpace(sender) ? sender.Trim().ToLowerInvariant() : string.Empty;
        return "h:" + ComputeSha256($"{se}|{sentTime}|{size}");
    }

    public static string GetFingerprint(MailItem? item)
    {
        if (item == null) return Guid.NewGuid().ToString();

        string mid = GetPropertySafe(item, MapiPropertyTags.PrInternetMessageId, string.Empty);
        string sk = string.Empty;

        if (string.IsNullOrWhiteSpace(mid))
        {
            sk = GetPropertySafe(item, MapiPropertyTags.PrSearchKey, string.Empty);
        }

        string sent = string.Empty;
        try { sent = item.SentOn.ToUniversalTime().ToString("yyyyMMddHHmmss"); } catch { }

        string size = string.Empty;
        try { size = item.Size.ToString(); } catch { }

        string se = (string.IsNullOrWhiteSpace(mid) && string.IsNullOrEmpty(sk)) ? GetItemSender(item) : string.Empty;
        return GetStableKey(mid, sk, se, sent, size);
    }

    public static string GetLegacyFingerprint(MailItem? item)
    {
        if (item == null) return string.Empty;
        string se = GetItemSender(item);
        string su = GetItemSubject(item);
        string rt = "00000000000000";
        try { rt = item.ReceivedTime.ToString("yyyyMMddHHmmss"); } catch { }
        return ComputeSha256($"{se}|{su}|{rt}");
    }

    public static string GetItemDna(MailItem? mailItem)
    {
        if (mailItem == null) return string.Empty;
        try
        {
            string msgId = GetPropertySafe(mailItem, MapiPropertyTags.PrInternetMessageId, string.Empty);
            if (!string.IsNullOrEmpty(msgId))
            {
                bool isSent = false;
                MAPIFolder? parent = null;
                Store? store = null;
                MAPIFolder? sentFolder = null;
                try
                {
                    parent = mailItem.Parent as MAPIFolder;
                    if (parent != null)
                    {
                        store = parent.Store;
                        if (store != null)
                        {
                            sentFolder = store.GetDefaultFolder(OlDefaultFolders.olFolderSentMail);
                            if (sentFolder != null && parent.EntryID == sentFolder.EntryID)
                            {
                                isSent = true;
                            }
                        }
                    }
                }
                catch { }
                finally
                {
                    ComUtils.Release(sentFolder);
                    ComUtils.Release(store);
                    ComUtils.Release(parent);
                }

                return ComputeSha256($"mid|{msgId}|{isSent}");
            }

            string subject = GetItemSubject(mailItem);
            string cleanSubject = CleanSubjectRegex().Replace(subject, "").Trim();
            string sender = GetItemSender(mailItem);
            string received = "00000000000000";
            try { received = mailItem.ReceivedTime.ToString("yyyyMMddHHmmss"); } catch { }

            string bodySample = string.Empty;
            try { bodySample = mailItem.Body ?? string.Empty; } catch { }
            if (bodySample.Length > 300) bodySample = bodySample[..300];

            return ComputeSha256($"{sender}|{cleanSubject}|{received}|{bodySample}");
        }
        catch
        {
            return string.Empty;
        }
    }

    public static List<AttachmentInfo> GetAttachmentDetails(MailItem? item, bool withHashes = false)
    {
        List<AttachmentInfo> result = [];
        if (item == null) return result;

        Attachments? atts = null;
        try
        {
            atts = item.Attachments;
            if (atts == null) return result;
            int count = atts.Count;

            for (int i = 1; i <= count; i++)
            {
                Attachment? att = null;
                try
                {
                    att = atts[i];
                    if (att == null) continue;

                    string name = att.FileName ?? $"attachment_{i}";
                    long size = att.Size;
                    string sha256 = "N/A";
                    string md5 = "N/A";

                    if (withHashes && size > 0 && size < 50 * 1024 * 1024) // 50MB limit
                    {
                        string tempFile = Path.Combine(Path.GetTempPath(), $"dg_{Guid.NewGuid():N}.tmp");
                        try
                        {
                            att.SaveAsFile(tempFile);
                            if (File.Exists(tempFile))
                            {
                                byte[] fileBytes = File.ReadAllBytes(tempFile);
                                sha256 = Convert.ToHexStringLower(SHA256.HashData(fileBytes));
                                md5 = Convert.ToHexStringLower(MD5.HashData(fileBytes));
                            }
                        }
                        catch { }
                        finally
                        {
                            if (File.Exists(tempFile))
                            {
                                try { File.Delete(tempFile); } catch { }
                            }
                        }
                    }

                    result.Add(new AttachmentInfo
                    {
                        Name = name,
                        Size = size,
                        Hash = sha256,
                        Md5 = md5,
                        Type = Path.GetExtension(name)
                    });
                }
                catch { }
                finally
                {
                    ComUtils.Release(att);
                }
            }
        }
        catch { }
        finally
        {
            ComUtils.Release(atts);
        }

        return result;
    }

    public static string ExtractSenderIp(string headers)
    {
        if (string.IsNullOrEmpty(headers)) return "N/A";
        Match match = ReceivedIpRegex().Match(headers);
        return match.Success ? match.Groups[1].Value : "N/A";
    }
}
