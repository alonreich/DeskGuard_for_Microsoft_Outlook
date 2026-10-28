namespace DeskGuard.Core.Tests;

using System.IO;
using System.Threading.Tasks;
using DeskGuard.Core.Models;
using DeskGuard.Core.Services;
using Xunit;

public sealed class LocalAntivirusServiceTests
{
    [Fact]
    public void FindDefenderPath_OnWindows_ReturnsValidPathIfExists()
    {
        string? defenderPath = LocalAntivirusService.FindDefenderPath();
        if (defenderPath != null)
        {
            Assert.True(File.Exists(defenderPath));
            Assert.EndsWith("MpCmdRun.exe", defenderPath);
        }
    }

    [Fact]
    public async Task ScanEmailAsync_DisassemblesAndEvaluatesCleanEmail()
    {
        LocalAntivirusService service = new();
        EmailRecord email = new()
        {
            EntryId = "test-entry-id-1",
            Headers = "From: sender@trusted.com\r\nSubject: Test Clean\r\n",
            Body = "Hello world, this is a clean business email.",
            Subject = "Test Clean"
        };

        var report = await service.ScanEmailAsync(email);

        Assert.NotNull(report);
        Assert.Equal("test-entry-id-1", report.EmailId);
        if (report.DefenderAvailable)
        {
            Assert.True(report.Success);
            Assert.Equal("Clean", report.OverallVerdict);
            Assert.NotEmpty(report.Pieces);
        }
    }
}
