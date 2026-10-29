namespace DeskGuard.App;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Models;
using DeskGuard.Data;
using DeskGuard.Interop.Outlook.Services;
using DeskGuard.Interop.Outlook.Sta;

internal static partial class Program
{
    [LibraryImport("ole32.dll", EntryPoint = "OleInitialize")]
    private static partial int OleInitialize(IntPtr pvReserved);

    [LibraryImport("ole32.dll", EntryPoint = "OleUninitialize")]
    private static partial void OleUninitialize();

    [LibraryImport("kernel32.dll", EntryPoint = "AttachConsole")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    private const int ATTACH_PARENT_PROCESS = -1;
    private const string AppMutexName = @"Local\DeskGuard_App_SingleInstance_Mutex";

    [STAThread]
    private static int Main(string[] args)
    {
        // Enforce OLE/COM Single-Threaded Apartment mode before any Shell/COM APIs are invoked
        _ = OleInitialize(IntPtr.Zero);

        try
        {
            return RunApp(args);
        }
        finally
        {
            OleUninitialize();
        }
    }

    private static int RunApp(string[] args)
    {
        // Attach to parent console if invoked from terminal (CLI/PowerShell/cmd)
        _ = AttachConsole(ATTACH_PARENT_PROCESS);

        string logsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "logs");
        DeskGuard.Core.Logging.AppLogger.Initialize(logsDir);

        if (DeploymentLifecycle.ShouldHandle(args))
        {
            return DeploymentLifecycle.RunAsync(args).GetAwaiter().GetResult();
        }

        // If running elevated as Administrator in normal runtime mode:
        // Windows UIPI blocks COM automation between Administrator processes and standard-user Outlook.
        // Re-launch non-elevated via Windows Explorer desktop session and exit.
        if (DeploymentLifecycle.IsElevated())
        {
            DeskGuard.Core.Logging.AppLogger.Warn("[Security] DeskGuard started with elevated Administrator privileges. Microsoft Outlook runs as a standard user, so Windows UIPI blocks COM automation. Re-launching as standard interactive user via Windows Explorer...");
            try
            {
                string targetExe = Environment.ProcessPath ?? "DeskGuard.exe";
                ProcessStartInfo explorerPsi = new("explorer.exe", $"\"{targetExe}\"")
                {
                    UseShellExecute = false
                };
                Process.Start(explorerPsi);
                DeskGuard.Core.Logging.AppLogger.Info("[Security] Re-launched non-elevated instance via Windows Explorer. Exiting elevated instance.");
                return 0;
            }
            catch (Exception ex)
            {
                DeskGuard.Core.Logging.AppLogger.Error("[Security] Failed to de-elevate via explorer.exe", ex);
            }
        }

        // Enforce single running instance per user session
        using Mutex appMutex = new(initiallyOwned: true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            TrayIconController.SignalExistingInstance();
            Console.WriteLine("DeskGuard is already running.");
            return 0;
        }

        Console.WriteLine("Starting DeskGuard for Microsoft Outlook...");

        string dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "data");
        Directory.CreateDirectory(dataDir);

        DeskGuard.Core.Logging.AppLogger.Info("Starting DeskGuard for Microsoft Outlook...");

        string configPath = Path.Combine(dataDir, "config.json");
        ScanConfiguration config = ScanConfiguration.Load(configPath);

        using TrayIconController tray = new();
        tray.DataDirectory = dataDir;
        tray.UpdateStatus(config.Enabled ? "Starting up..." : "Paused (Protection Disabled)");
        tray.ShowNotification(
            "DeskGuard for Microsoft Outlook",
            "DeskGuard is active and protecting your inbox in the background.\nRight-click this icon for options.");

        using VaultRepository vault = new(dataDir);
        using OutlookStaCoordinator coordinator = new();
        coordinator.LogMessage += msg => DeskGuard.Core.Logging.AppLogger.Warn($"[STA Coordinator] {msg}");

        using OutlookMapiService mapiService = new(coordinator, vault, config);

        using DashboardWindow dashboard = new(vault, mapiService, config);
        dashboard.Initialize();

        DeskGuard.Core.Logging.AppLogger.OnLog += msg =>
        {
            dashboard.PostEvent("live-log", msg);
        };

        tray.RequestOpenDashboard += () =>
        {
            dashboard.ShowAndFocus();
        };

        mapiService.LogProgress += msg =>
        {
            DeskGuard.Core.Logging.AppLogger.Info($"[DeskGuard] {msg}");
        };

        mapiService.SweepProgress += (count, total) =>
        {
            dashboard.PostEvent("outlook-scan-update", new { status = "SCANNING", count, total });
        };

