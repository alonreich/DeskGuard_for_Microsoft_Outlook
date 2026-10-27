namespace DeskGuard.Build;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

internal static class GitRelease
{
    public static bool PublishToGit(string compiledExePath, BuildLog log)
    {
        log.Banner("===========================================================");
        log.Info("[Git Release] Automated publishing of public release to git...");
        log.Banner("===========================================================");

        if (!File.Exists(compiledExePath))
        {
            log.Error($"Compiled release binary not found at '{compiledExePath}'");
            return false;
        }

        string repoRoot = Environment.CurrentDirectory;
        string changeLogPath = Path.Combine(repoRoot, "docs", "change.md");

        var (version, notes) = PromoteChangelogAndGetVersion(changeLogPath, log);
        string tagName = $"v{version}";
        string releaseTitle = $"DeskGuard v{version}";

        log.Info($"Target Release Version: {version} (Tag: {tagName})");

        // 1. Stage and commit updated changelog and any pending files
        log.Info("[Git] Staging all files...");
        Cli.RunStreaming("git", ["add", "-A"], log);

        List<string> statusLines = [];
        Cli.RunStreaming("git", ["status", "--porcelain"], log, line => statusLines.Add(line));

        if (statusLines.Count > 0)
        {
            log.Info($"[Git] Committing {statusLines.Count} changes...");
            Cli.RunStreaming("git", ["commit", "-m", $"release: {tagName} - public release packaging"], log);
        }
        else
        {
            log.Info("[Git] Working directory clean. No changes to commit.");
        }

        // 2. Tag current commit (force update if tag already exists locally)
        log.Info($"[Git] Creating annotated tag '{tagName}'...");
        Cli.RunStreaming("git", ["tag", "-f", "-a", tagName, "-m", releaseTitle], log);

        // 3. Push commit and tag to remote
        log.Info("[Git] Pushing commits to remote origin main...");
        int pushCommitExit = Cli.RunStreaming("git", ["push", "origin", "main"], log);
        if (pushCommitExit != 0)
        {
            log.Warn("[Git] git push origin main returned non-zero exit code.");
        }

        log.Info($"[Git] Pushing tag '{tagName}' to remote origin...");
        int pushTagExit = Cli.RunStreaming("git", ["push", "origin", tagName, "--force"], log);
        if (pushTagExit != 0)
        {
            log.Warn($"[Git] git push origin {tagName} returned non-zero exit code.");
        }

        // 4. Create or update GitHub Release with compiled\\DeskGuard.exe using gh CLI
        log.Info("[GitHub] Publishing release asset via GitHub CLI...");
        bool ghSuccess = PublishGitHubRelease(tagName, releaseTitle, compiledExePath, notes, log);

        if (ghSuccess)
        {
            log.Success($"[GitHub] Release {tagName} published with {Path.GetFileName(compiledExePath)}!");
        }
        else
        {
            log.Warn("[GitHub] gh CLI publication failed or gh not authenticated. Git tag was pushed.");
        }

        return true;
    }

