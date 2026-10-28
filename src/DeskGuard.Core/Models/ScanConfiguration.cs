namespace DeskGuard.Core.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DeskGuard.Core.Serialization;

public sealed partial class ScanConfiguration
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    private const uint CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    [LibraryImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(
        in DATA_BLOB pDataIn,
        IntPtr szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [LibraryImport("Crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(
        in DATA_BLOB pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        uint dwFlags,
        out DATA_BLOB pDataOut);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr LocalFree(IntPtr hMem);

    public bool Enabled { get; set; } = true;
    public bool OnAccessEnabled { get; set; } = true;
    public bool HistoryScanEnabled { get; set; } = true;
    public bool DeepHistoryScanEnabled { get; set; } = false;
    public int OnDemandLimit { get; set; } = 0;
    public Dictionary<string, double> Weights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> Toggles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ThresholdSettings Thresholds { get; set; } = new();
    public Dictionary<string, int> KnownDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Whitelist { get; set; } = [];
    public List<string> Blacklist { get; set; } = [];
    public List<string> SpamKeywords { get; set; } = [];
    public List<string> Rubrics { get; set; } = [];
    public int ThreatIntelligenceLevel { get; set; } = 1;
    public int ScanningSpeed { get; set; } = 10;
    public string SpamDestination { get; set; } = "quarantine"; // "quarantine" or "junk"
    public bool LabelInOutlook { get; set; } = true;
    public string VtKey { get; set; } = string.Empty;
    public int WindowX { get; set; } = -1;
    public int WindowY { get; set; } = -1;
    public int WindowWidth { get; set; } = 1500;
    public int WindowHeight { get; set; } = 900;
    public bool WindowMaximized { get; set; } = false;
    public Dictionary<string, string> ModalStates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Theme { get; set; } = "dark"; // "dark", "light", "system"
    public string HistoryScanScope { get; set; } = "past7days"; // "unread", "past7days", "all"

    public static string EncryptSecret(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return string.Empty;
        if (plain.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) return plain;
        byte[] bytes = Encoding.UTF8.GetBytes(plain.Trim());
        IntPtr inPtr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, inPtr, bytes.Length);
            DATA_BLOB inBlob = new() { cbData = bytes.Length, pbData = inPtr };
            if (CryptProtectData(in inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB outBlob))
            {
                try
                {
                    byte[] outBytes = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
                    return "dpapi:" + Convert.ToBase64String(outBytes);
                }
                finally
                {
                    LocalFree(outBlob.pbData);
                }
            }
        }
        catch
        {
        }
        finally
        {
            Marshal.FreeHGlobal(inPtr);
        }
        return plain;
    }

    public static string DecryptSecret(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted)) return string.Empty;
        if (!encrypted.StartsWith("dpapi:", StringComparison.OrdinalIgnoreCase)) return encrypted;
        try
        {
            string b64 = encrypted["dpapi:".Length..];
            byte[] bytes = Convert.FromBase64String(b64);
            IntPtr inPtr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, inPtr, bytes.Length);
                DATA_BLOB inBlob = new() { cbData = bytes.Length, pbData = inPtr };
                if (CryptUnprotectData(in inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out DATA_BLOB outBlob))
                {
                    try
                    {
                        byte[] outBytes = new byte[outBlob.cbData];
                        Marshal.Copy(outBlob.pbData, outBytes, 0, outBlob.cbData);
                        return Encoding.UTF8.GetString(outBytes);
                    }
                    finally
                    {
                        LocalFree(outBlob.pbData);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(inPtr);
            }
        }
        catch
        {
        }
        return string.Empty;
    }

    public static ScanConfiguration Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var cfg = JsonSerializer.Deserialize(json, DeskGuardJsonContext.Default.ScanConfiguration);
                    if (cfg != null)
                    {
                        cfg.VtKey = DecryptSecret(cfg.VtKey);
                        return cfg;
                    }
                }
            }
        }
        catch { }

        return new ScanConfiguration();
    }

    public void Save(string filePath)
    {
        string? tempPath = null;
        string originalKey = this.VtKey;
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            this.VtKey = EncryptSecret(this.VtKey);
            string json = JsonSerializer.Serialize(this, DeskGuardJsonContext.Default.ScanConfiguration);

            tempPath = Path.Combine(dir ?? ".", $"{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
        finally
        {
            this.VtKey = originalKey;
        }
    }

    public ScanConfiguration CreateExportPackage()
    {
        return new ScanConfiguration
        {
            Enabled = this.Enabled,
            OnAccessEnabled = this.OnAccessEnabled,
            HistoryScanEnabled = this.HistoryScanEnabled,
            DeepHistoryScanEnabled = this.DeepHistoryScanEnabled,
            Weights = new Dictionary<string, double>(this.Weights, StringComparer.OrdinalIgnoreCase),
            Toggles = new Dictionary<string, bool>(this.Toggles, StringComparer.OrdinalIgnoreCase),
            Thresholds = new ThresholdSettings
            {
                SpamThresholdPercent = this.Thresholds.SpamThresholdPercent,
                MaliciousThresholdPercent = this.Thresholds.MaliciousThresholdPercent,
                SuspiciousThresholdPercent = this.Thresholds.SuspiciousThresholdPercent
            },
            Whitelist = [.. this.Whitelist],
            Blacklist = [.. this.Blacklist],
            SpamKeywords = [.. this.SpamKeywords],
            ThreatIntelligenceLevel = this.ThreatIntelligenceLevel,
            ScanningSpeed = this.ScanningSpeed,
            SpamDestination = this.SpamDestination,
            LabelInOutlook = this.LabelInOutlook,
            Theme = this.Theme,
            HistoryScanScope = this.HistoryScanScope,
            VtKey = string.Empty
        };
    }

    public void ImportSettings(ScanConfiguration imported)
    {
        if (imported == null) return;
        this.Enabled = imported.Enabled;
        this.OnAccessEnabled = imported.OnAccessEnabled;
        this.HistoryScanEnabled = imported.HistoryScanEnabled;
        this.DeepHistoryScanEnabled = imported.DeepHistoryScanEnabled;
        this.Weights = new Dictionary<string, double>(imported.Weights, StringComparer.OrdinalIgnoreCase);
        this.Toggles = new Dictionary<string, bool>(imported.Toggles, StringComparer.OrdinalIgnoreCase);
        if (imported.Thresholds != null)
        {
            this.Thresholds.SpamThresholdPercent = imported.Thresholds.SpamThresholdPercent;
            this.Thresholds.MaliciousThresholdPercent = imported.Thresholds.MaliciousThresholdPercent;
            this.Thresholds.SuspiciousThresholdPercent = imported.Thresholds.SuspiciousThresholdPercent;
        }
        this.Whitelist = [.. imported.Whitelist];
        this.Blacklist = [.. imported.Blacklist];
        this.SpamKeywords = [.. imported.SpamKeywords];
        this.ThreatIntelligenceLevel = imported.ThreatIntelligenceLevel;
        this.ScanningSpeed = imported.ScanningSpeed;
        this.SpamDestination = imported.SpamDestination;
        this.LabelInOutlook = imported.LabelInOutlook;
        if (!string.IsNullOrEmpty(imported.Theme)) this.Theme = imported.Theme;
        if (!string.IsNullOrEmpty(imported.HistoryScanScope)) this.HistoryScanScope = imported.HistoryScanScope;
        if (!string.IsNullOrWhiteSpace(imported.VtKey) && imported.VtKey != "[REDACTED]")
        {
            this.VtKey = imported.VtKey;
        }
    }
}

public sealed class ThresholdSettings
{
    public double SpamThresholdPercent { get; set; } = 40.0;
    public double MaliciousThresholdPercent { get; set; } = 20.0;
    public double SuspiciousThresholdPercent { get; set; } = 55.0;
}
