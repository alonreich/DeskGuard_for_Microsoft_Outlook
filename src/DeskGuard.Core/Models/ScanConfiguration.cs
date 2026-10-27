namespace DeskGuard.Core.Models;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DeskGuard.Core.Serialization;

public sealed class ScanConfiguration
{
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
    public int ScanningSpeed { get; set; } = 50;
    public string SpamDestination { get; set; } = "quarantine"; // "quarantine" or "junk"
    public bool LabelInOutlook { get; set; } = true;
    public string VtKey { get; set; } = string.Empty;

    public static ScanConfiguration Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                string json = File.ReadAllText(filePath);
                var cfg = JsonSerializer.Deserialize(json, DeskGuardJsonContext.Default.ScanConfiguration);
                if (cfg != null) return cfg;
            }
        }
        catch { }

        return new ScanConfiguration();
    }

    public void Save(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(this, DeskGuardJsonContext.Default.ScanConfiguration);
            File.WriteAllText(filePath, json);
        }
        catch { }
    }
}

public sealed class ThresholdSettings
{
    public double SpamThresholdPercent { get; set; } = 40.0;
    public double MaliciousThresholdPercent { get; set; } = 20.0;
    public double SuspiciousThresholdPercent { get; set; } = 55.0;
}
