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

        if (DeploymentLifecycle.ShouldHandle(args))
        {
            return DeploymentLifecycle.RunAsync(args).GetAwaiter().GetResult();
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
        using OutlookMapiService mapiService = new(coordinator, vault, config);

        using DashboardWindow dashboard = new(vault, mapiService, config);
        dashboard.Initialize();

        tray.RequestOpenDashboard += () =>
        {
            dashboard.ShowAndFocus();
        };

        mapiService.LogProgress += msg =>
        {
            Console.WriteLine($"[DeskGuard] {msg}");
            dashboard.PostEvent("live-log", msg);
        };

        mapiService.SweepProgress += (count, total) =>
        {
            dashboard.PostEvent("outlook-scan-update", new { status = "SCANNING", count, total });
        };

        mapiService.EmailScanned += email =>
        {
            Console.WriteLine($"Scanned: {email.Subject} [{email.Verdict}] (Score: {email.Score:F1})");
            tray.OnEmailScanned(email.Subject, email.Verdict, email.Score);
            dashboard.PostEvent("outlook-scan-update", new { item = email, stats = vault.GetStats() });
            dashboard.PostEvent("stats-update", vault.GetStats());
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
            Console.WriteLine("Mailbox sweep requested.");
            tray.ShowNotification("Mailbox Sweep", "Scanning items from the past 7 days for threats...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await mapiService.RunSweepAsync(DateTime.UtcNow.AddDays(-7), cts.Token);
                    tray.ShowNotification("Mailbox Sweep Complete", "Mailbox sweep has finished successfully.");
                }
                catch (Exception ex)
                {
                    tray.ShowNotification("Sweep Error", ex.Message, isWarning: true);
                }
            });
        };

        tray.RequestDuplicateScan += () =>
        {
            Console.WriteLine("Duplicate scan requested.");
            tray.ShowNotification("Duplicate Scan", "Scanning mailboxes for duplicate items...");
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await mapiService.ScanDuplicatesAsync(cts.Token);
                    tray.ShowNotification("Duplicate Scan Complete", $"Found {result.Items.Count} duplicate items across {result.ScannedCount} emails.");
                }
                catch (Exception ex)
                {
                    tray.ShowNotification("Duplicate Scan Error", ex.Message, isWarning: true);
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
                        tray.UpdateStatus("Connecting to Outlook...");
                        bool watched = await mapiService.StartLiveWatcherAsync(cts.Token);
                        if (watched)
                        {
                            tray.UpdateStatus("Active (Monitoring Inbox)");
                            tray.ShowNotification("DeskGuard Active", "Outlook connected. Real-time inbox protection is active.");

                            if (config.HistoryScanEnabled)
                            {
                                _ = Task.Run(async () =>
                                {
                                    try
                                    {
                                        await mapiService.RunSweepAsync(DateTime.UtcNow.AddDays(-7), cts.Token);
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[Startup Sweep] {ex.Message}");
                                    }
                                });
                            }
                        }
                        else
                        {
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
                        await mapiService.StopLiveWatcherAsync();
                    }
                    tray.UpdateStatus("Paused (Protection Disabled)");
                }
                else
                {
                    if (mapiService.IsConnected || mapiService.IsWatching)
                    {
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
                Console.WriteLine($"[Watchdog] Outlook connection check: {ex.Message}");
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
