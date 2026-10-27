namespace DeskGuard.Data.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DeskGuard.Core.Models;
using DeskGuard.Data;
using Xunit;

public sealed class VaultRepositoryTests : IDisposable
{
    private readonly string _tempDbDir;
    private readonly VaultRepository _repository;

    public VaultRepositoryTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), $"DeskGuard_Test_{Guid.NewGuid():N}");
        _repository = new VaultRepository(_tempDbDir, readOnly: false);
    }

    [Fact]
    public void Base64SnapshotHealing_DecodesCleanBase64Strings()
    {
        string original = "This is a legitimate raw MIME snapshot header body.";
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(original));

        string decoded = VaultRepository.DecodeMaybeBase64(encoded);
        Assert.Equal(original, decoded);

        // Plain text should remain untouched
        string plain = "Regular plain text message";
        Assert.Equal(plain, VaultRepository.DecodeMaybeBase64(plain));
    }

    [Fact]
    public void InsertEmail_AndGetById_RetrievesAccurateRecord()
    {
        EmailRecord record = new()
        {
            EntryId = "EID_12345",
            StoreId = "STORE_01",
            Fingerprint = "mid:12345@corp.com",
            Subject = "Q4 Financial Forecast",
            Sender = "cfo@corp.com",
            Recipient = "board@corp.com",
            Body = "Confidential financial results enclosed.",
            Headers = "From: cfo@corp.com\nTo: board@corp.com\n",
            Verdict = "Safe",
            Score = 100.0,
            Unread = true,
            CurrentFolder = "Inbox",
            MessageId = "12345@corp.com",
            Size = 2048
        };

        _repository.InsertEmail(record);

        EmailRecord? retrieved = _repository.GetEmailById("EID_12345");
        Assert.NotNull(retrieved);
        Assert.Equal("EID_12345", retrieved.EntryId);
        Assert.Equal("Q4 Financial Forecast", retrieved.Subject);
        Assert.Equal("cfo@corp.com", retrieved.Sender);
        Assert.Equal("Safe", retrieved.Verdict);
        Assert.Equal(100.0, retrieved.Score);
        Assert.True(retrieved.Unread);
        Assert.Equal("Inbox", retrieved.CurrentFolder);
    }

    [Fact]
    public void FullTextSearch_MatchesTerms_AndReturnsSnippets()
    {
        EmailRecord email1 = new()
        {
            EntryId = "EID_FTS_1",
            Subject = "Urgent: Project Titan Deadline",
            Sender = "pm@agency.com",
            Recipient = "dev@agency.com",
            Body = "Please submit the final delivery by Friday noon.",
            Verdict = "Safe"
        };

        EmailRecord email2 = new()
        {
            EntryId = "EID_FTS_2",
            Subject = "Quarterly Newsletter",
            Sender = "news@agency.com",
            Recipient = "all@agency.com",
            Body = "General company updates and holiday schedules.",
            Verdict = "Safe"
        };

        _repository.InsertEmail(email1);
        _repository.InsertEmail(email2);

        SearchResult search = _repository.SearchEmails(new SearchParameters
        {
            Query = "Titan delivery"
        });

        Assert.Equal(1, search.Total);
        Assert.Single(search.Rows);
        Assert.Equal("EID_FTS_1", search.Rows[0].EntryId);
        Assert.Contains("<mark>delivery</mark>", search.Rows[0].Snippet);
    }

    [Fact]
    public void DuplicateDiscovery_ElectsOptimalSurvivor()
    {
        string sharedDna = "mid:duplicate_test_01@corp.com";

        // Duplicate in Deleted / Junk
        EmailRecord dupJunk = new()
        {
            EntryId = "EID_DUP_JUNK",
            Fingerprint = sharedDna,
            Subject = "Quarterly Review",
            Sender = "boss@corp.com",
            FolderKind = "junk",
            Size = 1000,
            AttachmentsJson = "[]"
        };

        // Survivor in Inbox with attachment
        EmailRecord dupInbox = new()
        {
            EntryId = "EID_DUP_INBOX",
            Fingerprint = sharedDna,
            Subject = "Quarterly Review",
            Sender = "boss@corp.com",
            FolderKind = "inbox",
            Size = 5000,
            AttachmentsJson = "[{\"name\":\"review.pdf\",\"size\":4000}]"
        };

        _repository.InsertIndexed(dupJunk);
        _repository.InsertIndexed(dupInbox);

        var (duplicates, indexed) = _repository.FindDuplicates(includeJunk: true);

        Assert.Single(duplicates);
        DuplicateItem duplicate = duplicates[0];
        Assert.Equal("EID_DUP_JUNK", duplicate.EntryId);
        Assert.Equal("EID_DUP_INBOX", duplicate.SurvivorId);
    }

    [Fact]
    public void ReconcileFolder_SkipsHiding_WhenOverHalfMissingInLargeFolder()
    {
        string folder = "LargeFolder";
        for (int i = 0; i < 25; i++)
        {
            _repository.InsertIndexed(new EmailRecord
            {
                EntryId = $"EID_LF_{i}",
                Fingerprint = $"fp_{i}",
                CurrentFolder = folder
            });
        }

        // Only 5 present out of 25 (20 missing > 50% threshold)
        HashSet<string> present = ["EID_LF_0", "EID_LF_1", "EID_LF_2", "EID_LF_3", "EID_LF_4"];
        var (hidden, skipped) = _repository.ReconcileFolder(string.Empty, folder, present);

        Assert.True(skipped);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void GetStats_AggregatesCategoriesCorrectly()
    {
        _repository.InsertEmail(new EmailRecord { EntryId = "S1", Verdict = "Safe", Score = 100 });
        _repository.InsertEmail(new EmailRecord { EntryId = "S2", Verdict = "Safe", Score = 95 });
        _repository.InsertEmail(new EmailRecord { EntryId = "M1", Verdict = "Malicious", Score = 10 });
        _repository.InsertEmail(new EmailRecord { EntryId = "P1", Verdict = "Spam", Score = 35 });
        _repository.InsertEmail(new EmailRecord { EntryId = "U1", Verdict = "Suspicious", Score = 50 });

        EngineStats stats = _repository.GetStats();

        Assert.Equal(5, stats.Total);
        Assert.Equal(2, stats.Safe);
        Assert.Equal(1, stats.Malicious);
        Assert.Equal(1, stats.Spam);
        Assert.Equal(1, stats.Suspicious);
    }

    [Fact]
    public void GetAllCategorizedEmails_EnforcesDefaultLimitPerCategory()
    {
        for (int i = 0; i < 150; i++)
        {
            _repository.InsertEmail(new EmailRecord
            {
                EntryId = $"Safe_{i}",
                Verdict = "Safe",
                Score = 100,
                Date = "2026-10-27",
                Time = "12:00:00"
            });
        }

        var categorized = _repository.GetAllCategorizedEmails();
        Assert.NotNull(categorized);
        Assert.True(categorized.ContainsKey("safe"));
        // Default limit is 100 per category
        Assert.Equal(100, categorized["safe"].Count);
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentReadsAndWrites_ExecuteSimultaneouslyWithoutBlocking()
    {
        using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
        var token = cts.Token;

        var writeTask = System.Threading.Tasks.Task.Run(() =>
        {
            int i = 0;
            while (!token.IsCancellationRequested && i < 100)
            {
                _repository.InsertEmail(new EmailRecord
                {
                    EntryId = $"Conc_Write_{i}",
                    Fingerprint = $"fp_conc_{i}",
                    Subject = $"Subject {i}",
                    Sender = $"sender{i}@corp.com",
                    Verdict = "Safe",
                    Score = 95.0
                });
                i++;
            }
        }, token);

        var readTask = System.Threading.Tasks.Task.Run(() =>
        {
            int iterations = 0;
            while (!token.IsCancellationRequested && iterations < 50)
            {
                var stats = _repository.GetStats();
                var search = _repository.SearchEmails(new SearchParameters { Query = "Subject" });
                var dups = _repository.FindDuplicates();
                iterations++;
            }
        }, token);

        await System.Threading.Tasks.Task.WhenAll(writeTask, readTask);
        Assert.True(_repository.GetStats().Total > 0);
    }

    public void Dispose()
    {
        _repository.Dispose();
        try
        {
            if (Directory.Exists(_tempDbDir))
            {
                Directory.Delete(_tempDbDir, recursive: true);
            }
        }
        catch { }
    }
}
