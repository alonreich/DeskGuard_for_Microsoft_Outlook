namespace DeskGuard.Build;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

internal static class Cli
{
    public static int RunStreaming(string fileName, IEnumerable<string> args, BuildLog log, Action<string>? onLine = null)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };
        foreach (string arg in args) startInfo.ArgumentList.Add(arg);

        using Process process = new() { StartInfo = startInfo };
        DateTime lastActivity = DateTime.UtcNow;
        DateTime startTime = DateTime.UtcNow;
        object sync = new();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (sync) { lastActivity = DateTime.UtcNow; }
                log.Info(e.Data);
                onLine?.Invoke(e.Data);
                if (e.Data.Contains("Generating native code", StringComparison.OrdinalIgnoreCase))
                {
                    log.Info("  -> [NativeAOT] Compiling native machine code via ilc.exe / link.exe (~30-60s expected)...");
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (sync) { lastActivity = DateTime.UtcNow; }
                log.Info(e.Data);
                onLine?.Invoke(e.Data);
            }
        };

        process.Start();
        try { process.StandardInput.Close(); } catch { }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using Timer heartbeat = new(_ =>
        {
            try
            {
                if (process.HasExited) return;
                DateTime now = DateTime.UtcNow;
                TimeSpan silence;
                TimeSpan total;
                lock (sync)
                {
                    silence = now - lastActivity;
                    total = now - startTime;
                }
                if (silence.TotalSeconds >= 15)
                {
                    string name = Path.GetFileName(fileName);
                    log.Info($"  -> [Active] Process '{name}' is still running ({total.TotalSeconds:F0}s elapsed)...");
                }
            }
            catch { }
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));

        process.WaitForExit();
        return process.ExitCode;
    }

    public static string? FindOnPath(string binaryName)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        foreach (string raw in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(raw.Trim(), binaryName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
