namespace DeskGuard.Core.Services;

using System;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed partial class CloudThreatService
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private const ushort DNS_TYPE_TEXT = 0x0010;
    private const uint DNS_QUERY_STANDARD = 0x00000000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DNS_TXT_DATA
    {
        public uint dwStringCount;
        public IntPtr pStringArray;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DNS_RECORD
    {
        public IntPtr pNext;
        public IntPtr pName;
        public ushort wType;
        public ushort wDataLength;
        public uint flags;
        public uint dwTtl;
        public uint dwReserved;
        public DNS_TXT_DATA Data;
    }

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsQuery_W", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DnsQuery_W(
        string lpstrName,
        ushort wType,
        uint fOptions,
        IntPtr pExtra,
        out IntPtr ppQueryResultsSet,
        IntPtr pReserved);

    [LibraryImport("dnsapi.dll", EntryPoint = "DnsRecordListFree")]
    private static partial void DnsRecordListFree(IntPtr pRecordList, int FreeType);

    public static async Task<CloudScanResult> ScanVirusAsync(string hashOrId, string? vtApiKey, int threatIntelLevel, CancellationToken ct = default)
    {
        if (threatIntelLevel == 0)
        {
            return new CloudScanResult
            {
                Success = true,
                Verdict = "Clean",
                Details = "Level 0: Local PC Only (Cloud inspection disabled for privacy)."
            };
        }

        // Level 1: Check Team Cymru Malware Hash Database via DNS (Zero-API)
        bool cymruHit = QueryCymruDns(hashOrId);
        if (cymruHit)
        {
            return new CloudScanResult
            {
                Success = true,
                Verdict = "Malicious",
                IsMalicious = true,
                Details = "Team Cymru Malware Hash Database hit: Known malicious payload hash."
            };
        }

        // Level 2 / VirusTotal if API key is provided
        if (string.IsNullOrWhiteSpace(vtApiKey) || vtApiKey.Trim().Length < 16 || vtApiKey == "MASKED_FOR_SECURITY")
        {
            if (threatIntelLevel >= 2)
            {
                return new CloudScanResult
                {
                    Success = false,
                    Code = "NO_API_KEY",
                    Error = "VirusTotal API key is not configured. Please enter your API key to enable live threat intelligence scanning."
                };
            }

            return new CloudScanResult
            {
                Success = true,
                Verdict = "Clean",
                Details = "Team Cymru DNS: Clean. VirusTotal skipped (no API key configured)."
            };
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{hashOrId}");
            request.Headers.Add("x-apikey", vtApiKey.Trim());

            using HttpResponseMessage response = await HttpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(ct);
                using JsonDocument doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataElem) &&
                    dataElem.TryGetProperty("attributes", out var attrElem) &&
                    attrElem.TryGetProperty("last_analysis_stats", out var statsElem))
                {
                    int malicious = statsElem.TryGetProperty("malicious", out var m) ? m.GetInt32() : 0;
                    int suspicious = statsElem.TryGetProperty("suspicious", out var s) ? s.GetInt32() : 0;

                    return new CloudScanResult
                    {
                        Success = true,
                        IsMalicious = malicious > 0,
                        Verdict = malicious > 0 ? "Malicious" : (suspicious > 0 ? "Suspicious" : "Clean"),
                        Details = $"VirusTotal detection: {malicious} engines flagged malicious, {suspicious} suspicious."
                    };
                }
            }
            else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new CloudScanResult
                {
                    Success = true,
                    Verdict = "Clean",
                    Details = "VirusTotal: Hash not seen in global database (no threats reported)."
                };
            }
            else
            {
                return new CloudScanResult
                {
                    Success = false,
                    Error = $"VirusTotal API returned HTTP {(int)response.StatusCode}: {response.ReasonPhrase}"
                };
            }
        }
        catch (Exception ex)
        {
            return new CloudScanResult
            {
                Success = false,
                Error = $"Threat intelligence query failed: {ex.Message}"
            };
        }

        return new CloudScanResult { Success = true, Verdict = "Clean" };
    }

    private static bool QueryCymruDns(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash) || (hash.Length != 32 && hash.Length != 64)) return false;

        string query = $"{hash.ToLowerInvariant()}.malware.hash.cymru.com";
        IntPtr pResults = IntPtr.Zero;
        try
        {
            int status = DnsQuery_W(query, DNS_TYPE_TEXT, DNS_QUERY_STANDARD, IntPtr.Zero, out pResults, IntPtr.Zero);
            if (status == 0 && pResults != IntPtr.Zero)
            {
                return true;
            }
        }
        catch { }
        finally
        {
            if (pResults != IntPtr.Zero)
            {
                DnsRecordListFree(pResults, 1);
            }
        }
        return false;
    }
}

public sealed class CloudScanResult
{
    public bool Success { get; set; } = true;
    public string? Code { get; set; }
    public string? Error { get; set; }
    public string Verdict { get; set; } = "Clean";
    public bool IsMalicious { get; set; }
    public string Details { get; set; } = string.Empty;
}
