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
}
