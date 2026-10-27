namespace DeskGuard.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Models;
using DeskGuard.Core.Scoring;
using DeskGuard.Core.Serialization;
using DeskGuard.Core.Services;
using DeskGuard.Data;
using DeskGuard.Interop.Outlook.Services;
using Microsoft.Web.WebView2.Core;

internal sealed partial class DashboardWindow : IDisposable
{
    private static DashboardWindow? _instance;
    private readonly IntPtr _hwnd;
    private readonly VaultRepository _vault;
    private readonly OutlookMapiService _mapiService;
    private readonly ScoringEngine _scoringEngine;
    private readonly ScanConfiguration _config;
    private readonly CancellationTokenSource _cts = new();

    private CoreWebView2Controller? _controller;
    private CoreWebView2? _coreWebView;
    private readonly SynchronizationContext? _uiSyncContext;
    private bool _isInitialized;

    public const string WindowClassName = "DeskGuard_Dashboard_WindowClass";
    public const string WindowTitle = "DeskGuard for Microsoft Outlook";

    private const int DefaultWidth = 1500;
    private const int DefaultHeight = 900;

    private const uint WS_POPUP = 0x80000000;
    private const uint WS_THICKFRAME = 0x00040000;
    private const uint WS_MINIMIZEBOX = 0x00020000;
    private const uint WS_MAXIMIZEBOX = 0x00010000;
    private const uint WS_CLIPCHILDREN = 0x02000000;
    private const uint WS_CLIPSIBLINGS = 0x04000000;

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;
    private const int SW_MAXIMIZE = 3;

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private const uint WM_SIZE = 0x0005;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public IntPtr lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static unsafe partial ushort RegisterClassExW(WNDCLASSEXW* lpwcx);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial IntPtr CreateWindowExW(
        uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int X, int Y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static partial IntPtr DefWindowProcW(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "ShowWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "IsZoomed")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetClientRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "GetSystemMetrics")]
    public static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseCapture")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "MsgWaitForMultipleObjects")]
    private static partial uint MsgWaitForMultipleObjects(uint nCount, IntPtr pHandles, [MarshalAs(UnmanagedType.Bool)] bool bWaitAll, uint dwMilliseconds, uint dwWakeMask);

    private const uint PM_REMOVE = 0x0001;
    private const uint QS_ALLINPUT = 0x04FF;

    private static T WaitForTaskWithPump<T>(Task<T> task)
    {
        while (!task.IsCompleted)
        {
            while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref msg);
                DispatchMessageW(ref msg);
            }
            if (!task.IsCompleted)
            {
                MsgWaitForMultipleObjects(0, IntPtr.Zero, false, 10, QS_ALLINPUT);
            }
        }
        return task.GetAwaiter().GetResult();
    }

    public DashboardWindow(VaultRepository vault, OutlookMapiService mapiService, ScanConfiguration config)
    {
        _instance = this;
        _uiSyncContext = SynchronizationContext.Current;
        _vault = vault;
        _mapiService = mapiService;
        _config = config;
        _scoringEngine = new ScoringEngine();

        IntPtr hInstance = GetModuleHandleW(IntPtr.Zero);

        unsafe
        {
            fixed (char* classNamePtr = WindowClassName)
            {
                WNDCLASSEXW wc = new()
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    style = 0,
                    lpfnWndProc = (IntPtr)(delegate* unmanaged[Stdcall]<IntPtr, uint, UIntPtr, IntPtr, IntPtr>)&WndProc,
                    cbClsExtra = 0,
                    cbWndExtra = 0,
                    hInstance = hInstance,
                    hIcon = IntPtr.Zero,
                    hCursor = IntPtr.Zero,
                    hbrBackground = IntPtr.Zero,
                    lpszMenuName = IntPtr.Zero,
                    lpszClassName = (IntPtr)classNamePtr,
                    hIconSm = IntPtr.Zero
                };
                _ = RegisterClassExW(&wc);
            }
        }

