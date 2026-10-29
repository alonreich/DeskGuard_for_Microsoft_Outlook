namespace DeskGuard.Build;

using System;
using System.IO;

internal static class InnoInstaller
{
    public const string ScriptPath = @"build\installer\DeskGuard.iss";
    public const string SetupFileName = "DeskGuard.exe";
    private const string IconPath = @"assets\icons\DeskGuard.ico";

    public static bool Build(string buildVersion, string stagingDir, string signedListDir, string outputDir, BuildLog log)
    {
        log.Info("[Installer] Building the Inno Setup installer (NODROPPER_01)...");
        string? compiler = FindCompiler();
        if (compiler is null)
        {
            log.Error("ERROR: Inno Setup compiler (ISCC.exe) was not found.");
            log.Error("       Install via: winget install JRSoftware.InnoSetup");
            log.Error("       or set DESKGUARD_ISCC to the full path of ISCC.exe.");
            return false;
        }

        foreach (string required in new[] {
            ScriptPath,
            IconPath,
            Path.Combine(stagingDir, "DeskGuard.exe"),
            Path.Combine(signedListDir, "update.json") })
        {
            if (!File.Exists(required))
            {
                log.Error($"ERROR: [Installer] input missing: {required}");
                return false;
            }
        }

        Directory.CreateDirectory(outputDir);
        string output = Path.Combine(outputDir, SetupFileName);
        if (File.Exists(output)) File.Delete(output);

        int exit = Cli.RunStreaming(compiler,
        [
            "/Qp",
            "/DAppVersion=" + buildVersion,
            "/DStagingDir=" + Path.GetFullPath(stagingDir),
            "/DSignedListDir=" + Path.GetFullPath(signedListDir),
            "/DIconFile=" + Path.GetFullPath(IconPath),
            "/DOutputDir=" + Path.GetFullPath(outputDir),
            Path.GetFullPath(ScriptPath),
        ], log);

        if (exit != 0 || !File.Exists(output))
        {
            log.Error($"ERROR: Inno Setup failed (exit code {exit}). The installer was not produced.");
            return false;
        }

        log.Success($"[Installer] {SetupFileName}: {new FileInfo(output).Length / (1024 * 1024)} MB.");
        Signing.SignBinary(output, log);
        return true;
    }

    private static string? FindCompiler()
    {
        string? configured = Environment.GetEnvironmentVariable("DESKGUARD_ISCC") ?? Environment.GetEnvironmentVariable("FVS_ISCC");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;

        string? onPath = Cli.FindOnPath("ISCC.exe");
        if (onPath is not null) return onPath;

        foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles })
        {
            string candidate = Path.Combine(Environment.GetFolderPath(folder), "Inno Setup 6", "ISCC.exe");
            if (File.Exists(candidate)) return candidate;
        }

        string perUser = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Inno Setup 6", "ISCC.exe");
        return File.Exists(perUser) ? perUser : null;
    }
}
