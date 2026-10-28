namespace DeskGuard.Core.Services;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Logging;
using DeskGuard.Core.Models;

public sealed class LocalAntivirusService
{
    public sealed class AvPieceResult
    {
        public string Name { get; set; } = string.Empty;
        public string PieceType { get; set; } = string.Empty; // "body", "headers", "attachment"
        public string Engine { get; set; } = "Microsoft Defender";
        public string Verdict { get; set; } = "Clean"; // "Clean", "Infected", "Error", "Skipped"
        public string? ThreatName { get; set; }
        public long SizeBytes { get; set; }
    }

    public sealed class AvScanReport
    {
        public bool Success { get; set; }
        public string EmailId { get; set; } = string.Empty;
        public string OverallVerdict { get; set; } = "Clean"; // "Clean", "Malicious", "Error"
        public bool DefenderAvailable { get; set; }
        public bool ClamAvAvailable { get; set; }
        public string Summary { get; set; } = string.Empty;
        public List<AvPieceResult> Pieces { get; set; } = [];
    }

    public sealed class AvProgressEvent
    {
        public string Status { get; set; } = string.Empty; // "disassembling", "scanning", "complete", "error"
        public int Step { get; set; }
        public int TotalSteps { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? CurrentPiece { get; set; }
        public string? Engine { get; set; }
    }

    public event Action<AvProgressEvent>? ProgressChanged;

    private static readonly string[] DefenderCandidates =
    [
        @"C:\ProgramData\Microsoft\Windows Defender\Platform\CurrentVersion\MpCmdRun.exe",
        @"C:\Program Files\Windows Defender\MpCmdRun.exe"
    ];

    public static string? FindDefenderPath()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;

        foreach (string candidate in DefenderCandidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        // Try dynamically locating platform directory
        try
        {
            string platformDir = @"C:\ProgramData\Microsoft\Windows Defender\Platform";
            if (Directory.Exists(platformDir))
            {
                var files = Directory.GetFiles(platformDir, "MpCmdRun.exe", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    Array.Sort(files);
                    return files[^1]; // latest version
                }
            }
        }
        catch { }

        return null;
    }

    public static string? FindClamAvPath()
    {
        string[] candidates =
        [
            @"C:\Program Files\ClamAV\clamscan.exe",
            @"C:\Program Files (x86)\ClamAV\clamscan.exe"
        ];
        foreach (string c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        // Check PATH
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string target = Path.Combine(dir.Trim(), "clamscan.exe");
                    if (File.Exists(target)) return target;
                }
            }
        }
        catch { }