        mapiService.EmailScanned += email =>
        {
            DeskGuard.Core.Logging.AppLogger.Info($"Scanned: {email.Subject} [{email.Verdict}] (Score: {email.Score:F1})");
            tray.OnEmailScanned(email.Subject, email.Verdict, email.Score);

            var stats = vault.GetStats();
            var categorized = vault.GetAllCategorizedEmails(100);

            dashboard.PostEvent("outlook-scan-update", new { item = email, stats = stats });
            dashboard.PostEvent("stats-update", new
            {
                full = true,
                stats = new
                {
                    all = categorized.GetValueOrDefault("all", []),
                    malicious = categorized.GetValueOrDefault("malicious", []),
                    suspicious = categorized.GetValueOrDefault("suspicious", []),
                    spam = categorized.GetValueOrDefault("spam", []),
                    safe = categorized.GetValueOrDefault("safe", [])
                },
                counts = new
                {
                    total = stats.Total,
                    malicious = stats.Malicious,
                    suspicious = stats.Suspicious,
                    spam = stats.Spam,
                    safe = stats.Safe
                }
            });
        };

        mapiService.EngineStatusChanged += (status, detail) =>
        {
            tray.UpdateStatus($"{status} ({detail})");
            dashboard.PostEvent("outlook-status", new { status, details = detail });
        };

        using CancellationTokenSource cts = new();

        tray.RequestExit += () =>
        {
            Console.WriteLine("Exit requested from tray context menu.");
            cts.Cancel();
            tray.Exit();
        };

        tray.RequestSweep += () =>
        {
            DateTime since = config.DeepHistoryScanEnabled ? DateTime.MinValue : DateTime.UtcNow.AddDays(-7);
            string desc = since == DateTime.MinValue ? "Scanning all historical mailbox items for threats..." : "Scanning items from the past 7 days for threats...";
            DeskGuard.Core.Logging.AppLogger.Info($"Mailbox sweep requested from tray ({desc}).");
            tray.ShowNotification("Mailbox Sweep", desc);
            _ = Task.Run(async () =>
            {
                try
                {
                    await mapiService.RunSweepAsync(since, cts.Token);
                    tray.ShowNotification("Mailbox Sweep Complete", "Mailbox sweep has finished successfully.");
                }
                catch (Exception ex)
                {
                    DeskGuard.Core.Logging.AppLogger.Error("[Tray Sweep] Error during sweep", ex);
                    tray.ShowNotification("Sweep Error", ex.Message, isWarning: true);
                }
            });
        };

