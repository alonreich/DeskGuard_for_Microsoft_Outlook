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
using DeskGuard.Interop.Outlook.Com;
using Microsoft.Web.WebView2.Core;

internal sealed partial class DashboardWindow : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static DashboardWindow? _instance;
    private readonly IntPtr _hwnd;
    private readonly VaultRepository _vault;
    private readonly OutlookMapiService _mapiService;
    private readonly ScoringEngine _scoringEngine;
    private ScanConfiguration _config;
    private readonly string _configFilePath;
    private readonly CancellationTokenSource _cts = new();
    private CancellationTokenSource? _dupScanCts;
    private bool _lastIncludeJunk;

    private void SaveConfig() => _config.Save(_configFilePath);

    public static void Log(string msg)
    {
        try
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeskGuard",
                "webview2.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
        Console.WriteLine($"[Dashboard] {msg}");
    }

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
    private const uint WM_NCCALCSIZE = 0x0083;
    private const uint WM_NCHITTEST = 0x0084;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_SYSCOMMAND = 0x0112;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

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

    [LibraryImport("user32.dll", EntryPoint = "GetWindowRect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

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
        _configFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "data",
            "config.json");
        _scoringEngine = new ScoringEngine();
        _mapiService.DuplicateProgress += (count, store, folder) =>
        {
            PostEvent("duplicate-update", new { status = "Progress", scanned = count, store = store, currentFolder = folder });
        };

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
        int width = _config.WindowWidth > 400 ? _config.WindowWidth : DefaultWidth;
        int height = _config.WindowHeight > 300 ? _config.WindowHeight : DefaultHeight;
        int x = _config.WindowX >= 0 ? _config.WindowX : Math.Max(0, (screenWidth - width) / 2);
        int y = _config.WindowY >= 0 ? _config.WindowY : Math.Max(0, (screenHeight - height) / 2);

        if (x < 0 || x > screenWidth - 100) x = Math.Max(0, (screenWidth - width) / 2);
        if (y < 0 || y > screenHeight - 100) y = Math.Max(0, (screenHeight - height) / 2);

        _hwnd = CreateWindowExW(
            0,
            WindowClassName,
            WindowTitle,
            WS_POPUP | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
            x, y, width, height,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);
    }

    public void Initialize()
    {
        if (_isInitialized || _hwnd == IntPtr.Zero) return;

        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeskGuard",
            "webview2");
        Directory.CreateDirectory(userDataFolder);

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

                try
                {
                    _coreWebView.Settings.IsNonClientRegionSupportEnabled = true;
                }
                catch { }

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
    }

    public void ShowAndFocus()
    {
        if (_hwnd == IntPtr.Zero) return;
        if (_config.WindowMaximized)
        {
            ShowWindow(_hwnd, SW_MAXIMIZE);
        }
        else
        {
            ShowWindow(_hwnd, SW_RESTORE);
        }
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
                ["data"] = JsonSerializer.SerializeToNode(data, JsonOptions)
            };
            _coreWebView.PostWebMessageAsJson(evt.ToJsonString());
        }
        catch (Exception ex)
        {
            Log($"[IPC] PostEvent error on '{channel}': {ex.Message}");
        }
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
                ["result"] = result != null ? JsonSerializer.SerializeToNode(result, JsonOptions) : null,
                ["error"] = error
            };
            _coreWebView.PostWebMessageAsJson(resp.ToJsonString());
        }
        catch (Exception ex)
        {
            Log($"[IPC] PostWebMessageResponse error for id={id}: {ex.Message}");
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string raw = string.Empty;
            try
            {
                raw = e.TryGetWebMessageAsString();
            }
            catch { }

            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = e.WebMessageAsJson;
            }

            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.String)
            {
                string? inner = root.GetString();
                if (!string.IsNullOrWhiteSpace(inner))
                {
                    using JsonDocument innerDoc = JsonDocument.Parse(inner);
                    await DispatchIpcMessageAsync(innerDoc.RootElement);
                    return;
                }
            }

            await DispatchIpcMessageAsync(root);
        }
        catch (Exception ex)
        {
            Log($"[IPC] OnWebMessageReceived parsing error: {ex}");
        }
    }

    private async Task DispatchIpcMessageAsync(JsonElement root)
    {
        long id = 0;
        if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
        {
            id = idProp.GetInt64();
        }

        string channel = string.Empty;
        if (root.TryGetProperty("channel", out var chProp) && chProp.ValueKind == JsonValueKind.String)
        {
            channel = chProp.GetString() ?? string.Empty;
        }

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
            Log($"[IPC] HandleIpcMessageAsync error on '{channel}': {ex}");
        }

        if (id > 0)
        {
            PostWebMessageResponse(id, result, error);
        }
    }

    private async Task<object?> HandleIpcMessageAsync(string channel, JsonElement payload)
    {
        switch (channel)
        {
            case "window-minimize":
                ShowWindow(_hwnd, SW_MINIMIZE);
                return new { ok = true };

            case "window-maximize":
                if (IsZoomed(_hwnd))
                {
                    ShowWindow(_hwnd, SW_RESTORE);
                    _config.WindowMaximized = false;
                }
                else
                {
                    ShowWindow(_hwnd, SW_MAXIMIZE);
                    _config.WindowMaximized = true;
                }
                SaveConfig();
                return new { ok = true, maximized = _config.WindowMaximized };

            case "window-hide":
            case "window-close":
                ShowWindow(_hwnd, SW_HIDE);
                return new { ok = true };

            case "window-drag":
                ReleaseCapture();
                SendMessageW(_hwnd, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                return new { ok = true };

            case "window-resize":
                int edge = 8;
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("edge", out var edgeProp))
                {
                    edge = edgeProp.GetInt32();
                }
                else if (payload.ValueKind == JsonValueKind.Number)
                {
                    edge = payload.GetInt32();
                }
                ReleaseCapture();
                SendMessageW(_hwnd, WM_SYSCOMMAND, (IntPtr)(0xF000 + edge), IntPtr.Zero);
                return new { ok = true };

            case "save-modal-config":
                string mId = payload.TryGetProperty("id", out var midP) ? midP.GetString() ?? "" : "";
                string mState = payload.TryGetProperty("state", out var mstP) ? mstP.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(mId))
                {
                    _config.ModalStates[mId] = mState;
                    SaveConfig();
                }
                return new { ok = true };

            case "get-modal-config":
                string qId = payload.TryGetProperty("id", out var qidP) ? qidP.GetString() ?? "" : "";
                string savedState = _config.ModalStates.GetValueOrDefault(qId, "");
                return new { ok = true, state = savedState };

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
                return new
                {
                    enabled = _config.Enabled,
                    onAccessEnabled = _config.OnAccessEnabled,
                    historyScanEnabled = _config.HistoryScanEnabled,
                    deepHistoryScanEnabled = _config.DeepHistoryScanEnabled,
                    onDemandLimit = _config.OnDemandLimit,
                    weights = _config.Weights,
                    toggles = _config.Toggles,
                    thresholds = _config.Thresholds,
                    whitelist = _config.Whitelist,
                    blacklist = _config.Blacklist,
                    spamKeywords = _config.SpamKeywords,
                    rubrics = new
                    {
                        version = 2,
                        weights = _config.Weights,
                        toggles = _config.Toggles,
                        maliciousThresholdPercent = _config.Thresholds.MaliciousThresholdPercent,
                        spamThresholdPercent = _config.Thresholds.SpamThresholdPercent,
                        suspiciousThresholdPercent = _config.Thresholds.SuspiciousThresholdPercent
                    },
                    rubricsList = _config.Rubrics,
                    threatIntelligenceLevel = _config.ThreatIntelligenceLevel,
                    scanningSpeed = _config.ScanningSpeed,
                    spamDestination = _config.SpamDestination,
                    labelInOutlook = _config.LabelInOutlook,
                    vtKey = _config.VtKey,
                    vtApiKey = _config.VtKey,
                    launchAtStartup = StartupManager.IsStartupEnabled()
                };

            case "set-enabled":
                if (payload.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    bool enable = payload.GetBoolean();
                    _config.Enabled = enable;
                    _mapiService.UpdateConfiguration(_config);
                    SaveConfig();
                    if (enable)
                    {
                        _ = _mapiService.StartLiveWatcherAsync(_cts.Token);
                    }
                    else
                    {
                        _ = _mapiService.StopLiveWatcherAsync();
                    }
                    PostEvent("status-sync", enable);
                }
                return new { ok = true };

            case "set-history-enabled":
                if (payload.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    _config.HistoryScanEnabled = payload.GetBoolean();
                    _mapiService.UpdateConfiguration(_config);
                    SaveConfig();
                }
                return new { ok = true };

            case "set-scanning-speed":
                if (payload.ValueKind == JsonValueKind.Number)
                {
                    int speed = payload.GetInt32();
                    _config.ScanningSpeed = speed;
                    _mapiService.SetScanSpeed(speed);
                    SaveConfig();
                }
                return new { ok = true };

            case "set-threat-intel-level":
                if (payload.ValueKind == JsonValueKind.Number)
                {
                    _config.ThreatIntelligenceLevel = payload.GetInt32();
                    SaveConfig();
                }
                return new { ok = true };

            case "set-vt-key":
                if (payload.ValueKind == JsonValueKind.String)
                {
                    _config.VtKey = payload.GetString() ?? string.Empty;
                    SaveConfig();
                }
                return new { ok = true };

            case "set-rubrics":
                if (payload.ValueKind == JsonValueKind.Object)
                {
                    if (payload.TryGetProperty("weights", out var wElem) && wElem.ValueKind == JsonValueKind.Object)
                    {
                        _config.Weights.Clear();
                        foreach (var prop in wElem.EnumerateObject())
                        {
                            if (prop.Value.TryGetDouble(out double val))
                                _config.Weights[prop.Name] = val;
                        }
                    }
                    if (payload.TryGetProperty("toggles", out var tElem) && tElem.ValueKind == JsonValueKind.Object)
                    {
                        _config.Toggles.Clear();
                        foreach (var prop in tElem.EnumerateObject())
                        {
                            _config.Toggles[prop.Name] = prop.Value.GetBoolean();
                        }
                    }
                    if (payload.TryGetProperty("maliciousThresholdPercent", out var mt) && mt.TryGetDouble(out double mtv))
                        _config.Thresholds.MaliciousThresholdPercent = mtv;
                    if (payload.TryGetProperty("spamThresholdPercent", out var st) && st.TryGetDouble(out double stv))
                        _config.Thresholds.SpamThresholdPercent = stv;
                    if (payload.TryGetProperty("suspiciousThresholdPercent", out var sst) && sst.TryGetDouble(out double sstv))
                        _config.Thresholds.SuspiciousThresholdPercent = sstv;
                    _mapiService.UpdateConfiguration(_config);
                    SaveConfig();
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
                    SaveConfig();
                }
                return new { ok = true };

            case "start-full-scan":
                _ = Task.Run(async () =>
                {
                    try
                    {
                        PostEvent("outlook-scan-update", new { status = "SCANNING", count = 0, total = 0 });
                        PostEvent("live-log", "Starting deep mailbox audit across all historical items and folders...");
                        await _mapiService.RunSweepAsync(DateTime.MinValue, _cts.Token);
                        PostEvent("outlook-scan-update", new { status = "Finished" });

                        var statsDict = _vault.GetAllCategorizedEmails(100);
                        var engineStats = _vault.GetStats();
                        PostEvent("stats-update", new
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
                        });
                    }
                    catch (Exception ex)
                    {
                        PostEvent("live-log", $"[Full Scan Error] {ex.Message}");
                        PostEvent("outlook-scan-update", new { status = "Finished" });
                    }
                });
                return new { ok = true, started = true };

            case "set-first-run":
                return new { ok = true };

            case "set-startup":
                bool startup = payload.ValueKind == JsonValueKind.True;
                StartupManager.SetStartupEnabled(startup);
                return new { ok = true };

            case "check-startup":
                bool isStartup = StartupManager.IsStartupEnabled();
                return new { systemActive = isStartup, enabled = isStartup, configEnabled = isStartup };

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
                return new { ok = released, success = released };

            case "quarantine-email":
                string qEntryId = payload.TryGetProperty("entryId", out var qe) ? qe.GetString() ?? string.Empty : string.Empty;
                string qStoreId = payload.TryGetProperty("storeId", out var qs) ? qs.GetString() ?? string.Empty : string.Empty;
                bool quarantined = await _mapiService.QuarantineEmailAsync(qEntryId, qStoreId, 23, _cts.Token);
                return new { ok = quarantined, success = quarantined };

            case "delete-email":
                List<string> delIds = [];
                if (payload.TryGetProperty("entryIds", out var idsProp) && idsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var elem in idsProp.EnumerateArray())
                    {
                        string s = elem.GetString() ?? string.Empty;
                        if (!string.IsNullOrEmpty(s)) delIds.Add(s);
                    }
                }
                if (delIds.Count == 0 && payload.TryGetProperty("entryId", out var de))
                {
                    string s = de.GetString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(s)) delIds.Add(s);
                }
                var delResult = await _mapiService.DeleteEmailsAsync(delIds, null, _cts.Token);
                return new { ok = delResult.Success, success = delResult.Success, count = delResult.Count };

            case "open-email":
                string oEntryId = payload.TryGetProperty("entryId", out var oe) ? oe.GetString() ?? string.Empty : string.Empty;
                string oStoreId = payload.TryGetProperty("storeId", out var os) ? os.GetString() ?? string.Empty : string.Empty;
                string oFingerprint = payload.TryGetProperty("fingerprint", out var ofp) ? ofp.GetString() ?? string.Empty : string.Empty;
                string oMessageId = payload.TryGetProperty("messageId", out var omid) ? omid.GetString() ?? string.Empty : string.Empty;

                if (!string.IsNullOrEmpty(oEntryId) && (string.IsNullOrEmpty(oFingerprint) || string.IsNullOrEmpty(oMessageId)))
                {
                    try
                    {
                        var rec = _vault.GetEmailById(oEntryId);
                        if (rec != null)
                        {
                            if (string.IsNullOrEmpty(oFingerprint)) oFingerprint = rec.Fingerprint;
                            if (string.IsNullOrEmpty(oMessageId)) oMessageId = rec.MessageId;
                            if (string.IsNullOrEmpty(oStoreId)) oStoreId = rec.StoreId;
                        }
                    }
                    catch { }
                }

                bool opened = await _mapiService.OpenInOutlookAsync(oEntryId, oStoreId, oFingerprint, oMessageId, _cts.Token);
                return new { ok = opened, success = opened };

            case "verify-existence":
                List<EmailRecord> itemsToCheck = [];
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var itemElem in itemsElem.EnumerateArray())
                    {
                        string eId = itemElem.TryGetProperty("entryId", out var ep) ? ep.GetString() ?? string.Empty : (itemElem.TryGetProperty("id", out var ip) ? ip.GetString() ?? string.Empty : string.Empty);
                        string sId = itemElem.TryGetProperty("storeId", out var sp) ? sp.GetString() ?? string.Empty : string.Empty;
                        string fpVal = itemElem.TryGetProperty("fingerprint", out var fpProp) ? fpProp.GetString() ?? string.Empty : (itemElem.TryGetProperty("messageKey", out var mkProp) ? mkProp.GetString() ?? string.Empty : string.Empty);
                        string vVal = itemElem.TryGetProperty("verdict", out var vProp) ? vProp.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrEmpty(eId))
                        {
                            itemsToCheck.Add(new EmailRecord { EntryId = eId, StoreId = sId, Fingerprint = fpVal, Verdict = vVal });
                        }
                    }
                }
                var checkRes = await _mapiService.CheckExistenceAsync(itemsToCheck, _cts.Token);
                return new { ok = true, success = true, removedCount = checkRes.RemovedEntryIds.Count, userMovedCount = checkRes.UserMovedEmails.Count };

            case "search-vault":
                string q = payload.TryGetProperty("query", out var qp) ? qp.GetString() ?? string.Empty : string.Empty;
                int limit = payload.TryGetProperty("limit", out var lp) ? lp.GetInt32() : 50;
                int offset = payload.TryGetProperty("offset", out var op) ? op.GetInt32() : 0;
                string? folderFilter = payload.TryGetProperty("folder", out var fp) ? fp.GetString() : null;
                string catFilter = payload.TryGetProperty("category", out var cp) ? cp.GetString() ?? string.Empty : string.Empty;
                string verFilter = payload.TryGetProperty("verdict", out var vp) ? vp.GetString() ?? string.Empty : string.Empty;
                string dFrom = payload.TryGetProperty("dateFrom", out var dfp) ? dfp.GetString() ?? string.Empty : string.Empty;
                string dTo = payload.TryGetProperty("dateTo", out var dtp) ? dtp.GetString() ?? string.Empty : string.Empty;
                bool hasAtt = payload.TryGetProperty("hasAttachment", out var hap) && hap.GetBoolean();
                string sBy = payload.TryGetProperty("sortBy", out var sbp) ? sbp.GetString() ?? "date" : "date";
                string sOrd = payload.TryGetProperty("sortOrder", out var sop) ? sop.GetString() ?? "desc" : "desc";
                string senderFilter = payload.TryGetProperty("fromFilter", out var ffp) ? ffp.GetString() ?? string.Empty : string.Empty;

                return await Task.Run(() =>
                {
                    string effectiveQuery = q;
                    if (!string.IsNullOrWhiteSpace(senderFilter))
                    {
                        effectiveQuery = string.IsNullOrWhiteSpace(effectiveQuery) ? senderFilter : $"{effectiveQuery} {senderFilter}";
                    }

                    var searchRes = _vault.SearchEmails(new SearchParameters
                    {
                        Query = effectiveQuery,
                        Category = catFilter,
                        Verdict = verFilter,
                        DateFrom = dFrom,
                        DateTo = dTo,
                        HasAttachment = hasAtt,
                        Limit = limit,
                        Offset = offset,
                        Folder = folderFilter ?? string.Empty,
                        SortBy = sBy,
                        SortOrder = sOrd
                    });

                    return (object)new
                    {
                        ok = true,
                        success = true,
                        items = searchRes.Rows,
                        rows = searchRes.Rows,
                        total = searchRes.Total,
                        results = new
                        {
                            rows = searchRes.Rows,
                            total = searchRes.Total
                        }
                    };
                });

            case "get-sender-suggestions":
                string prefix = payload.ValueKind == JsonValueKind.String ? payload.GetString() ?? string.Empty : string.Empty;
                var suggestionsList = await Task.Run(() => _vault.GetSenderSuggestions(prefix, 10));
                return new { ok = true, success = true, suggestions = suggestionsList };

            case "scan-duplicates":
                bool includeJunk = false;
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("includeJunk", out var ijProp))
                {
                    includeJunk = ijProp.GetBoolean();
                }
                _lastIncludeJunk = includeJunk;
                _dupScanCts?.Cancel();
                _dupScanCts = new CancellationTokenSource();
                var dupToken = _dupScanCts.Token;

                List<DuplicateItem> allDups;
                int totalScanned;
                bool wasPaused = false;

                if (_mapiService.IsConnected)
                {
                    var dupRes = await _mapiService.ScanDuplicatesAsync(includeJunk, dupToken);
                    allDups = dupRes.Items;
                    totalScanned = dupRes.ScannedCount;
                    wasPaused = dupRes.Paused;
                }
                else
                {
                    var (vaultDups, indexedCount) = await Task.Run(() => _vault.FindDuplicates(includeJunk));
                    allDups = vaultDups;
                    totalScanned = indexedCount;
                }

                string scanStatus = wasPaused ? "Paused" : "Finished";
                await Task.Run(() => _vault.SaveDuplicates(allDups, scanStatus));
                PostEvent("duplicate-update", new { status = scanStatus, items = allDups, scanned = totalScanned, found = allDups.Count });
                return new { ok = true, items = allDups, count = totalScanned, paused = wasPaused };

            case "pause-duplicate-scan":
                _dupScanCts?.Cancel();
                return new { ok = true };

            case "resume-duplicate-scan":
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var resumeElement = JsonSerializer.SerializeToElement(new { includeJunk = _lastIncludeJunk });
                        await HandleIpcMessageAsync("scan-duplicates", resumeElement);
                    }
                    catch { }
                });
                return new { ok = true };

            case "reset-duplicate-engine":
                _dupScanCts?.Cancel();
                await Task.Run(() => _vault.SaveDuplicates([], "Finished"));
                PostEvent("duplicate-update", new { status = "Finished", items = new List<DuplicateItem>(), scanned = 0, found = 0 });
                return new { ok = true };

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
                string? evKind = payload.TryGetProperty("kind", out var ekp) && ekp.ValueKind == JsonValueKind.String ? ekp.GetString() : null;
                return await Task.Run(() => (object)_vault.GetEvents(evLimit, 0, evKind));

            case "get-forensics":
                string forId = string.Empty;
                if (payload.ValueKind == JsonValueKind.String)
                {
                    forId = payload.GetString() ?? string.Empty;
                }
                else if (payload.ValueKind == JsonValueKind.Object)
                {
                    if (payload.TryGetProperty("entryId", out var ep) && ep.ValueKind == JsonValueKind.String)
                        forId = ep.GetString() ?? string.Empty;
                    else if (payload.TryGetProperty("fingerprint", out var fingerProp) && fingerProp.ValueKind == JsonValueKind.String)
                        forId = fingerProp.GetString() ?? string.Empty;
                    else if (payload.TryGetProperty("id", out var ip) && ip.ValueKind == JsonValueKind.String)
                        forId = ip.GetString() ?? string.Empty;
                }
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
                            headers = emailRec.Headers,
                            fullHeaders = emailRec.Headers,
                            penalties = emailRec.PenaltiesJson
                        };
                    }
                    return (object)new { };
                });

            case "score-what-if":
                return new { score = 85.0, verdict = "Clean" };

            case "scan-virus":
                string vId = payload.ValueKind == JsonValueKind.String ? (payload.GetString() ?? string.Empty) : string.Empty;
                if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("id", out var idProp))
                {
                    vId = idProp.GetString() ?? string.Empty;
                }

                if (_config.ThreatIntelligenceLevel >= 2 && (string.IsNullOrWhiteSpace(_config.VtKey) || _config.VtKey.Trim().Length < 16 || _config.VtKey == "MASKED_FOR_SECURITY"))
                {
                    return new
                    {
                        success = false,
                        code = "NO_API_KEY",
                        error = "VirusTotal API key is not configured. Please enter your API key to enable live threat intelligence scanning."
                    };
                }

                var emailRec = await Task.Run(() => _vault.GetEmailById(vId));
                List<AttachmentInfo> attList = [];
                if (emailRec != null && !string.IsNullOrEmpty(emailRec.AttachmentsJson))
                {
                    try
                    {
                        attList = JsonSerializer.Deserialize(emailRec.AttachmentsJson, DeskGuardJsonContext.Default.ListAttachmentInfo) ?? [];
                    }
                    catch { }
                }

                if ((attList.Count == 0 || attList.All(a => string.IsNullOrEmpty(a.Hash) || a.Hash == "N/A")) && _mapiService.IsConnected && emailRec != null)
                {
                    try
                    {
                        var fetched = await _mapiService.GetAttachmentsWithHashesAsync(emailRec.EntryId, emailRec.StoreId, _cts.Token);
                        if (fetched.Count > 0)
                        {
                            attList = fetched;
                        }
                    }
                    catch { }
                }

                List<object> reportItems = [];
                int threatsFound = 0;

                if (attList.Count > 0)
                {
                    foreach (var att in attList)
                    {
                        string hashToScan = (!string.IsNullOrEmpty(att.Hash) && att.Hash != "N/A") ? att.Hash : OutlookItemAccessor.ComputeSha256(att.Name + att.Size);
                        var scanRes = await CloudThreatService.ScanVirusAsync(hashToScan, _config.VtKey, _config.ThreatIntelligenceLevel, _cts.Token);
                        if (scanRes.IsMalicious) threatsFound++;
                        reportItems.Add(new
                        {
                            name = att.Name,
                            type = !string.IsNullOrEmpty(att.Type) ? att.Type : "file",
                            status = scanRes.Verdict,
                            hash = hashToScan,
                            malicious = scanRes.IsMalicious ? 1 : 0,
                            suspicious = 0
                        });
                    }
                }
                else
                {
                    string bodyHash = emailRec != null ? OutlookItemAccessor.ComputeSha256(emailRec.Body + emailRec.Subject) : OutlookItemAccessor.ComputeSha256(vId);
                    var scanRes = await CloudThreatService.ScanVirusAsync(bodyHash, _config.VtKey, _config.ThreatIntelligenceLevel, _cts.Token);
                    if (scanRes.IsMalicious) threatsFound++;
                    reportItems.Add(new
                    {
                        name = (emailRec?.Subject != null && emailRec.Subject.Length > 0) ? emailRec.Subject : "Message Content",
                        type = "rfc822/digest",
                        status = scanRes.Verdict,
                        hash = bodyHash,
                        malicious = scanRes.IsMalicious ? 1 : 0,
                        suspicious = 0
                    });
                }

                return new
                {
                    success = true,
                    data = new
                    {
                        subject = emailRec?.Subject ?? "Incident Threat Audit",
                        sender = emailRec?.Sender ?? "Unknown",
                        ip = "Internal Network",
                        status = threatsFound > 0 ? "Threat Detected" : "Audit Complete - Clean",
                        vtQueried = !string.IsNullOrWhiteSpace(_config.VtKey) && _config.ThreatIntelligenceLevel >= 2,
                        threatsFound = threatsFound,
                        items = reportItems
                    }
                };

            case "trust-sender":
                string senderEmail = payload.ValueKind == JsonValueKind.String ? (payload.GetString() ?? string.Empty).Trim() : string.Empty;
                if (!string.IsNullOrEmpty(senderEmail))
                {
                    if (!_config.Whitelist.Any(w => string.Equals(w, senderEmail, StringComparison.OrdinalIgnoreCase)))
                    {
                        _config.Whitelist.Add(senderEmail);
                        _mapiService.UpdateConfiguration(_config);
                        SaveConfig();
                    }
                    await Task.Run(() =>
                    {
                        _vault.AddProtected([senderEmail], "released");
                        _vault.RecordEvent(new SecurityEvent
                        {
                            Kind = "Trust",
                            Sender = senderEmail,
                            Subject = $"Trusted sender: {senderEmail}",
                            FromFolder = "Inbox",
                            ToFolder = "Trusted",
                            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    });
                }
                return new { ok = true };

            case "set-whitelist":
                _config.Whitelist = ParseStringList(payload);
                _mapiService.UpdateConfiguration(_config);
                SaveConfig();
                return new { ok = true };

            case "set-blacklist":
                _config.Blacklist = ParseStringList(payload);
                _mapiService.UpdateConfiguration(_config);
                SaveConfig();
                return new { ok = true };

            case "set-spam-keywords":
                _config.SpamKeywords = ParseStringList(payload);
                _mapiService.UpdateConfiguration(_config);
                SaveConfig();
                return new { ok = true };

            case "export-config":
                return new
                {
                    ok = true,
                    success = true,
                    config = _config,
                    json = JsonSerializer.Serialize(_config, DeskGuardJsonContext.Default.ScanConfiguration)
                };

            case "import-config":
                try
                {
                    ScanConfiguration? importedCfg = null;
                    if (payload.ValueKind == JsonValueKind.Object)
                    {
                        importedCfg = JsonSerializer.Deserialize(payload.GetRawText(), DeskGuardJsonContext.Default.ScanConfiguration);
                    }
                    else if (payload.ValueKind == JsonValueKind.String)
                    {
                        string s = payload.GetString() ?? "";
                        if (s.StartsWith('{'))
                        {
                            importedCfg = JsonSerializer.Deserialize(s, DeskGuardJsonContext.Default.ScanConfiguration);
                        }
                    }
                    if (importedCfg != null)
                    {
                        _config = importedCfg;
                        _mapiService.UpdateConfiguration(_config);
                        SaveConfig();
                        return new { ok = true, success = true };
                    }
                }
                catch (Exception ex)
                {
                    return new { ok = false, success = false, error = ex.Message };
                }
                return new { ok = false, success = false, error = "Invalid configuration data" };

            case "app-reset":
                string resetMode = payload.ValueKind == JsonValueKind.String ? payload.GetString() ?? "safe" : "safe";
                await Task.Run(() =>
                {
                    if (string.Equals(resetMode, "scorched", StringComparison.OrdinalIgnoreCase))
                    {
                        StartupManager.SetStartupEnabled(false);
                        _vault.NuclearReset();
                        _config = new ScanConfiguration();
                        SaveConfig();
                        _mapiService.UpdateConfiguration(_config);
                        _mapiService.ClearProcessedCache(clearProtected: true);
                    }
                    else
                    {
                        _vault.ClearIncidents();
                        _mapiService.ClearProcessedCache(clearProtected: false);
                    }
                });
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
            if (msg == WM_NCCALCSIZE && wParam != UIntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (msg == WM_NCHITTEST)
            {
                int xPos = unchecked((short)(long)lParam);
                int yPos = unchecked((short)((long)lParam >> 16));

                if (GetWindowRect(hWnd, out RECT winRect))
                {
                    const int borderThickness = 8;
                    bool left = xPos >= winRect.Left && xPos < winRect.Left + borderThickness;
                    bool right = xPos < winRect.Right && xPos >= winRect.Right - borderThickness;
                    bool top = yPos >= winRect.Top && yPos < winRect.Top + borderThickness;
                    bool bottom = yPos < winRect.Bottom && yPos >= winRect.Bottom - borderThickness;

                    if (top && left) return (IntPtr)HTTOPLEFT;
                    if (top && right) return (IntPtr)HTTOPRIGHT;
                    if (bottom && left) return (IntPtr)HTBOTTOMLEFT;
                    if (bottom && right) return (IntPtr)HTBOTTOMRIGHT;
                    if (left) return (IntPtr)HTLEFT;
                    if (right) return (IntPtr)HTRIGHT;
                    if (top) return (IntPtr)HTTOP;
                    if (bottom) return (IntPtr)HTBOTTOM;
                }
            }

            if (msg == WM_SIZE)
            {
                _instance.OnResize();
                return IntPtr.Zero;
            }

            if (msg == WM_EXITSIZEMOVE)
            {
                _instance.OnExitSizeMove();
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

    private void OnExitSizeMove()
    {
        if (_hwnd == IntPtr.Zero) return;
        bool isMax = IsZoomed(_hwnd);
        _config.WindowMaximized = isMax;
        if (!isMax && GetWindowRect(_hwnd, out RECT rc))
        {
            _config.WindowX = rc.Left;
            _config.WindowY = rc.Top;
            _config.WindowWidth = rc.Right - rc.Left;
            _config.WindowHeight = rc.Bottom - rc.Top;
        }
        SaveConfig();
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

    private static List<string> ParseStringList(JsonElement element)
    {
        List<string> list = [];
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    string s = (item.GetString() ?? string.Empty).Trim();
                    if (!string.IsNullOrEmpty(s) && !list.Contains(s, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(s);
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("emails", out var emailsElem) && emailsElem.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in emailsElem.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string s = (item.GetString() ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(s) && !list.Contains(s, StringComparer.OrdinalIgnoreCase))
                        {
                            list.Add(s);
                        }
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            string s = (element.GetString() ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        return list;
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
