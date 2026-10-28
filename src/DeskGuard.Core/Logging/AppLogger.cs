namespace DeskGuard.Core.Logging;

using System;
using System.IO;

public static class AppLogger
{
    private static readonly object _sync = new();
    private static string? _logFilePath;

    public static event Action<string>? OnLog;

    public static void Initialize(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            _logFilePath = Path.Combine(logDirectory, "deskguard.log");
            Log("=== DeskGuard Logging Initialized ===", "INFO");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AppLogger] Initialization failed: {ex.Message}");
        }
    }

    public static void Log(string message, string level = "INFO")
    {
        string timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string formatted = $"[{timestamp}] [{level}] {message}";

        Console.WriteLine(formatted);
        OnLog?.Invoke(formatted);

        if (_logFilePath == null) return;

        lock (_sync)
        {
            try
            {
                File.AppendAllText(_logFilePath, formatted + Environment.NewLine);
            }
            catch
            {
                // Never crash application due to logging I/O failure
            }
        }
    }

    public static void Info(string message) => Log(message, "INFO");

    public static void Warn(string message) => Log(message, "WARN");

    public static void Error(string message, Exception? ex = null)
    {
        string detail = ex != null
            ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}"
            : message;
        Log(detail, "ERROR");
    }
}
