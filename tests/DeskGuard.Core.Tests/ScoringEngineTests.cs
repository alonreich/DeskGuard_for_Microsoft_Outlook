namespace DeskGuard.Core.Tests;

using System.Collections.Generic;
using System.Linq;
using DeskGuard.Core.Models;
using DeskGuard.Core.Scoring;
using Xunit;

public sealed class ScoringEngineTests
{
    private readonly ScoringEngine _engine = new();

    [Fact]
    public void CleanEmail_Scores100_WithZeroPenalties()
    {
        EmailRecord email = new()
        {
            Sender = "alice@example.com",
            Headers = "Received-SPF: pass\nAuthentication-Results: dkim=pass; dmarc=pass\n",
            Body = "Hello, here is the weekly report."
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        Assert.Equal(100.0, result.Score);
        Assert.Equal("Safe", result.Verdict);
        Assert.Empty(result.Penalties);
        Assert.Equal(0.0, result.TotalDeduction);
    }

    [Fact]
    public void LinkMismatch_PenalizesHiddenRedirectDomain()
    {
        EmailRecord email = new()
        {
            Sender = "billing@service.com",
            Headers = "Received-SPF: pass\n",
            Body = "<p>Please verify your account at <a href=\"https://evil-phishing-login.ru/login\">www.paypal.com</a></p>"
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        SecurityPenalty? penalty = result.Penalties.FirstOrDefault(p => p.Rule == "LINK_MISMATCH");
        Assert.NotNull(penalty);
        Assert.Equal(8.0, penalty.Points);
        Assert.Contains("paypal.com", penalty.Evidence);
        Assert.Contains("evil-phishing-login.ru", penalty.Evidence);
    }

    [Fact]
    public void DisplayNameSpoofing_FlagsBrandMismatch()
    {
        EmailRecord email = new()
        {
            Sender = "\"PayPal Support\" <security-alert@randomphish.xyz>",
            Headers = "Received-SPF: pass\n",
            Body = "Your account has been locked."
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        SecurityPenalty? penalty = result.Penalties.FirstOrDefault(p => p.Rule == "DISPLAY_NAME_SPOOF");
        Assert.NotNull(penalty);
        Assert.Equal(7.0, penalty.Points);
        Assert.Contains("paypal", penalty.Evidence);
    }

    [Fact]
    public void MsScl9_GivesFull10PointPenalty()
    {
        EmailRecord email = new()
        {
            Sender = "user@domain.com",
            Headers = "X-MS-Exchange-Organization-SCL: 9\n",
            Body = "Hello world"
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        SecurityPenalty? penalty = result.Penalties.FirstOrDefault(p => p.Rule == "MS_SPAM_SCORE");
        Assert.NotNull(penalty);
        Assert.Equal(10.0, penalty.Points);
    }

    [Fact]
    public void MsScl5_Gives7PointPenalty()
    {
        EmailRecord email = new()
        {
            Sender = "user@domain.com",
            Headers = "X-MS-Exchange-Organization-SCL: 5\n",
            Body = "Hello world"
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        SecurityPenalty? penalty = result.Penalties.FirstOrDefault(p => p.Rule == "MS_SPAM_SCORE");
        Assert.NotNull(penalty);
        Assert.Equal(7.0, penalty.Points);
    }

    [Fact]
    public void ReplyTo_FreeWebmailMismatch_GivesFullPenalty()
    {
        EmailRecord email = new()
        {
            Sender = "support@corporate.com",
            Headers = "Reply-To: refund-department@gmail.com\n",
            Body = "Please reply with your banking details."
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        SecurityPenalty? penalty = result.Penalties.FirstOrDefault(p => p.Rule == "REPLYTO_MISMATCH");
        Assert.NotNull(penalty);
        Assert.Equal(3.0, penalty.Points);
    }

    [Fact]
    public void LookalikeDomain_FlagsTypoHomoglyphDomain()
    {
        EmailRecord email = new()
        {
            Sender = "alerts@paypa1.top",
            Headers = "Received-SPF: pass\n",
            Body = "Security alert"
        };

        ScanConfiguration config = new();
        config.KnownDomains["paypal.com"] = 50;

        SecurityVerdict result = _engine.ScoreEmail(email, config);

        SecurityPenalty? penalty = result.Penalties.FirstOrDefault(p => p.Rule == "LOOKALIKE_DOMAIN");
        Assert.NotNull(penalty);
        Assert.Equal(5.0, penalty.Points);
    }

    [Fact]
    public void DangerousAttachment_CapsScoreAt10_VerdictMalicious()
    {
        EmailRecord email = new()
        {
            Sender = "invoice@legit.com",
            Headers = "Received-SPF: pass\n",
            Body = "See attached statement",
            AttachmentsJson = "[{\"name\":\"statement.exe\",\"size\":1024}]"
        };

        SecurityVerdict result = _engine.ScoreEmail(email);

        Assert.Contains(result.Penalties, p => p.Rule == "DANGEROUS_ATTACHMENT");
        Assert.True(result.Score <= 10.0);
        Assert.Equal("Malicious", result.Verdict);
    }

    [Fact]
    public void WhatIf_WeightAdjustment_RecalculatesDeterministicScore()
    {
        EmailRecord email = new()
        {
            Sender = "alerts@paypal-security.org",
            Headers = "X-MS-Exchange-Organization-SCL: 9\n",
            Body = "Please verify your credentials."
        };

        // Baseline: default weight for msscl is 10 -> score 90
        SecurityVerdict baseline = _engine.ScoreEmail(email);
        Assert.Equal(90.0, baseline.Score);

        // What-if: slider turned up to 25
        ScanConfiguration whatIfConfig = new();
        whatIfConfig.Weights["msscl"] = 25.0;

        SecurityVerdict whatIf = _engine.ScoreEmail(email, whatIfConfig);
        Assert.Equal(75.0, whatIf.Score);

        // What-if: toggle disabled
        ScanConfiguration disabledConfig = new();
        disabledConfig.Toggles["msscl"] = false;

        SecurityVerdict disabled = _engine.ScoreEmail(email, disabledConfig);
        Assert.Empty(disabled.Penalties);
        Assert.Equal(100.0, disabled.Score);
    }
}