        tray.RequestDuplicateScan += () =>
        {
            DeskGuard.Core.Logging.AppLogger.Info("Duplicate scan requested from tray.");
            tray.ShowNotification("Duplicate Scan", "Scanning mailboxes for duplicate items...");
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await mapiService.ScanDuplicatesAsync(cts.Token);
                    vault.SaveDuplicates(result.Items, "Finished");
                    dashboard.PostEvent("duplicate-update", new { status = "Finished", items = result.Items, scanned = result.ScannedCount, found = result.Items.Count });
                    tray.ShowNotification("Duplicate Scan Complete", $"Found {result.Items.Count} duplicate items across {result.ScannedCount} emails.");
                }
                catch (Exception ex)
                {
                    DeskGuard.Core.Logging.AppLogger.Error("[Tray Duplicate Scan] Error", ex);
                    tray.ShowNotification("Duplicate Scan Error", ex.Message, isWarning: true);
                }
            });
        };

        tray.RequestCheckUpdates += () =>
        {
            DeskGuard.Core.Logging.AppLogger.Info("Check for updates requested from tray context menu.");
            tray.ShowNotification("DeskGuard Updates", "Checking for updates...");
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await dashboard.UpdateManager.CheckForUpdatesAsync(manual: true, cts.Token);
                    if (result.HasUpdate)
                    {
                        tray.ShowNotification(
                            "DeskGuard Update Available",
                            $"Version {result.LatestVersion} is available! Opening dashboard...");
                        dashboard.ShowAndFocus();
                        dashboard.PostEvent("update-available", result);
                    }
                    else if (!string.IsNullOrEmpty(result.Error))
                    {
                        tray.ShowNotification("Update Check Failed", result.Error, isWarning: true);
                    }
                    else
                    {
                        tray.ShowNotification(
                            "DeskGuard is Up to Date",
                            $"You are running the latest version (v{result.CurrentVersion}).");
                    }
                }
                catch (Exception ex)
                {
                    DeskGuard.Core.Logging.AppLogger.Error("[Tray Update Check] Error", ex);
                    tray.ShowNotification("Update Check Error", ex.Message, isWarning: true);
                }
            });
        };

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
            tray.Exit();
        };

        int isConnecting = 0;
        DateTime nextConnectionAttemptUtc = DateTime.MinValue;
        async Task CheckOutlookConnectionAsync()
        {
            if (Interlocked.CompareExchange(ref isConnecting, 1, 0) != 0) return;
            try
            {
                bool outlookRunning = Process.GetProcessesByName("OUTLOOK").Length > 0;
                if (outlookRunning && config.Enabled)
                {
                    if (!mapiService.IsWatching)
                    {
                        if (DateTime.UtcNow < nextConnectionAttemptUtc)
                        {
                            return;
                        }

                        DeskGuard.Core.Logging.AppLogger.Info("[Watchdog] Outlook detected running. Connecting live watcher...");
                        tray.UpdateStatus("Connecting to Outlook...");
                        bool watched = await mapiService.StartLiveWatcherAsync(cts.Token);
                        if (watched)
                        {
                            nextConnectionAttemptUtc = DateTime.MinValue;
                            DeskGuard.Core.Logging.AppLogger.Info("[Watchdog] Real-time protection successfully established.");
                            tray.UpdateStatus("Active (Monitoring Inbox)");
                            tray.ShowNotification("DeskGuard Active", "Outlook connected. Real-time inbox protection is active.");

                            if (config.HistoryScanEnabled)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        DateTime since = (config.HistoryScanScope == "unread")
                                            ? DateTime.UtcNow.AddDays(-1)
                                            : DateTime.UtcNow.AddDays(-7);
                                        string scope = (config.HistoryScanScope == "unread") ? "recent unread items" : "past 7 days";
                                        DeskGuard.Core.Logging.AppLogger.Info($"[Watchdog] Triggering startup priority sweep for {scope}...");
                                        await mapiService.RunSweepAsync(since, cts.Token);

                                        // Continue background crawling past 7 days cutoff across older history
                                        if (config.DeepHistoryScanEnabled || config.HistoryScanScope != "unread")
                                        {
                                            DeskGuard.Core.Logging.AppLogger.Info("[Watchdog] Priority sweep complete. Continuing background crawl across remaining older emails...");
                                            await mapiService.RunSweepAsync(DateTime.MinValue, cts.Token);
                                            await mapiService.RunCrawlerAsync(cts.Token);
                                            DeskGuard.Core.Logging.AppLogger.Info("[Watchdog] Complete historical sweep and indexing finished.");
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        DeskGuard.Core.Logging.AppLogger.Error("[Startup Sweep] Failed", ex);
                                    }
                                });
                            }
                        }
                        else
                        {
                            nextConnectionAttemptUtc = DateTime.UtcNow.AddSeconds(15);
                            DeskGuard.Core.Logging.AppLogger.Warn("[Watchdog] StartLiveWatcherAsync returned false. Will retry in 15 seconds.");
                            tray.UpdateStatus("Standby (Waiting for Outlook)");
                        }
                    }
                    else
                    {
                        mapiService.DrainRetryQueue();
                    }
                }
                else if (!config.Enabled)
                {
                    if (mapiService.IsWatching)
                    {
                        DeskGuard.Core.Logging.AppLogger.Info("[Watchdog] Protection disabled by configuration. Stopping watcher...");
                        await mapiService.StopLiveWatcherAsync();
                    }
                    tray.UpdateStatus("Paused (Protection Disabled)");
                }
                else
                {
                    if (mapiService.IsConnected || mapiService.IsWatching)
                    {
                        DeskGuard.Core.Logging.AppLogger.Info("[Watchdog] Outlook process not detected. Resetting connection...");
                        await mapiService.ResetConnectionAsync();
                        tray.UpdateStatus("Standby (Waiting for Outlook)");
                    }
                    else if (!mapiService.IsWatching)
                    {
                        tray.UpdateStatus("Standby (Waiting for Outlook)");
                    }
                }
            }
            catch (Exception ex)
            {
                DeskGuard.Core.Logging.AppLogger.Error("[Watchdog] Outlook connection check exception", ex);
            }
            finally
            {
                Interlocked.Exchange(ref isConnecting, 0);
            }
        }

        tray.WatchdogTick += () =>
        {
            _ = CheckOutlookConnectionAsync();
        };

        tray.StartWatchdog(intervalMs: 3000);

        // Initial connection check
        _ = CheckOutlookConnectionAsync();

        // Show dashboard window on launch unless explicitly running hidden
        if (!args.Contains("--hidden", StringComparer.OrdinalIgnoreCase))
        {
            dashboard.ShowAndFocus();
        }

        // Run message loop on the main STA thread
        tray.RunMessageLoop(cts.Token);

        Console.WriteLine("Shutting down DeskGuard...");
        try
        {
            mapiService.StopLiveWatcherAsync().GetAwaiter().GetResult();
        }
        catch { }

        return 0;
    }
}
