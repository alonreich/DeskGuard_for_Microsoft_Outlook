namespace DeskGuard.Build;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class GitRelease
{
    public static string DetermineTargetVersion(string repoRoot)
    {
        string changeLogPath = Path.Combine(repoRoot, "docs", "CHANGELOG.md");
        if (!File.Exists(changeLogPath))
        {
            changeLogPath = Path.Combine(repoRoot, "docs", "change.md");
        }

        if (!File.Exists(changeLogPath))
        {
            return "1.0.12";
        }

        string content = File.ReadAllText(changeLogPath);

        // Pattern to match unreleased section
        Match unreleasedMatch = Regex.Match(
            content,
            @"(?m)^##\s*\[Unreleased\]\s*\r?\n(?<notes>[\s\S]*?)(?=\r?\n---\r?\n|\Z)",
            RegexOptions.IgnoreCase);

        string rawNotes = unreleasedMatch.Success ? unreleasedMatch.Groups["notes"].Value.Trim() : string.Empty;

        bool hasSubstantiveNotes = false;
        foreach (string line in rawNotes.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("-") &&
                !trimmed.Equals("- None.", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Equals("- None", StringComparison.OrdinalIgnoreCase))
            {
                hasSubstantiveNotes = true;
                break;
            }
        }

        MatchCollection versionMatches = Regex.Matches(content, @"(?m)^##\s*\[(?<ver>(\d+)\.(\d+)\.(\d+))\]");
        int major = 1, minor = 0, patch = 11;

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

        if (hasSubstantiveNotes)
        {
            patch++;
        }

        return $"{major}.{minor}.{patch}";
    }

    public static bool PublishToGit(string compiledExePath, string version, BuildLog log)
    {
        log.Banner("===========================================================");
        log.Info($"[Git Release] Automated publishing of public release v{version} to git...");
        log.Banner("===========================================================");

        if (!File.Exists(compiledExePath))
        {
            log.Error($"Compiled release binary not found at '{compiledExePath}'");
            return false;
        }

        string repoRoot = Environment.CurrentDirectory;
        string changeLogPath1 = Path.Combine(repoRoot, "docs", "CHANGELOG.md");
        string changeLogPath2 = Path.Combine(repoRoot, "docs", "change.md");

        string notes = PromoteChangelogAndGetNotes(changeLogPath1, version, log);
        if (File.Exists(changeLogPath2))
        {
            PromoteChangelogAndGetNotes(changeLogPath2, version, log);
        }

        string tagName = $"v{version}";
        string releaseTitle = $"DeskGuard v{version}";

        log.Info($"Target Release Version: {version} (Tag: {tagName})");

        // 1. Strict Git staging (only changelog and project documentation files, never scratch/temp files)
        log.Info("[Git] Staging changelog, metadata, and build property files strictly...");
        List<string> filesToStage = ["docs/CHANGELOG.md", "docs/change.md", "project_structure.txt", "Directory.Build.props"];
        foreach (var file in filesToStage)
        {
            if (File.Exists(Path.Combine(repoRoot, file)))
            {
                Cli.RunStreaming("git", ["add", file], log);
            }
        }

        List<string> statusLines = [];
        Cli.RunStreaming("git", ["status", "--porcelain"], log, line => statusLines.Add(line));

        if (statusLines.Count > 0)
        {
            log.Info($"[Git] Committing {statusLines.Count} changes...");
            int commitExit = Cli.RunStreaming("git", ["commit", "-m", $"release: {tagName} - public release packaging"], log);
            if (commitExit != 0)
            {
                log.Warn("[Git] git commit returned non-zero exit code.");
            }
        }
        else
        {
            log.Info("[Git] Working directory clean. No changes to commit.");
        }

        // 2. Tag current commit (force update if tag already exists locally)
        log.Info($"[Git] Creating annotated tag '{tagName}'...");
        int tagExit = Cli.RunStreaming("git", ["tag", "-f", "-a", tagName, "-m", releaseTitle], log);
        if (tagExit != 0)
        {
            log.Error($"[Git] Failed to create git tag '{tagName}'.");
            return false;
        }

        // 3. Push commit and tag to remote
        log.Info("[Git] Pushing commits to remote origin main...");
        int pushCommitExit = Cli.RunStreaming("git", ["push", "origin", "main"], log);
        if (pushCommitExit != 0)
        {
            log.Error("[Git] git push origin main failed with non-zero exit code.");
            return false;
        }

        log.Info($"[Git] Pushing tag '{tagName}' to remote origin...");
        int pushTagExit = Cli.RunStreaming("git", ["push", "origin", tagName, "--force"], log);
        if (pushTagExit != 0)
        {
            log.Error($"[Git] git push origin {tagName} failed with non-zero exit code.");
            return false;
        }

        // 4. Create or update GitHub Release with compiled\DeskGuard.exe using gh CLI
        log.Info("[GitHub] Publishing release asset via GitHub CLI...");
        bool ghSuccess = PublishGitHubRelease(tagName, releaseTitle, compiledExePath, notes, log);
        if (!ghSuccess)
        {
            log.Error("[GitHub] Automated GitHub release publication failed.");
            return false;
        }

        log.Success($"[GitHub] Release {tagName} published with {Path.GetFileName(compiledExePath)}!");

        // 5. Enforce 5-release installer retention policy
        EnforceReleaseRetention(log);

        return true;
    }

    private static string PromoteChangelogAndGetNotes(string changeLogPath, string targetVersion, BuildLog log)
    {
        if (!File.Exists(changeLogPath))
        {
            return $"DeskGuard release v{targetVersion}";
        }

        string content = File.ReadAllText(changeLogPath);

        Match unreleasedMatch = Regex.Match(
            content,
            @"(?m)^##\s*\[Unreleased\]\s*\r?\n(?<notes>[\s\S]*?)(?=\r?\n---\r?\n|\Z)",
            RegexOptions.IgnoreCase);

        string rawNotes = unreleasedMatch.Success ? unreleasedMatch.Groups["notes"].Value.Trim() : string.Empty;

        bool hasSubstantiveNotes = false;
        foreach (string line in rawNotes.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("-") &&
                !trimmed.Equals("- None.", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.Equals("- None", StringComparison.OrdinalIgnoreCase))
            {
                hasSubstantiveNotes = true;
                break;
            }
        }

        if (hasSubstantiveNotes)
        {
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

{rawNotes}";

            string updatedContent = Regex.Replace(
                content,
                @"(?m)^##\s*\[Unreleased\]\s*[\s\S]*?(?=\r?\n---\r?\n|\Z)",
                newReleaseBlock,
                RegexOptions.IgnoreCase);

            File.WriteAllText(changeLogPath, updatedContent, Encoding.UTF8);
            log.Info($"[Changelog] Promoted ## [Unreleased] to ## [{targetVersion}] in {Path.GetFileName(changeLogPath)}");
            return rawNotes;
        }

        Match latestBlockMatch = Regex.Match(
            content,
            @$"(?m)^##\s*\[{Regex.Escape(targetVersion)}\][^\r\n]*\r?\n(?<notes>[\s\S]*?)(?=\r?\n---\r?\n|\Z)",
            RegexOptions.IgnoreCase);

        return latestBlockMatch.Success ? latestBlockMatch.Groups["notes"].Value.Trim() : $"DeskGuard release v{targetVersion}";
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

            int viewExit = Cli.RunStreaming(ghPath, ["release", "view", tagName], log);
            if (viewExit == 0)
            {
                log.Info($"GitHub release '{tagName}' already exists. Updating notes and uploading {Path.GetFileName(exePath)} (clobber)...");
                _ = Cli.RunStreaming(ghPath, ["release", "edit", tagName, "--title", releaseTitle, "-F", tempNotes, "--latest"], log);
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

    public static void EnforceReleaseRetention(BuildLog log)
    {
        string? ghPath = Cli.FindOnPath("gh.exe") ?? Cli.FindOnPath("gh");
        if (string.IsNullOrEmpty(ghPath))
        {
            log.Warn("[Retention] gh CLI not found on PATH; skipping 5-release retention enforcement.");
            return;
        }

        try
        {
            log.Info("[Retention] Enforcing 5-release installer retention policy (FIFO pruning of older binaries)...");

            List<string> jsonOutput = [];
            int listExit = Cli.RunStreaming(
                ghPath,
                ["release", "list", "--limit", "50", "--json", "tagName,publishedAt,isDraft"],
                log,
                line => jsonOutput.Add(line));

            if (listExit != 0 || jsonOutput.Count == 0)
            {
                log.Warn("[Retention] Could not retrieve release list from GitHub.");
                return;
            }

            string fullJson = string.Join("\n", jsonOutput);
            using var doc = JsonDocument.Parse(fullJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;

            var releases = new List<(string Tag, DateTime PublishedAt)>();
            foreach (var elem in doc.RootElement.EnumerateArray())
            {
                bool isDraft = elem.TryGetProperty("isDraft", out var dProp) && dProp.GetBoolean();
                if (isDraft) continue;

                string tag = elem.TryGetProperty("tagName", out var tProp) ? tProp.GetString() ?? "" : "";
                string pubStr = elem.TryGetProperty("publishedAt", out var pProp) ? pProp.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(tag)) continue;

                DateTime pubDate = DateTime.TryParse(pubStr, out var pd) ? pd : DateTime.MinValue;
                releases.Add((tag, pubDate));
            }

            var ordered = releases.OrderByDescending(r => r.PublishedAt).ToList();
            log.Info($"[Retention] Found {ordered.Count} public releases.");

            if (ordered.Count <= 5)
            {
                log.Info($"[Retention] Total releases ({ordered.Count}) <= 5 retention limit. No older binaries to prune.");
                return;
            }

            var toRetain = ordered.Take(5).Select(r => r.Tag).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var olderReleases = ordered.Skip(5).ToList();

            log.Info($"[Retention] Retaining full installer binaries for 5 latest releases: {string.Join(", ", toRetain)}");

            foreach (var (olderTag, _) in olderReleases)
            {
                List<string> assetLines = [];
                int viewExit = Cli.RunStreaming(
                    ghPath,
                    ["release", "view", olderTag, "--json", "assets"],
                    log,
                    line => assetLines.Add(line));

                if (viewExit != 0 || assetLines.Count == 0) continue;

                string assetJson = string.Join("\n", assetLines);
                using var assetDoc = JsonDocument.Parse(assetJson);
                if (!assetDoc.RootElement.TryGetProperty("assets", out var assetsElem) || assetsElem.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var asset in assetsElem.EnumerateArray())
                {
                    string assetName = asset.TryGetProperty("name", out var aProp) ? aProp.GetString() ?? "" : "";
                    if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                        assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                        assetName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    {
                        log.Info($"[Retention] Pruning old binary asset '{assetName}' from older release '{olderTag}' (retaining release notes and tag)...");
                        int delExit = Cli.RunStreaming(ghPath, ["release", "delete-asset", olderTag, assetName, "-y"], log);
                        if (delExit == 0)
                        {
                            log.Success($"[Retention] Successfully pruned '{assetName}' from '{olderTag}'.");
                        }
                        else
                        {
                            log.Warn($"[Retention] Could not delete '{assetName}' from '{olderTag}' (exit code {delExit}).");
                        }
                    }
                }
            }

            log.Success("[Retention] 5-release installer retention policy successfully enforced.");
        }
        catch (Exception ex)
        {
            log.Warn($"[Retention] Exception while enforcing release retention: {ex.Message}");
        }
    }
}
