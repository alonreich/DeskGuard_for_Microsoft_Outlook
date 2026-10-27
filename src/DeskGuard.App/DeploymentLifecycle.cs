namespace DeskGuard.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

internal static class DeploymentLifecycle
{
    public static bool ShouldHandle(string[] args)
    {
        return args.Any(a =>
            a.Equals("--install", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--install-worker", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                if (args.Any(a => a.Equals("--cleanup-worker", StringComparison.OrdinalIgnoreCase)))
                {
                    return await RunUninstallWorkerAsync(args);
                }
                return RunUninstallLauncher(args);
            }

            if (args.Any(a => a.Equals("--install-worker", StringComparison.OrdinalIgnoreCase)))
            {
                return await RunInstallWorkerAsync(args);
            }

            if (args.Any(a => a.Equals("--install", StringComparison.OrdinalIgnoreCase)))
            {
                return RunInstallLauncher(args);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Deployment] Error: {ex}");
            return 1;
        }
    }

    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        WindowsPrincipal principal = new(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int RunInstallLauncher(string[] args)
    {
        string? smartFolder = GetSmartUpdateFolder(args);
        string sourceDir = smartFolder ?? AppContext.BaseDirectory;

        // If not elevated, relaunch with runas to install to Program Files
        if (!IsElevated())
        {
            ProcessStartInfo psi = new()
            {
                FileName = Environment.ProcessPath ?? Path.Combine(sourceDir, DeploymentFootprint.AppExeName),
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--install-worker --source \"{sourceDir}\""
            };

            try
            {
                using Process? p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode ?? 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Elevation request declined or failed: {ex.Message}");
                // Fallback to per-user install without elevation
                return RunInstallWorkerDirect(sourceDir, DeploymentFootprint.PerUserInstallFolder, isPerUser: true).GetAwaiter().GetResult();
            }
        }

        return RunInstallWorkerDirect(sourceDir, DeploymentFootprint.ProgramFilesInstallFolder, isPerUser: false).GetAwaiter().GetResult();
    }

    private static async Task<int> RunInstallWorkerAsync(string[] args)
    {
        string sourceDir = GetArgValue(args, "--source") ?? AppContext.BaseDirectory;
        string targetDir = IsElevated() ? DeploymentFootprint.ProgramFilesInstallFolder : DeploymentFootprint.PerUserInstallFolder;
        bool isPerUser = !IsElevated();
        return await RunInstallWorkerDirect(sourceDir, targetDir, isPerUser);
    }

    private static bool ValidatePayloadIntegrity(string sourceDir, out Dictionary<string, string> validatedFiles)
    {
        validatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string manifestPath = Path.Combine(sourceDir, "update.json");

        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using FileStream stream = File.OpenRead(manifestPath);
            using JsonDocument doc = JsonDocument.Parse(stream);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("files", out JsonElement filesElement) ||
                filesElement.ValueKind != JsonValueKind.Object)
            {
                Console.Error.WriteLine("[Deployment] Invalid manifest: 'files' element missing or not an object.");
                return false;
            }

            foreach (JsonProperty prop in filesElement.EnumerateObject())
            {
                string relPath = prop.Name.Replace('/', Path.DirectorySeparatorChar);
                if (relPath.Contains("..") || Path.IsPathRooted(relPath))
                {
                    Console.Error.WriteLine($"[Deployment] Security error: Illegal path traversal in manifest: {relPath}");
                    return false;
                }

                string? expectedHash = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(expectedHash))
                {
                    Console.Error.WriteLine($"[Deployment] Security error: Empty hash for {relPath}");
                    return false;
                }

                string fullSourcePath = Path.Combine(sourceDir, relPath);
                if (!File.Exists(fullSourcePath))
                {
                    Console.Error.WriteLine($"[Deployment] Security error: Manifest file missing on disk: {relPath}");
                    return false;
                }

                using FileStream fileStream = File.OpenRead(fullSourcePath);
                byte[] actualHashBytes = SHA256.HashData(fileStream);
                string actualHash = Convert.ToHexStringLower(actualHashBytes);

                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"[Deployment] Hash mismatch for {relPath}! Expected: {expectedHash}, Actual: {actualHash}");
                    return false;
                }

                validatedFiles[relPath] = fullSourcePath;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Deployment] Failed to validate manifest: {ex.Message}");
            return false;
        }
    }

    private static async Task<int> RunInstallWorkerDirect(string sourceDir, string targetDir, bool isPerUser)
    {
        Console.WriteLine($"[Deployment] Installing to: {targetDir}");

        bool hasManifest = File.Exists(Path.Combine(sourceDir, "update.json"));
        Dictionary<string, string> filesToInstall;

        if (hasManifest)
        {
            if (!ValidatePayloadIntegrity(sourceDir, out filesToInstall))
            {
                Console.Error.WriteLine("[Deployment] Cryptographic verification failed! Installation aborted.");
                return 2;
            }
            Console.WriteLine($"[Deployment] Verified {filesToInstall.Count} payload files against cryptographic manifest.");
        }
        else
        {
            filesToInstall = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDir, file);
                if (relative.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                filesToInstall[relative] = file;
            }
        }

        TerminateRunningAppInstances(targetDir);

        Directory.CreateDirectory(targetDir);

        // Copy files with retry to guarantee reliable overwrites during upgrade
        foreach (var (relPath, sourceFile) in filesToInstall)
        {
            string destFile = Path.Combine(targetDir, relPath);
            CopyFileWithRetry(sourceFile, destFile);
        }

        if (hasManifest)
        {
            try
            {
                CopyFileWithRetry(Path.Combine(sourceDir, "update.json"), Path.Combine(targetDir, "update.json"));
            }
            catch { }
        }

        string targetExe = Path.Combine(targetDir, DeploymentFootprint.AppExeName);

        // Create shortcuts
        try
        {
            string startMenuDir = isPerUser
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");

            string desktopDir = isPerUser
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

            string startMenuLnk = Path.Combine(startMenuDir, $"{DeploymentFootprint.DisplayName}.lnk");
            string desktopLnk = Path.Combine(desktopDir, $"{DeploymentFootprint.DisplayName}.lnk");

            await ShellLinkWriter.CreateAsync(startMenuLnk, targetExe, targetDir, targetExe, DeploymentFootprint.DisplayName);
            await ShellLinkWriter.CreateAsync(desktopLnk, targetExe, targetDir, targetExe, DeploymentFootprint.DisplayName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Deployment] Warning creating shortcuts: {ex.Message}");
        }

        // Register in Windows Add/Remove Programs
        try
        {
            RegistryKey root = isPerUser ? Registry.CurrentUser : Registry.LocalMachine;
            using RegistryKey key = root.CreateSubKey(DeploymentFootprint.UninstallKeyPath);
            key.SetValue("DisplayName", DeploymentFootprint.DisplayName);
            key.SetValue("DisplayVersion", "1.0.0");
            key.SetValue("Publisher", DeploymentFootprint.Publisher);
            key.SetValue("InstallLocation", targetDir);
            key.SetValue("DisplayIcon", targetExe);
            key.SetValue("UninstallString", $"\"{targetExe}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{targetExe}\" --uninstall --quiet");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Deployment] Warning writing registry keys: {ex.Message}");
        }

        Console.WriteLine("[Deployment] Installation completed successfully.");

        // Start installed application reliably as medium-integrity interactive user
        LaunchInstalledApp(targetExe, targetDir);

        return 0;
    }

    private static void CopyFileWithRetry(string source, string dest, int maxRetries = 6)
    {
        string? destDir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                File.Copy(source, dest, overwrite: true);
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (i < maxRetries - 1)
            {
                Thread.Sleep(500);
            }
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Shell.Application COM object instantiation")]
    private static void LaunchInstalledApp(string targetExe, string targetDir)
    {
        try
        {
            // Use Shell.Application to launch at Medium Integrity via Windows Explorer desktop session
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType != null)
            {
                dynamic? shell = Activator.CreateInstance(shellType);
                if (shell != null)
                {
                    shell.ShellExecute(targetExe, "", targetDir, "open", 1);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Deployment] Shell.Application launch error: {ex.Message}");
        }

        try
        {
            ProcessStartInfo startApp = new(targetExe)
            {
                WorkingDirectory = targetDir,
                UseShellExecute = true
            };
            Process.Start(startApp);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Deployment] Process.Start fallback error: {ex.Message}");
        }
    }

    private static void TerminateRunningAppInstances(string targetDir)
    {
        int currentPid = Environment.ProcessId;
        string tempDir = Path.GetTempPath();

        foreach (Process proc in Process.GetProcessesByName("DeskGuard"))
        {
            using (proc)
            {
                if (proc.Id == currentPid) continue;
                try
                {
                    string? exePath = null;
                    try { exePath = proc.MainModule?.FileName; } catch { }

                    // Never terminate installer instances running from Temp
                    if (!string.IsNullOrEmpty(exePath) && exePath.StartsWith(tempDir, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(exePath) &&
                        (exePath.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase) ||
                         exePath.StartsWith(DeploymentFootprint.ProgramFilesInstallFolder, StringComparison.OrdinalIgnoreCase) ||
                         exePath.StartsWith(DeploymentFootprint.PerUserInstallFolder, StringComparison.OrdinalIgnoreCase)))
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(5000);
                    }
                }
                catch { }
            }
        }
    }

    private static int RunUninstallLauncher(string[] args)
    {
        string? appDir = Path.GetDirectoryName(Environment.ProcessPath);
        bool isInProgramFiles = appDir != null && appDir.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

        if (isInProgramFiles && !IsElevated())
        {
            ProcessStartInfo psi = new()
            {
                FileName = Environment.ProcessPath!,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--uninstall --cleanup-worker"
            };
            try
            {
                using Process? p = Process.Start(psi);
                p?.WaitForExit();
                return p?.ExitCode ?? 0;
            }
            catch
            {
                return 1;
            }
        }

        return RunUninstallWorkerAsync(args).GetAwaiter().GetResult();
    }

    private static async Task<int> RunUninstallWorkerAsync(string[] args)
    {
        Console.WriteLine("[Deployment] Starting uninstallation...");

        // Remove shortcuts
        foreach (Environment.SpecialFolder folder in new[] {
            Environment.SpecialFolder.StartMenu,
            Environment.SpecialFolder.CommonStartMenu,
            Environment.SpecialFolder.DesktopDirectory,
            Environment.SpecialFolder.CommonDesktopDirectory })
        {
            try
            {
                string baseDir = Environment.GetFolderPath(folder);
                if (folder is Environment.SpecialFolder.StartMenu or Environment.SpecialFolder.CommonStartMenu)
                {
                    baseDir = Path.Combine(baseDir, "Programs");
                }
                string lnk = Path.Combine(baseDir, $"{DeploymentFootprint.DisplayName}.lnk");
                if (File.Exists(lnk)) File.Delete(lnk);
            }
            catch { }
        }

        // Remove registry keys
        foreach (RegistryKey root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try { root.DeleteSubKeyTree(DeploymentFootprint.UninstallKeyPath, throwOnMissingSubKey: false); } catch { }
        }

        // Self cleanup
        string? runningDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(runningDir) &&
            (runningDir.Equals(DeploymentFootprint.ProgramFilesInstallFolder, StringComparison.OrdinalIgnoreCase) ||
             runningDir.Equals(DeploymentFootprint.PerUserInstallFolder, StringComparison.OrdinalIgnoreCase)))
        {
            ScheduleDirectoryDeleteOnExit(runningDir);
        }

        Console.WriteLine("[Deployment] Uninstallation finished.");
        await Task.CompletedTask;
        return 0;
    }

    private static void ScheduleDirectoryDeleteOnExit(string dir)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/C timeout /t 2 /nobreak > NUL & rmdir /S /Q \"{dir}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
        }
        catch { }
    }

    private static string? GetSmartUpdateFolder(string[] args)
    {
        int idx = Array.FindIndex(args, a => a.Equals("--smart-update", StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < args.Length)
        {
            return Path.GetFullPath(args[idx + 1]);
        }
        return null;
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        int idx = Array.FindIndex(args, a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < args.Length)
        {
            return args[idx + 1];
        }
        return null;
    }
}
