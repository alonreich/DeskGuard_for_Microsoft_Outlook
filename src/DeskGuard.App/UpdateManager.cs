namespace DeskGuard.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Logging;
using DeskGuard.Core.Models;
using DeskGuard.Core.Security;

public sealed class UpdateCheckResult
{
    public bool HasUpdate { get; init; }
    public string CurrentVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string PublishedAt { get; init; } = string.Empty;
    public bool IsManual { get; init; }
    public string? Error { get; init; }
}

public sealed class UpdateManager : IDisposable
{
    private const string GitHubApiUrl = "https://api.github.com/repos/alonreich/DeskGuard_for_Microsoft_Outlook/releases/latest";
    private readonly HttpClient _http;
    private readonly ScanConfiguration _config;
    private readonly Timer? _backgroundTimer;
    private bool _isDisposed;
    private int _isChecking;

    public event Action<UpdateCheckResult>? UpdateAvailable;

    public static Version CurrentVersion
    {
        get
        {
            var asmVer = typeof(UpdateManager).Assembly.GetName().Version;
            if (asmVer != null && asmVer.Major > 0)
            {
                return asmVer;
            }
            return new Version(1, 0, 12, 0);
        }
    }

    public static string CurrentVersionString
    {
        get
        {
            var v = CurrentVersion;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public UpdateManager(ScanConfiguration config)
    {
        _config = config;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DeskGuard-Updater", CurrentVersionString));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));

        if (_config.AutoCheckUpdates)
        {
            // First check after 15 seconds, then every 4 hours
            _backgroundTimer = new Timer(
                callback: _ => _ = BackgroundCheckAsync(),
                state: null,
                dueTime: TimeSpan.FromSeconds(15),
                period: TimeSpan.FromHours(4));
        }
    }

    private async Task BackgroundCheckAsync()
    {
        if (_isDisposed || !_config.AutoCheckUpdates) return;
        try
        {
            var result = await CheckForUpdatesAsync(manual: false, CancellationToken.None);
            if (result.HasUpdate)
            {
                AppLogger.Info($"[UpdateManager] Background check found new version: v{result.LatestVersion}");
                UpdateAvailable?.Invoke(result);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[UpdateManager] Background update check failed: {ex.Message}");
        }
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool manual = false, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _isChecking, 1, 0) != 0)
        {
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = CurrentVersionString,
                IsManual = manual,
                Error = "Update check is already in progress."
            };
        }

        try
        {
            AppLogger.Info($"[UpdateManager] Checking for updates (current: v{CurrentVersionString}, manual: {manual})...");

            using var response = await _http.GetAsync(GitHubApiUrl, ct);
            if (!response.IsSuccessStatusCode)
            {
                string errMsg = $"GitHub API returned {(int)response.StatusCode} {response.ReasonPhrase}";
                AppLogger.Warn($"[UpdateManager] {errMsg}");
                return new UpdateCheckResult
                {
                    HasUpdate = false,
                    CurrentVersion = CurrentVersionString,
                    IsManual = manual,
                    Error = errMsg
                };
            }

            string json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tagElem) ? tagElem.GetString() ?? "" : "";
            string releaseName = root.TryGetProperty("name", out var nameElem) ? nameElem.GetString() ?? "" : "";
            string releaseNotes = root.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
            string publishedAt = root.TryGetProperty("published_at", out var pubElem) ? pubElem.GetString() ?? "" : "";

            string rawVersion = tagName.TrimStart('v', 'V').Trim();
            if (!TryParseSemVer(rawVersion, out Version? latestVer) || latestVer == null)
            {
                return new UpdateCheckResult
                {
                    HasUpdate = false,
                    CurrentVersion = CurrentVersionString,
                    IsManual = manual,
                    Error = $"Unrecognized release version tag '{tagName}'."
                };
            }

            string latestVersionStr = $"{latestVer.Major}.{latestVer.Minor}.{latestVer.Build}";
            bool hasUpdate = latestVer > CurrentVersion;

            string downloadUrl = "";
            if (root.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assetsElem.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out var aName) ? aName.GetString() ?? "" : "";
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var dlElem) ? dlElem.GetString() ?? "" : "";
                        if (string.Equals(assetName, "DeskGuard.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            break; // Preferred installer asset
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(downloadUrl))
            {
                downloadUrl = root.TryGetProperty("html_url", out var htmlElem) ? htmlElem.GetString() ?? "" : "";
            }

            AppLogger.Info($"[UpdateManager] Update check completed. Latest: v{latestVersionStr}, HasUpdate: {hasUpdate}");

            return new UpdateCheckResult
            {
                HasUpdate = hasUpdate,
                CurrentVersion = CurrentVersionString,
                LatestVersion = latestVersionStr,
                ReleaseName = releaseName,
                ReleaseNotes = CleanReleaseNotes(releaseNotes),
                DownloadUrl = downloadUrl,
                PublishedAt = publishedAt,
                IsManual = manual
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = CurrentVersionString,
                IsManual = manual,
                Error = "Update check was cancelled or timed out."
            };
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Warn($"[UpdateManager] Network error during update check: {ex.Message}");
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = CurrentVersionString,
                IsManual = manual,
                Error = "Unable to connect to update server. Please verify your internet connection."
            };
        }
        catch (Exception ex)
        {
            AppLogger.Error("[UpdateManager] Unexpected error during update check", ex);
            return new UpdateCheckResult
            {
                HasUpdate = false,
                CurrentVersion = CurrentVersionString,
                IsManual = manual,
                Error = ex.Message
            };
        }
        finally
        {
            Interlocked.Exchange(ref _isChecking, 0);
        }
    }

    public async Task<(bool Success, string FilePath, string? Error)> DownloadAndVerifyUpdateAsync(
        string downloadUrl,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return (false, "", "No valid download URL provided.");
        }

        string tempInstaller = Path.Combine(Path.GetTempPath(), $"DeskGuard_Setup_{Guid.NewGuid():N}.exe");
        try
        {
            AppLogger.Info($"[UpdateManager] Downloading update installer from {downloadUrl} to {tempInstaller}...");
            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(tempInstaller, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            byte[] buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
                if (totalBytes > 0 && progress != null)
                {
                    int pct = (int)((totalRead * 100) / totalBytes);
                    progress.Report(pct);
                }
            }

            await fileStream.FlushAsync(ct);
            fileStream.Close();

            AppLogger.Info($"[UpdateManager] Download complete ({totalRead} bytes). Verifying Authenticode signature...");

            // Verify Authenticode signature before execution
            bool isSigned = AuthenticodeVerifier.VerifyAuthenticodeSignature(tempInstaller);
            if (!isSigned)
            {
                AppLogger.Error("[UpdateManager] Security verification failed: Downloaded update binary is NOT signed by DeskGuard trusted certificate!");
                try { File.Delete(tempInstaller); } catch { }
                return (false, "", "Update package failed security signature verification. Installation aborted.");
            }

            AppLogger.Info("[UpdateManager] Authenticode signature verified successfully.");
            return (true, tempInstaller, null);
        }
        catch (Exception ex)
        {
            AppLogger.Error("[UpdateManager] Failed to download or verify update", ex);
            try { if (File.Exists(tempInstaller)) File.Delete(tempInstaller); } catch { }
            return (false, "", ex.Message);
        }
    }

    public static string GetLocalWhatsNew(string? targetVersion = null)
    {
        string ver = targetVersion ?? CurrentVersionString;
        try
        {
            string changelogPath = Path.Combine(AppContext.BaseDirectory, "docs", "CHANGELOG.md");
            if (!File.Exists(changelogPath))
            {
                // Fallback to checking up directory hierarchy
                string cur = AppContext.BaseDirectory;
                for (int i = 0; i < 5; i++)
                {
                    string candidate = Path.Combine(cur, "docs", "CHANGELOG.md");
                    if (File.Exists(candidate))
                    {
                        changelogPath = candidate;
                        break;
                    }
                    var parent = Directory.GetParent(cur);
                    if (parent == null) break;
                    cur = parent.FullName;
                }
            }

            if (File.Exists(changelogPath))
            {
                string text = File.ReadAllText(changelogPath);
                // Extract section for target version or unreleased
                string headerPattern = $"## [{ver}]";
                int idx = text.IndexOf(headerPattern, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    int endIdx = text.IndexOf("\n## [", idx + headerPattern.Length, StringComparison.OrdinalIgnoreCase);
                    if (endIdx < 0) endIdx = text.Length;
                    string section = text.Substring(idx, endIdx - idx).Trim();
                    return section;
                }
            }
        }
        catch { }

        return $"## [{ver}]\n\n- General performance improvements and security threat detection enhancements.";
    }

    public static void LaunchInstaller(string installerPath)
    {
        AppLogger.Info($"[UpdateManager] Launching installer: {installerPath}");
        ProcessStartInfo psi = new(installerPath)
        {
            UseShellExecute = true
        };
        Process.Start(psi);
    }

    public static void OpenReleaseUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[UpdateManager] Could not open URL: {ex.Message}");
        }
    }

    public static bool TryParseSemVer(string raw, out Version? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        string clean = raw.TrimStart('v', 'V').Trim();
        int dashIdx = clean.IndexOf('-');
        if (dashIdx > 0) clean = clean.Substring(0, dashIdx);

        var parts = clean.Split('.');
        if (parts.Length == 2) clean += ".0";
        if (parts.Length == 3) clean += ".0";

        return Version.TryParse(clean, out version);
    }

    private static string CleanReleaseNotes(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "No release notes available.";
        return markdown.Trim();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _backgroundTimer?.Dispose();
        _http.Dispose();
    }
}
