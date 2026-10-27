namespace DeskGuard.Core.Tests;

using System.Threading.Tasks;
using DeskGuard.Core.Concurrency;
using DeskGuard.Core.Scoring;
using Xunit;

public sealed class ConcurrencyAndParserTests
{
    [Fact]
    public void EmlParser_CorrectlyExtractsHeadersAndBody()
    {
        string rawEml = "From: sender@domain.com\r\nSubject: Test Subject\r\nX-Custom: Multi-line\r\n header value\r\n\r\nHello Body";
        ParsedEml parsed = EmlParser.Parse(rawEml);

        Assert.Equal("sender@domain.com", parsed.Headers["From"]);
        Assert.Equal("Test Subject", parsed.Headers["Subject"]);
        Assert.Equal("Multi-line header value", parsed.Headers["X-Custom"]);
        Assert.Equal("Hello Body", parsed.Body);
    }

    [Fact]
    public void PriorityJobQueue_AntiStarvation_WorksCorrectly()
    {
        PriorityJobQueue<string> queue = new(starvationLimit: 2);

        queue.Enqueue("P0-1", JobPriority.P0);
        queue.Enqueue("P0-2", JobPriority.P0);
        queue.Enqueue("P0-3", JobPriority.P0);
        queue.Enqueue("P2-1", JobPriority.P2);

        // Dequeue 1: P0-1
        Assert.True(queue.TryDequeue(out string? item1));
        Assert.Equal("P0-1", item1);

        // Dequeue 2: P0-2
        Assert.True(queue.TryDequeue(out string? item2));
        Assert.Equal("P0-2", item2);

        // Dequeue 3: Anti-starvation triggers after 2 high-priority runs -> P2-1 is yielded!
        Assert.True(queue.TryDequeue(out string? item3));
        Assert.Equal("P2-1", item3);

        // Dequeue 4: P0-3
        Assert.True(queue.TryDequeue(out string? item4));
        Assert.Equal("P0-3", item4);
    }

    [Fact]
    public void PriorityJobQueue_TryDequeueHighPriority_SelectsOnlyHighPriority()
    {
        PriorityJobQueue<string> queue = new();

        queue.Enqueue("P3-Item", JobPriority.P3);
        queue.Enqueue("P2-Item", JobPriority.P2);
        queue.Enqueue("P1-Item", JobPriority.P1);
        queue.Enqueue("P0-Item", JobPriority.P0);

        Assert.True(queue.HasHighPriorityJobs);
        Assert.True(queue.TryDequeueHighPriority(out string? first));
        Assert.Equal("P0-Item", first);

        Assert.True(queue.HasHighPriorityJobs);
        Assert.True(queue.TryDequeueHighPriority(out string? second));
        Assert.Equal("P1-Item", second);

        // No more high priority jobs (only P2 and P3 remain)
        Assert.False(queue.HasHighPriorityJobs);
        Assert.False(queue.TryDequeueHighPriority(out string? third));
        Assert.Null(third);

        // Standard dequeue retrieves P2 and P3
        Assert.True(queue.TryDequeue(out string? fourth));
        Assert.Equal("P2-Item", fourth);
        Assert.True(queue.TryDequeue(out string? fifth));
        Assert.Equal("P3-Item", fifth);
    }

    [Fact]
    public void ScanConfiguration_AtomicSaveAndLoad_MaintainsIntegrity()
    {
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"DeskGuard_CfgTest_{System.Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(tempDir);
        string cfgFile = System.IO.Path.Combine(tempDir, "config.json");

        try
        {
            var config = new DeskGuard.Core.Models.ScanConfiguration
            {
                Enabled = true,
                ScanningSpeed = 75,
                SpamDestination = "junk"
            };

            config.Save(cfgFile);
            Assert.True(System.IO.File.Exists(cfgFile));

            var loaded = DeskGuard.Core.Models.ScanConfiguration.Load(cfgFile);
            Assert.Equal(75, loaded.ScanningSpeed);
            Assert.Equal("junk", loaded.SpamDestination);
            Assert.True(loaded.Enabled);
        }
        finally
        {
            try { System.IO.Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}