    private static (string Version, string Notes) PromoteChangelogAndGetVersion(string changeLogPath, BuildLog log)
    {
        if (!File.Exists(changeLogPath))
        {
            return ("1.0.3", $"DeskGuard release {DateTime.UtcNow:yyyy-MM-dd}");
        }

        string content = File.ReadAllText(changeLogPath);

        // Pattern to match unreleased section
        Match unreleasedMatch = Regex.Match(
            content,
            @"##\s*\[Unreleased\]\s*(?<notes>[\s\S]*?)(?=\r?\n---\r?\n\s*##\s*\[|\r?\n##\s*\[|$)",
            RegexOptions.IgnoreCase);

        string rawNotes = unreleasedMatch.Success ? unreleasedMatch.Groups["notes"].Value.Trim() : string.Empty;

        // Check if rawNotes has substantive content (not just empty or None)
        bool hasSubstantiveNotes = !string.IsNullOrWhiteSpace(rawNotes) &&
                                  !Regex.IsMatch(rawNotes, @"^(?:###\s+\w+\r?\n\s*-\s*None\.?\s*)+$", RegexOptions.IgnoreCase);

        // Find existing versions
        MatchCollection versionMatches = Regex.Matches(content, @"##\s*\[(?<ver>(\d+)\.(\d+)\.(\d+))\]");
        int major = 1, minor = 0, patch = 2;

        if (versionMatches.Count > 0)
        {
            string latestVerStr = versionMatches[0].Groups["ver"].Value;
            var parts = latestVerStr.Split('.');
            if (parts.Length == 3 && int.TryParse(parts[0], out int mj) && int.TryParse(parts[1], out int mn) && int.TryParse(parts[2], out int pt))
            {
                major = mj;
                minor = mn;
                patch = pt;
            }
        }

        string targetVersion;
        string finalNotes;

        if (hasSubstantiveNotes)
        {
            patch++;
            targetVersion = $"{major}.{minor}.{patch}";
            finalNotes = rawNotes;

            string dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            string newReleaseBlock = $@"## [Unreleased]

### Added
- None.

### Changed
- None.

### Fixed
- None.

---

## [{targetVersion}] - {dateStr}

{finalNotes}";

            string updatedContent = Regex.Replace(
                content,
                @"##\s*\[Unreleased\]\s*[\s\S]*?(?=\r?\n---\r?\n\s*##\s*\[|\r?\n##\s*\[|$)",
                newReleaseBlock,
                RegexOptions.IgnoreCase);

            File.WriteAllText(changeLogPath, updatedContent, Encoding.UTF8);
            log.Info($"[Changelog] Promoted ## [Unreleased] to ## [{targetVersion}] in docs/change.md");
        }
        else
        {
            targetVersion = $"{major}.{minor}.{patch}";
            Match latestBlockMatch = Regex.Match(
                content,
                $@"##\s*\[{Regex.Escape(targetVersion)}\][^\r\n]*\r?\n(?<notes>[\s\S]*?)(?=\r?\n---\r?\n\s*##\s*\[|\r?\n##\s*\[|$)",
                RegexOptions.IgnoreCase);
            finalNotes = latestBlockMatch.Success ? latestBlockMatch.Groups["notes"].Value.Trim() : $"DeskGuard release v{targetVersion}";
        }

        return (targetVersion, finalNotes);
    }

    private static bool PublishGitHubRelease(string tagName, string releaseTitle, string exePath, string notes, BuildLog log)
    {
        string? ghPath = Cli.FindOnPath("gh.exe") ?? Cli.FindOnPath("gh");
        if (string.IsNullOrEmpty(ghPath))
        {
            log.Warn("gh CLI was not found on PATH; skipping automated GitHub release upload.");
            return false;
        }

        string tempNotes = Path.Combine(Path.GetTempPath(), $"dg_release_notes_{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(tempNotes, $"{releaseTitle}\n\n{notes}");

            // Check if release already exists on GitHub
            int viewExit = Cli.RunStreaming(ghPath, ["release", "view", tagName], log);
            if (viewExit == 0)
            {
                log.Info($"GitHub release '{tagName}' already exists. Uploading {Path.GetFileName(exePath)} (clobber)...");
                int uploadExit = Cli.RunStreaming(ghPath, ["release", "upload", tagName, exePath, "--clobber"], log);
                return uploadExit == 0;
            }
            else
            {
                log.Info($"Creating new GitHub release '{tagName}' with asset {Path.GetFileName(exePath)}...");
                int createExit = Cli.RunStreaming(
                    ghPath,
                    ["release", "create", tagName, exePath, "--title", releaseTitle, "-F", tempNotes, "--latest"],
                    log);
                return createExit == 0;
            }
        }
        catch (Exception ex)
        {
            log.Warn($"Exception while publishing GitHub release: {ex.Message}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempNotes)) File.Delete(tempNotes); } catch { }
        }
    }
}
