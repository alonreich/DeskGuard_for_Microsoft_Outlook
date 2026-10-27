namespace DeskGuard.Build;

using System;
using System.IO;
using System.Linq;

internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitBuildFailed = 1;

    private static int Main(string[] args)
    {
        bool devMode = args.Any(a => a.Equals("--dev", StringComparison.OrdinalIgnoreCase));
        bool stageOnly = args.Any(a => a.Equals("--stage-only", StringComparison.OrdinalIgnoreCase));

        if (args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("Usage: DeskGuard.Build [--dev] [--stage-only]");
            Console.WriteLine("  (default)     Standard production build: Native AOT compile + Inno Setup installer");
            Console.WriteLine("  --dev         Local dev build: .\\compiled is not wiped first");
            Console.WriteLine("  --stage-only  Diagnostic: publish and write manifests, then stop");
            return ExitOk;
        }

        Environment.CurrentDirectory = FindRepoRoot();
        DateTimeOffset started = DateTimeOffset.Now;
        using BuildLog log = new("build.log");

        try
        {
            string buildVersion = DateTime.UtcNow.ToString("yyyy.M.d.HHmm");
            log.Banner("===========================================================");
            log.Banner($"BUILDING DeskGuard for Microsoft Outlook (v{buildVersion})");
            log.Banner("===========================================================");

            log.Info("[Pre-build] Checking running processes...");
            Staging.TerminateProcesses(log);

            if (!args.Any(a => a.Equals("--no-test", StringComparison.OrdinalIgnoreCase)))
            {
                log.Info("[Pre-build] Running unit test suite...");
                int testExit = Cli.RunStreaming("dotnet", ["test", "DeskGuard.sln", "-c", "Release", "--no-restore"], log);
                if (testExit != 0)
                {
                    log.Error("ERROR: Pre-build unit test suite failed.");
                    log.WriteVerdictAndClose(ExitBuildFailed, DateTimeOffset.Now - started);
                    return ExitBuildFailed;
                }
            }

            Staging.PrepareCompiledDir(log, devMode);
            Staging.PurgeTempArtifacts(log);

            if (!Staging.Publish(Staging.StagingDirPath, buildVersion, log))
            {
                log.Error("ERROR: Native AOT compilation failed.");
                log.WriteVerdictAndClose(ExitBuildFailed, DateTimeOffset.Now - started);
                return ExitBuildFailed;
            }

            if (!Staging.WriteManifests(buildVersion, log))
            {
                log.Error("ERROR: Manifest generation failed.");
                log.WriteVerdictAndClose(ExitBuildFailed, DateTimeOffset.Now - started);
                return ExitBuildFailed;
            }

            if (stageOnly)
            {
                log.Success($"STAGE-ONLY SUCCESS: Staged output in {Staging.StagingDirPath}");
                log.WriteVerdictAndClose(ExitOk, DateTimeOffset.Now - started);
                return ExitOk;
            }

            if (!InnoInstaller.Build(buildVersion, Staging.StagingDirPath, Staging.InstallerListDirPath, Staging.CompiledDirPath, log))
            {
                log.Error("ERROR: Inno Setup packaging failed.");
                log.WriteVerdictAndClose(ExitBuildFailed, DateTimeOffset.Now - started);
                return ExitBuildFailed;
            }

            if (!Staging.ValidateCompiledOutput(log))
            {
                log.Error("ERROR: Compiled output validation failed.");
                log.WriteVerdictAndClose(ExitBuildFailed, DateTimeOffset.Now - started);
                return ExitBuildFailed;
            }

            Staging.CleanupTempArtifacts(log);

            log.Banner("===========================================================");
            log.Success("SUCCESS: DeskGuard build completed successfully!");
            log.Info($"Installer: {Staging.CompiledExePath}");
            log.Banner("===========================================================");

            log.WriteVerdictAndClose(ExitOk, DateTimeOffset.Now - started);
            return ExitOk;
        }
        catch (Exception ex)
        {
            log.Error($"FATAL BUILD ERROR: {ex}");
            log.WriteVerdictAndClose(ExitBuildFailed, DateTimeOffset.Now - started);
            return ExitBuildFailed;
        }
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DeskGuard.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return Environment.CurrentDirectory;
    }
}