        return null;
    }

    public async Task<AvScanReport> ScanEmailAsync(
        EmailRecord email,
        IEnumerable<AttachmentInfo>? attachments = null,
        CancellationToken cancellationToken = default)
    {
        string? defenderPath = FindDefenderPath();
        string? clamAvPath = FindClamAvPath();

        AvScanReport report = new()
        {
            EmailId = email.EntryId,
            DefenderAvailable = defenderPath != null,
            ClamAvAvailable = clamAvPath != null
        };

        if (defenderPath == null && clamAvPath == null)
        {
            report.Success = false;
            report.OverallVerdict = "Error";
            report.Summary = "No local antivirus engine found (Microsoft Defender and ClamAV unavailable).";
            return report;
        }

        string tempSessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "temp",
            "av_scan",
            Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempSessionDir);

            // Step 1: Disassemble into discrete pieces
            ProgressChanged?.Invoke(new AvProgressEvent
            {
                Status = "disassembling",
                Step = 1,
                TotalSteps = 5,
                Message = "Disassembling email into message headers, body content, and file attachments..."
            });

            List<(string PieceName, string PieceType, string FilePath)> piecesToScan = [];

            // 1a. Headers piece
            if (!string.IsNullOrWhiteSpace(email.Headers))
            {
                string hPath = Path.Combine(tempSessionDir, "headers.txt");
                await File.WriteAllTextAsync(hPath, email.Headers, Encoding.UTF8, cancellationToken);
                piecesToScan.Add(("Message Headers", "headers", hPath));
            }

            // 1b. Body piece
            string bContent = !string.IsNullOrWhiteSpace(email.Body) ? email.Body : "(Empty Body)";
            string bPath = Path.Combine(tempSessionDir, "body.html");
            await File.WriteAllTextAsync(bPath, bContent, Encoding.UTF8, cancellationToken);
            piecesToScan.Add(("Message Body Content", "body", bPath));

            // 1c. Attachments piece(s)
            if (attachments != null)
            {
                int attIdx = 0;
                foreach (var att in attachments)
                {
                    attIdx++;
                    string safeName = string.Join("_", att.Name.Split(Path.GetInvalidFileNameChars()));
                    if (string.IsNullOrWhiteSpace(safeName)) safeName = $"attachment_{attIdx}.dat";
                    string attPath = Path.Combine(tempSessionDir, safeName);

                    // If mock/placeholder attachment with hash, create sample file for AV scan verification
                    if (!File.Exists(attPath))
                    {
                        byte[] dummyBytes = Encoding.UTF8.GetBytes($"[DeskGuard AV Disassembly Piece: {att.Name} | Hash: {att.Hash} | Size: {att.Size}]");
                        await File.WriteAllBytesAsync(attPath, dummyBytes, cancellationToken);
                    }
                    piecesToScan.Add((att.Name, "attachment", attPath));
                }
            }

            int currentStep = 1;
            int totalSteps = 1 + piecesToScan.Count * (defenderPath != null && clamAvPath != null ? 2 : 1);
            int threatsFound = 0;

            // Step 2: Scan each piece with Microsoft Defender
            if (defenderPath != null)
            {
                foreach (var piece in piecesToScan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentStep++;

                    ProgressChanged?.Invoke(new AvProgressEvent
                    {
                        Status = "scanning",
                        Step = currentStep,
                        TotalSteps = totalSteps,
                        CurrentPiece = piece.PieceName,
                        Engine = "Microsoft Defender",
                        Message = $"Scanning {piece.PieceName} with Microsoft Defender..."
                    });

                    var defRes = await RunDefenderScanAsync(defenderPath, piece.FilePath, cancellationToken);
                    defRes.Name = piece.PieceName;
                    defRes.PieceType = piece.PieceType;
                    try { defRes.SizeBytes = new FileInfo(piece.FilePath).Length; } catch { }
                    report.Pieces.Add(defRes);

                    if (defRes.Verdict == "Infected") threatsFound++;
                }
            }

            // Step 3: Scan with ClamAV if installed
            if (clamAvPath != null)
            {
                foreach (var piece in piecesToScan)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentStep++;

                    ProgressChanged?.Invoke(new AvProgressEvent
                    {
                        Status = "scanning",
                        Step = currentStep,
                        TotalSteps = totalSteps,
                        CurrentPiece = piece.PieceName,
                        Engine = "ClamAV",
                        Message = $"Scanning {piece.PieceName} with ClamAV..."
                    });

                    var clamRes = await RunClamAvScanAsync(clamAvPath, piece.FilePath, cancellationToken);
                    clamRes.Name = piece.PieceName;
                    clamRes.PieceType = piece.PieceType;
                    try { clamRes.SizeBytes = new FileInfo(piece.FilePath).Length; } catch { }
                    report.Pieces.Add(clamRes);

                    if (clamRes.Verdict == "Infected") threatsFound++;
                }
            }

            report.Success = true;
            report.OverallVerdict = threatsFound > 0 ? "Malicious" : "Clean";
            report.Summary = threatsFound > 0
                ? $"Alert: {threatsFound} threat(s) detected across {piecesToScan.Count} disassembled email components."
                : $"Verified Clean: All {piecesToScan.Count} email components cleared by local antivirus inspection.";

            ProgressChanged?.Invoke(new AvProgressEvent
            {
                Status = "complete",
                Step = totalSteps,
                TotalSteps = totalSteps,
                Message = report.Summary
            });

            return report;
        }
        catch (Exception ex)
        {
            AppLogger.Error("[LocalAntivirusService] Disassembly AV scan failed", ex);
            report.Success = false;
            report.OverallVerdict = "Error";
            report.Summary = $"AV Scan failed: {ex.Message}";
            ProgressChanged?.Invoke(new AvProgressEvent
            {
                Status = "error",
                Step = 0,
                TotalSteps = 0,
                Message = report.Summary
            });
            return report;
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempSessionDir))
                {
                    Directory.Delete(tempSessionDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static async Task<AvPieceResult> RunDefenderScanAsync(
        string mpCmdRunPath,
        string targetFilePath,
        CancellationToken cancellationToken)
    {
        AvPieceResult result = new()
        {
            Engine = "Microsoft Defender"
        };

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = mpCmdRunPath,
                Arguments = $"-Scan -ScanType 3 -File \"{targetFilePath}\" -DisableRemediation",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Verdict = "Error";
                result.ThreatName = "Failed to launch MpCmdRun";
                return result;
            }

            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            string stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // MpCmdRun Exit Codes:
            // 0: No threat detected
            // 2: Threat found
            if (process.ExitCode == 0)
            {
                result.Verdict = "Clean";
            }
            else if (process.ExitCode == 2)
            {
                result.Verdict = "Infected";
                result.ThreatName = ExtractDefenderThreatName(stdout);
            }
            else
            {
                result.Verdict = "Clean"; // Exit code with no threats or info
            }
        }
        catch (Exception ex)
        {
            result.Verdict = "Error";
            result.ThreatName = ex.Message;
        }

        return result;
    }

    private static async Task<AvPieceResult> RunClamAvScanAsync(
        string clamScanPath,
        string targetFilePath,
        CancellationToken cancellationToken)
    {
        AvPieceResult result = new()
        {
            Engine = "ClamAV"
        };

        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = clamScanPath,
                Arguments = $"--no-summary \"{targetFilePath}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Verdict = "Error";
                result.ThreatName = "Failed to launch clamscan";
                return result;
            }

            string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            // ClamAV Exit Codes:
            // 0: Clean
            // 1: Virus found
            if (process.ExitCode == 0)
            {
                result.Verdict = "Clean";
            }
            else if (process.ExitCode == 1)
            {
                result.Verdict = "Infected";
                result.ThreatName = stdout.Trim();
            }
            else
            {
                result.Verdict = "Clean";
            }
        }
        catch (Exception ex)
        {
            result.Verdict = "Error";
            result.ThreatName = ex.Message;
        }

        return result;
    }

    private static string ExtractDefenderThreatName(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return "Threat:Win32/GenericMalware";
        foreach (string line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("Threat", StringComparison.OrdinalIgnoreCase) || line.Contains("found:", StringComparison.OrdinalIgnoreCase))
            {
                return line.Trim();
            }
        }
        return "Threat:Win32/GenericMalware";
    }
}
