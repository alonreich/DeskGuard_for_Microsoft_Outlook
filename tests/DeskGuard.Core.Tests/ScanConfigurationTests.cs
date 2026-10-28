namespace DeskGuard.Core.Tests;

using DeskGuard.Core.Models;
using Xunit;

public sealed class ScanConfigurationTests
{
    [Fact]
    public void Dpapi_RoundTrip_EncryptsAndDecryptsSuccessfully()
    {
        string rawSecret = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef";
        string encrypted = ScanConfiguration.EncryptSecret(rawSecret);

        Assert.StartsWith("dpapi:", encrypted);
        Assert.NotEqual(rawSecret, encrypted);

        string decrypted = ScanConfiguration.DecryptSecret(encrypted);
        Assert.Equal(rawSecret, decrypted);
    }

    [Fact]
    public void CreateExportPackage_RedactsSecretsAndWindowCoords()
    {
        ScanConfiguration cfg = new()
        {
            VtKey = "my-secret-vt-key-value",
            WindowX = 500,
            WindowY = 400,
            ScanningSpeed = 75,
            Whitelist = ["trusted@company.com"],
            Blacklist = ["spammer@evil.com"]
        };

        ScanConfiguration exportPkg = cfg.CreateExportPackage();

        Assert.Equal(string.Empty, exportPkg.VtKey);
        Assert.Equal(-1, exportPkg.WindowX);
        Assert.Equal(-1, exportPkg.WindowY);
        Assert.Equal(75, exportPkg.ScanningSpeed);
        Assert.Contains("trusted@company.com", exportPkg.Whitelist);
        Assert.Contains("spammer@evil.com", exportPkg.Blacklist);
    }

    [Fact]
    public void ImportSettings_PreservesSecretsAndWindowPositions()
    {
        ScanConfiguration original = new()
        {
            VtKey = "existing-secret-key",
            WindowX = 120,
            WindowY = 150,
            ScanningSpeed = 10,
            Whitelist = ["old@company.com"]
        };

        ScanConfiguration imported = new()
        {
            VtKey = "", // Redacted in import
            WindowX = 0,
            WindowY = 0,
            ScanningSpeed = 85,
            Whitelist = ["new@company.com"],
            SpamKeywords = ["urgent transfer"]
        };

        original.ImportSettings(imported);

        // Secret and window positions are preserved
        Assert.Equal("existing-secret-key", original.VtKey);
        Assert.Equal(120, original.WindowX);
        Assert.Equal(150, original.WindowY);

        // Policies and rules are updated
        Assert.Equal(85, original.ScanningSpeed);
        Assert.Contains("new@company.com", original.Whitelist);
        Assert.Contains("urgent transfer", original.SpamKeywords);
    }

    [Fact]
    public void NewDefaults_IncludeThemeAndHistoryScope()
    {
        ScanConfiguration cfg = new();
        Assert.Equal("dark", cfg.Theme);
        Assert.Equal("past7days", cfg.HistoryScanScope);
        Assert.Equal(10, cfg.ScanningSpeed);
    }
}
