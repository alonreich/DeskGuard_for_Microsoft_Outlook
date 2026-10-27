namespace DeskGuard.Build;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Staging
{
    private const string ProjectFile = @"src\DeskGuard.App\DeskGuard.App.csproj";
    private const string OutputExe = InnoInstaller.SetupFileName;
    private const string OutputDir = @"compiled";
    private const string StagingDir = @"obj\StandaloneTemp\Staging";
    private const string InstallerListDir = @"obj\StandaloneTemp\InstallerList";

    public static string CompiledExePath => Path.Combine(OutputDir, OutputExe);
    public static string CompiledDirPath => OutputDir;
    public static string StagingDirPath => StagingDir;
    public static string InstallerListDirPath => InstallerListDir;

    private static readonly string[] IlcInternalCrashMarkers =
    [
        "ILCompiler.NativeAotFatalErrorException",
        "ILCompiler.CodeGenerationFailedException",
        "error IL1013",
    ];

    public static void TerminateProcesses(BuildLog log)
    {
        string root = Path.GetFullPath(Environment.CurrentDirectory).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                string? executable;
                try { executable = process.MainModule?.FileName; }
                catch { continue; }

                if (executable is null || !executable.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    log.Info($"  -> Stopping {Path.GetFileName(executable)} (PID {process.Id}): it runs from this repository.");
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    log.Warn($"  -> Could not stop PID {process.Id}: {ex.Message}");
                }
            }
        }
    }

    public static void PrepareCompiledDir(BuildLog log, bool devMode)
    {
        Directory.CreateDirectory(OutputDir);
        foreach (string entry in Directory.EnumerateFileSystemEntries(OutputDir))
        {
            if (!string.Equals(Path.GetFileName(entry), OutputExe, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(entry);
                TryDeleteFile(entry);
            }
        }
        if (!devMode)
        {
            TryDeleteFile(Path.Combine(OutputDir, OutputExe));
        }
    }

    public static void PurgeTempArtifacts(BuildLog log)
    {
        log.Info("[NativeAOT] 1. Purging old temp folders...");
        TryDeleteDirectory(StagingDir);
        TryDeleteDirectory(InstallerListDir);
        TryDeleteDirectory(@"obj\StandaloneTemp\NativeAot_final");
    }

    public static bool Publish(string outputDir, string buildVersion, BuildLog log)
    {
        log.Info("[NativeAOT] 2. Publishing DeskGuard.App to staging...");
        if (RunPublish(outputDir, buildVersion, log, singleThreadedIlc: false, out bool ilcCrashed))
        {
            return true;
        }
        if (!ilcCrashed)
        {
            return false;
        }

        log.Warn("[NativeAOT] ILC (the NativeAOT compiler) crashed internally - known .NET 9 race (dotnet/runtime#108743).");
        log.Warn("[NativeAOT] Retrying publish once with single-threaded ILC scanner (-p:IlcSingleThreaded=true)...");
        return RunPublish(outputDir, buildVersion, log, singleThreadedIlc: true, out _);
    }

    private static bool RunPublish(string outputDir, string buildVersion, BuildLog log, bool singleThreadedIlc, out bool ilcCrashed)
    {
        bool crashed = false;
        List<string> arguments =
        [
            "publish", ProjectFile,
            "-c", "Release",
            "-r", "win-x64",
            "-p:TreatWarningsAsErrors=true",
            "-p:PublishAot=true",
            "-p:SelfContained=true",
            "-p:AssemblyVersion=" + buildVersion,
            "-p:Version=" + buildVersion,
            "-p:FileVersion=" + buildVersion,
            "-p:InformationalVersion=" + buildVersion,
            "-nodeReuse:false",
            "-p:UseSharedCompilation=false",
            "-o", outputDir,
            "-v", "m",
        ];
        if (singleThreadedIlc)
        {
            arguments.Add("-p:IlcSingleThreaded=true");
        }

        int exit = Cli.RunStreaming("dotnet", arguments, log, line =>
        {
            foreach (string marker in IlcInternalCrashMarkers)
            {
                if (line.Contains(marker, StringComparison.Ordinal)) { crashed = true; break; }
            }
        });
        ilcCrashed = crashed;
        return exit == 0;
    }

    public static bool WriteManifests(string buildVersion, BuildLog log)
    {
        log.Info("[Manifest] Generating SHA-256 file manifest (update.json)...");
        Directory.CreateDirectory(InstallerListDir);

        Dictionary<string, string> files = new(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(StagingDir, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (ext.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteFile(file);
                continue;
            }

            string relative = Path.GetRelativePath(StagingDir, file).Replace('\\', '/');
            using FileStream stream = File.OpenRead(file);
            byte[] hash = SHA256.HashData(stream);
            files[relative] = Convert.ToHexStringLower(hash);
        }

        using MemoryStream ms = new();
        using (Utf8JsonWriter writer = new(ms, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteString("version", buildVersion);
            writer.WriteString("createdUtc", DateTime.UtcNow.ToString("o"));
            writer.WriteStartObject("files");
            foreach (var kvp in files)
            {
                writer.WriteString(kvp.Key, kvp.Value);
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        File.WriteAllBytes(Path.Combine(InstallerListDir, "update.json"), ms.ToArray());
        log.Success($"[Manifest] Indexed {files.Count} files in update.json.");
        return true;
    }

    public static bool ValidateCompiledOutput(BuildLog log)
    {
        string exe = Path.Combine(OutputDir, OutputExe);
        if (!File.Exists(exe))
        {
            log.Error($"ERROR: Expected output not found: {exe}");
            return false;
        }

        int count = 0;
        foreach (string file in Directory.EnumerateFiles(OutputDir))
        {
            count++;
            if (!string.Equals(Path.GetFileName(file), OutputExe, StringComparison.OrdinalIgnoreCase))
            {
                log.Error($"ERROR: Unexpected sidecar file in compiled folder: {file}");
                return false;
            }
        }

        if (count != 1)
        {
            log.Error($"ERROR: Expected exactly 1 file in {OutputDir}, but found {count}.");
            return false;
        }

        log.Success($"[Validation] Exactly one file verified in {OutputDir}: {OutputExe}");
        return true;
    }

    public static void CleanupTempArtifacts(BuildLog log)
    {
        log.Info("[Cleanup] Removing temporary staging directories...");
        TryDeleteDirectory(StagingDir);
        TryDeleteDirectory(InstallerListDir);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