        int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        int screenHeight = GetSystemMetrics(SM_CYSCREEN);
        int x = Math.Max(0, (screenWidth - DefaultWidth) / 2);
        int y = Math.Max(0, (screenHeight - DefaultHeight) / 2);

        _hwnd = CreateWindowExW(
            0,
            WindowClassName,
            WindowTitle,
            WS_POPUP | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            x, y, DefaultWidth, DefaultHeight,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
    }

    public Task InitializeAsync()
    {
        if (_isInitialized || _hwnd == IntPtr.Zero) return Task.CompletedTask;

        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "webview2");
        Directory.CreateDirectory(userDataFolder);

        string logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "webview2.log");

        void Log(string msg)
        {
            try { File.AppendAllText(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}"); } catch { }
            Console.WriteLine($"[Dashboard] {msg}");
        }

        try
        {
            Log($"Starting WebView2 initialization. BaseDir={AppContext.BaseDirectory}, UserDataFolder={userDataFolder}, Apartment={Thread.CurrentThread.GetApartmentState()}, SyncContext={_uiSyncContext?.GetType().Name ?? "null"}");

            try
            {
                CoreWebView2Environment.SetLoaderDllFolderPath(AppContext.BaseDirectory);
            }
            catch (Exception ex)
            {
                Log($"SetLoaderDllFolderPath note: {ex.Message}");
            }

            Log("Creating CoreWebView2Environment with message pump...");
            CoreWebView2Environment env = WaitForTaskWithPump(CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder));
            Log($"CoreWebView2Environment created. BrowserVersion: {env.BrowserVersionString}");

            Log("Creating CoreWebView2Controller with message pump...");
            _controller = WaitForTaskWithPump(env.CreateCoreWebView2ControllerAsync(_hwnd));
            _coreWebView = _controller.CoreWebView2;
            Log("CoreWebView2Controller created successfully.");

            if (GetClientRect(_hwnd, out RECT rc))
            {
                _controller.Bounds = new Rectangle(0, 0, rc.Right - rc.Left, rc.Bottom - rc.Top);
            }
            _controller.IsVisible = true;

            string wwwrootDir = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            if (!Directory.Exists(wwwrootDir))
            {
                string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
                if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(Path.Combine(exeDir, "wwwroot")))
                {
                    wwwrootDir = Path.Combine(exeDir, "wwwroot");
                }
            }

            Log($"Resolved wwwroot: {wwwrootDir} (Exists: {Directory.Exists(wwwrootDir)})");

            if (Directory.Exists(wwwrootDir))
            {
                _coreWebView.SetVirtualHostNameToFolderMapping(
                    "app.deskguard",
                    wwwrootDir,
                    CoreWebView2HostResourceAccessKind.Allow);

                _coreWebView.NavigationCompleted += (s, e) =>
                {
                    Log($"NavigationCompleted: IsSuccess={e.IsSuccess}, WebErrorStatus={e.WebErrorStatus}");
                };

                _coreWebView.Navigate("https://app.deskguard/index.html");
                Log("Navigated to https://app.deskguard/index.html");
            }
            else
            {
                Log($"CRITICAL: wwwroot folder not found at '{wwwrootDir}'!");
            }

            _coreWebView.WebMessageReceived += OnWebMessageReceived;
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Log($"FATAL: WebView2 initialization error: {ex}");
        }

        return Task.CompletedTask;
    }

    public void ShowAndFocus()
    {
        if (_hwnd == IntPtr.Zero) return;
        ShowWindow(_hwnd, SW_RESTORE);
        SetForegroundWindow(_hwnd);
    }

    public void ToggleWindow()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (IsWindowVisible(_hwnd))
        {
            ShowWindow(_hwnd, SW_HIDE);
        }
        else
        {
            ShowAndFocus();
        }
    }

    public void PostEvent(string channel, object data)
    {
        if (_coreWebView == null) return;
        if (_uiSyncContext != null && SynchronizationContext.Current != _uiSyncContext)
        {
            _uiSyncContext.Post(_ => PostEvent(channel, data), null);
            return;
        }

        try
        {
            JsonObject evt = new()
            {
                ["type"] = "event",
                ["channel"] = channel,
                ["data"] = JsonSerializer.SerializeToNode(data)
            };
            _coreWebView.PostWebMessageAsJson(evt.ToJsonString());
        }
        catch { }
    }

    private void PostWebMessageResponse(long id, object? result, string? error)
    {
        if (_coreWebView == null) return;
        if (_uiSyncContext != null && SynchronizationContext.Current != _uiSyncContext)
        {
            _uiSyncContext.Post(_ => PostWebMessageResponse(id, result, error), null);
            return;
        }

        try
        {
            JsonObject resp = new()
            {
                ["type"] = "response",
                ["id"] = id,
                ["result"] = result != null ? JsonSerializer.SerializeToNode(result) : null,
                ["error"] = error
            };
            _coreWebView.PostWebMessageAsJson(resp.ToJsonString());
        }
        catch { }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            long id = root.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : 0;
            string channel = root.TryGetProperty("channel", out var chProp) ? chProp.GetString() ?? string.Empty : string.Empty;
            JsonElement payload = root.TryGetProperty("payload", out var plProp) ? plProp : default;

            object? result = null;
            string? error = null;

            try
            {
                result = await HandleIpcMessageAsync(channel, payload);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (id > 0)
            {
                PostWebMessageResponse(id, result, error);
            }
        }
        catch { }
    }

    private async Task<object?> HandleIpcMessageAsync(string channel, JsonElement payload)
    {
        switch (channel)
        {
            case "window-minimize":
                ShowWindow(_hwnd, SW_MINIMIZE);
                return new { ok = true };

            case "window-maximize":
                if (IsZoomed(_hwnd)) ShowWindow(_hwnd, SW_RESTORE);
                else ShowWindow(_hwnd, SW_MAXIMIZE);
                return new { ok = true };

            case "window-hide":
            case "window-close":
                ShowWindow(_hwnd, SW_HIDE);
                return new { ok = true };

            case "window-drag":
                ReleaseCapture();
                SendMessageW(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                return new { ok = true };

            case "get-stats":
                return await Task.Run(() =>
                {
                    var statsDict = _vault.GetAllCategorizedEmails(100);
                    var engineStats = _vault.GetStats();
                    return (object)new
                    {
                        full = true,
                        stats = new
                        {
                            malicious = statsDict.GetValueOrDefault("malicious", []),
                            suspicious = statsDict.GetValueOrDefault("suspicious", []),
                            spam = statsDict.GetValueOrDefault("spam", []),
                            safe = statsDict.GetValueOrDefault("safe", [])
                        },
                        counts = new
                        {
                            total = engineStats.Total,
                            malicious = engineStats.Malicious,
                            suspicious = engineStats.Suspicious,
                            spam = engineStats.Spam,
                            safe = engineStats.Safe
                        }
                    };
                });

            case "get-config":
                return _config;

            case "set-enabled":
                if (payload.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _config.Enabled = payload.GetBoolean();
                }
                return new { ok = true };

            case "set-history-enabled":
                if (payload.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _config.HistoryScanEnabled = payload.GetBoolean();
                }
                return new { ok = true };

            case "set-scanning-speed":
                if (payload.ValueKind == JsonValueKind.Number)
                {
                    int speed = payload.GetInt32();
                    _config.ScanningSpeed = speed;
                    _mapiService.SetScanSpeed(speed);
                }
                return new { ok = true };

            case "set-threat-intel-level":
                if (payload.ValueKind == JsonValueKind.Number)
                {
                    _config.ThreatIntelligenceLevel = payload.GetInt32();
                }
                return new { ok = true };

            case "set-vt-key":
                if (payload.ValueKind == JsonValueKind.String)
                {
                    _config.VtKey = payload.GetString() ?? string.Empty;
                }
                return new { ok = true };

            case "set-scan-modes":
                if (payload.ValueKind == JsonValueKind.Object)
                {
                    if (payload.TryGetProperty("onAccessEnabled", out var oa))
                    {
                        _config.OnAccessEnabled = oa.GetBoolean();
                    }
                    if (payload.TryGetProperty("deepHistoryScanEnabled", out var dh))
                    {
                        _config.DeepHistoryScanEnabled = dh.GetBoolean();
                    }
                    if (payload.TryGetProperty("onDemandLimit", out var odl))
                    {
                        _config.OnDemandLimit = odl.GetInt32();
                    }
                    if (payload.TryGetProperty("spamDestination", out var sd))
                    {
                        _config.SpamDestination = sd.GetString() ?? "quarantine";
                    }
                    if (payload.TryGetProperty("labelInOutlook", out var lb))
                    {
                        _config.LabelInOutlook = lb.GetBoolean();
                    }
                    _mapiService.UpdateConfiguration(_config);
                }
                return new { ok = true };

            case "set-startup":
                bool startup = payload.ValueKind == JsonValueKind.True;
                StartupManager.SetStartupEnabled(startup);
                return new { ok = true };

            case "check-startup":
                return new { enabled = StartupManager.IsStartupEnabled() };

            case "open-logs-folder":
                try
                {
                    string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskGuard", "data");
                    Process.Start(new ProcessStartInfo { FileName = dataDir, UseShellExecute = true });
                }
                catch { }
                return new { ok = true };

            case "release-email":
                string rEntryId = payload.TryGetProperty("entryId", out var re) ? re.GetString() ?? string.Empty : string.Empty;
                string rStoreId = payload.TryGetProperty("storeId", out var rs) ? rs.GetString() ?? string.Empty : string.Empty;
                string rFp = payload.TryGetProperty("fingerprint", out var rf) ? rf.GetString() ?? string.Empty : string.Empty;
                bool released = await _mapiService.ReleaseEmailAsync(rEntryId, rStoreId, rFp, 6, _cts.Token);
                return new { ok = released };

            case "quarantine-email":
                string qEntryId = payload.TryGetProperty("entryId", out var qe) ? qe.GetString() ?? string.Empty : string.Empty;
                string qStoreId = payload.TryGetProperty("storeId", out var qs) ? qs.GetString() ?? string.Empty : string.Empty;
                bool quarantined = await _mapiService.QuarantineEmailAsync(qEntryId, qStoreId, 23, _cts.Token);
                return new { ok = quarantined };

            case "delete-email":
                string dEntryId = payload.TryGetProperty("entryId", out var de) ? de.GetString() ?? string.Empty : string.Empty;
                var delResult = await _mapiService.DeleteEmailsAsync([dEntryId], null, _cts.Token);
                return new { ok = delResult.Success, count = delResult.Count };

            case "open-email":
                string oEntryId = payload.TryGetProperty("entryId", out var oe) ? oe.GetString() ?? string.Empty : string.Empty;
                string oStoreId = payload.TryGetProperty("storeId", out var os) ? os.GetString() ?? string.Empty : string.Empty;
                bool opened = await _mapiService.OpenInOutlookAsync(oEntryId, oStoreId, null, null, _cts.Token);
                return new { ok = opened };

            case "verify-existence":
                return new { ok = true };

            case "search-vault":
                string q = payload.TryGetProperty("query", out var qp) ? qp.GetString() ?? string.Empty : string.Empty;
                int limit = payload.TryGetProperty("limit", out var lp) ? lp.GetInt32() : 50;
                int offset = payload.TryGetProperty("offset", out var op) ? op.GetInt32() : 0;
                string? folderFilter = payload.TryGetProperty("folder", out var fp) ? fp.GetString() : null;
                return await Task.Run(() =>
                {
                    var searchRes = _vault.SearchEmails(new SearchParameters
                    {
                        Query = q,
                        Limit = limit,
                        Offset = offset,
                        Folder = folderFilter ?? string.Empty,
                        SortBy = "date",
                        SortOrder = "desc"
                    });
                    return (object)new { items = searchRes.Rows, total = searchRes.Total };
                });

            case "get-sender-suggestions":
                string prefix = payload.GetString() ?? string.Empty;
                return await Task.Run(() => (object)_vault.GetSenderSuggestions(prefix, 10));

            case "scan-duplicates":
                var dupRes = await _mapiService.ScanDuplicatesAsync(_cts.Token);
                _vault.SaveDuplicates(dupRes.Items, dupRes.Paused ? "Paused" : "Finished");
                return new { items = dupRes.Items, count = dupRes.ScannedCount };

            case "get-saved-duplicates":
                return await Task.Run(() =>
                {
                    var savedDups = _vault.GetSavedDuplicates();
                    return (object)new { items = savedDups.Items, status = savedDups.Status, savedAt = savedDups.SavedAt };
                });

            case "delete-duplicates":
                string dupMode = payload.TryGetProperty("mode", out var mProp) ? mProp.GetString() ?? "safe" : "safe";
                List<DuplicateItem> dupItems = [];
                if (payload.TryGetProperty("items", out var itemsProp) && itemsProp.ValueKind == JsonValueKind.Array)
                {
                    dupItems = JsonSerializer.Deserialize(itemsProp.GetRawText(), DeskGuardJsonContext.Default.ListDuplicateItem) ?? [];
                }
                var cleanRes = await _mapiService.CleanDuplicatesAsync(dupItems, dupMode, _cts.Token);
                return new { ok = true, count = cleanRes.SuccessCount, successCount = cleanRes.SuccessCount, skippedCount = cleanRes.SkippedCount };

            case "get-events":
                int evLimit = payload.TryGetProperty("limit", out var elp) ? elp.GetInt32() : 100;
                return await Task.Run(() => (object)_vault.GetEvents(evLimit));

            case "get-forensics":
                string forId = payload.GetString() ?? string.Empty;
                return await Task.Run(() =>
                {
                    var emailRec = _vault.GetEmailById(forId);
                    if (emailRec != null)
                    {
                        return (object)new
                        {
                            subject = emailRec.Subject,
                            sender = emailRec.Sender,
                            recipient = emailRec.Recipient,
                            date = emailRec.Date,
                            score = emailRec.Score,
                            verdict = emailRec.Verdict,
                            body = emailRec.Body,
                            penalties = emailRec.PenaltiesJson
                        };
                    }
                    return (object)new { };
                });

            case "score-what-if":
                return new { score = 85.0, verdict = "Clean" };

            case "scan-virus":
                string vId = payload.GetString() ?? string.Empty;
                return await CloudThreatService.ScanVirusAsync(vId, _config.VtKey, _config.ThreatIntelligenceLevel, _cts.Token);

            case "trust-sender":
                string senderEmail = payload.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(senderEmail))
                {
                    _vault.AddProtected([senderEmail], "released");
                }
                return new { ok = true };

            case "app-reset":
                string resetMode = payload.ValueKind == JsonValueKind.String ? payload.GetString() ?? "safe" : "safe";
                if (string.Equals(resetMode, "scorched", StringComparison.OrdinalIgnoreCase))
                {
                    StartupManager.SetStartupEnabled(false);
                    _vault.NuclearReset();
                }
                else
                {
                    _vault.ClearIncidents();
                }
                return new { ok = true };

            default:
                return new { ok = true };
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        if (_instance != null)
        {
            if (msg == WM_SIZE)
            {
                _instance.OnResize();
                return IntPtr.Zero;
            }
            if (msg == WM_CLOSE)
            {
                ShowWindow(hWnd, SW_HIDE);
                return IntPtr.Zero;
            }
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void OnResize()
    {
        if (_controller != null && _hwnd != IntPtr.Zero)
        {
            if (GetClientRect(_hwnd, out RECT rc))
            {
                _controller.Bounds = new Rectangle(0, 0, rc.Right - rc.Left, rc.Bottom - rc.Top);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _controller?.Close();
        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }
    }
}
