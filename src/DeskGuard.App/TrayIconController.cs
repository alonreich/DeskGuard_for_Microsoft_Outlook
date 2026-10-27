namespace DeskGuard.App;

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

internal sealed partial class TrayIconController : IDisposable
{
    private static TrayIconController? _instance;
    private readonly IntPtr _hwnd;
    private readonly IntPtr _hIcon;
    private bool _iconAdded;
    private string _currentStatus = "Initializing...";
    private int _scannedCount;
    private int _threatCount;

    public event Action? RequestOpenDashboard;
    public event Action? RequestSweep;
    public event Action? RequestDuplicateScan;
    public event Action? RequestExit;
    public event Action? WatchdogTick;
    public string DataDirectory { get; set; } = string.Empty;

    public const uint WM_USER = 0x0400;
    public const uint WM_TRAY_CALLBACK = WM_USER + 100;
    public const uint WM_TRAY_SHOW_STATUS = WM_USER + 101;

    private const uint WM_CREATE = 0x0001;
    private const uint WM_DESTROY = 0x0002;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_NULL = 0x0000;
    private const uint WM_QUIT = 0x0012;
    private const uint WM_TIMER = 0x0113;

    private UIntPtr _timerId;

    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_NONE = 0x00000000;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;

    private const uint MF_STRING = 0x00000000;
    private const uint MF_DISABLED = 0x00000002;
    private const uint MF_SEPARATOR = 0x00000800;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_BOTTOMALIGN = 0x0020;
    private const uint TPM_RETURNCMD = 0x0100;

    private const int IDI_APPLICATION = 32512;
    private const int IDC_ARROW = 32512;
    private const uint WS_OVERLAPPED = 0x00000000;

    private const uint MenuHeader = 1001;
    private const uint MenuStatus = 1002;
    private const uint MenuOpenDashboard = 1003;
    private const uint MenuRunSweep = 1004;
    private const uint MenuDuplicates = 1005;
    private const uint MenuOpenData = 1006;
    private const uint MenuExit = 1007;

    public const string WindowClassName = "DeskGuard_Tray_WindowClass";
    public const string WindowTitle = "DeskGuard_Tray_Window";

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
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
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

    [LibraryImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Shell_NotifyIconW(uint dwMessage, NOTIFYICONDATAW* lpData);

    [LibraryImport("shell32.dll", EntryPoint = "ExtractIconExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint ExtractIconExW(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [LibraryImport("user32.dll", EntryPoint = "LoadIconW")]
    private static partial IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [LibraryImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static partial IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW")]
    private static partial IntPtr GetModuleHandleW(IntPtr lpModuleName);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll", EntryPoint = "SetForegroundWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", EntryPoint = "CreatePopupMenu")]
    private static partial IntPtr CreatePopupMenu();

    [LibraryImport("user32.dll", EntryPoint = "AppendMenuW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [LibraryImport("user32.dll", EntryPoint = "TrackPopupMenuEx")]
    private static partial uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lpTPMParams);

    [LibraryImport("user32.dll", EntryPoint = "DestroyMenu")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyMenu(IntPtr hMenu);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    private static partial int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll", EntryPoint = "PostQuitMessage")]
    private static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll", EntryPoint = "SetTimer")]
    private static partial UIntPtr SetTimer(IntPtr hWnd, UIntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

    [LibraryImport("user32.dll", EntryPoint = "KillTimer")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool KillTimer(IntPtr hWnd, UIntPtr uIDEvent);

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll", EntryPoint = "TranslateMessage")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessageW(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    public static bool SignalExistingInstance()
    {
        IntPtr hwnd = FindWindowW(WindowClassName, WindowTitle);
        if (hwnd != IntPtr.Zero)
        {
            PostMessageW(hwnd, WM_TRAY_SHOW_STATUS, UIntPtr.Zero, IntPtr.Zero);
            return true;
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    public TrayIconController()
    {
        _instance = this;
        IntPtr hInstance = GetModuleHandleW(IntPtr.Zero);

        _hIcon = LoadAppIcon();

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
                    hIcon = _hIcon,
                    hCursor = LoadCursorW(IntPtr.Zero, (IntPtr)IDC_ARROW),
                    hbrBackground = IntPtr.Zero,
                    lpszMenuName = IntPtr.Zero,
                    lpszClassName = (IntPtr)classNamePtr,
                    hIconSm = _hIcon
                };
                ushort atom = RegisterClassExW(&wc);
                if (atom == 0)
                {
                    int regErr = Marshal.GetLastPInvokeError();
                    if (regErr != 1410) // ERROR_CLASS_ALREADY_EXISTS
                    {
                        Console.WriteLine($"[Tray] RegisterClassExW error: {regErr}");
                    }
                }
            }
        }

        _hwnd = CreateWindowExW(
            0,
            WindowClassName,
            WindowTitle,
            WS_OVERLAPPED,
            0, 0, 0, 0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            int createErr = Marshal.GetLastPInvokeError();
            Console.WriteLine($"[Tray] CreateWindowExW failed: {createErr}");
        }

        AddTrayIcon();
    }

    private static IntPtr LoadAppIcon()
    {
        try
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
            {
                if (ExtractIconExW(procPath, 0, out _, out IntPtr smallIcon, 1) > 0 && smallIcon != IntPtr.Zero)
                {
                    return smallIcon;
                }
            }
        }
        catch { }

        return LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    private unsafe void AddTrayIcon()
    {
        NOTIFYICONDATAW nid = new()
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon
        };

        SetFixedString(nid.szTip, 128, "DeskGuard for Microsoft Outlook");
        _iconAdded = Shell_NotifyIconW(NIM_ADD, &nid);
        int notifyErr = Marshal.GetLastPInvokeError();
        Console.WriteLine($"[Tray] Init: HWND={_hwnd:X8}, IconAdded={_iconAdded} (err: {notifyErr})");
    }

    public unsafe void UpdateStatus(string status)
    {
        _currentStatus = status;
        if (!_iconAdded) return;

        NOTIFYICONDATAW nid = new()
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_TIP
        };

        string tip = $"DeskGuard - {status}";
        if (tip.Length > 127) tip = tip[..127];
        SetFixedString(nid.szTip, 128, tip);
        Shell_NotifyIconW(NIM_MODIFY, &nid);
    }

    public unsafe void ShowNotification(string title, string message, bool isWarning = false)
    {
        if (!_iconAdded) return;

        NOTIFYICONDATAW nid = new()
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_INFO,
            dwInfoFlags = isWarning ? NIIF_WARNING : NIIF_INFO
        };

        SetFixedString(nid.szInfoTitle, 64, title.Length > 63 ? title[..63] : title);
        SetFixedString(nid.szInfo, 256, message.Length > 255 ? message[..255] : message);
        Shell_NotifyIconW(NIM_MODIFY, &nid);
    }

    public void OnEmailScanned(string subject, string verdict, double score)
    {
        Interlocked.Increment(ref _scannedCount);
        if (verdict is "Malicious" or "Spam")
        {
            Interlocked.Increment(ref _threatCount);
            ShowNotification(
                $"DeskGuard: {verdict} Email",
                $"Subject: {subject}\nVerdict: {verdict} (Score: {score:F0})",
                isWarning: true);
        }
    }

    private static unsafe void SetFixedString(char* dest, int capacity, string value)
    {
        int len = Math.Min(value.Length, capacity - 1);
        for (int i = 0; i < len; i++) dest[i] = value[i];
        dest[len] = '\0';
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static IntPtr WndProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam)
    {
        if (_instance != null)
        {
            if (msg == WM_TRAY_CALLBACK)
            {
                uint mouseMsg = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouseMsg == WM_RBUTTONUP)
                {
                    _instance.ShowContextMenu();
                    return IntPtr.Zero;
                }
                if (mouseMsg is WM_LBUTTONUP or WM_LBUTTONDBLCLK)
                {
                    _instance.RequestOpenDashboard?.Invoke();
                    return IntPtr.Zero;
                }
            }
            else if (msg == WM_TRAY_SHOW_STATUS)
            {
                _instance.RequestOpenDashboard?.Invoke();
                return IntPtr.Zero;
            }
            else if (msg == WM_TIMER)
            {
                _instance.WatchdogTick?.Invoke();
                return IntPtr.Zero;
            }
        }

        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    private void ShowStatusBalloon()
    {
        ShowNotification(
            "DeskGuard Protection Status",
            $"Status: {_currentStatus}\nScanned emails: {_scannedCount}\nFlagged threats: {_threatCount}");
    }

    private void ShowContextMenu()
    {
        if (!GetCursorPos(out POINT pt)) return;

        SetForegroundWindow(_hwnd);
        IntPtr hMenu = CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            AppendMenuW(hMenu, MF_STRING | MF_DISABLED, (UIntPtr)MenuHeader, "DeskGuard for Microsoft Outlook");
            AppendMenuW(hMenu, MF_STRING | MF_DISABLED, (UIntPtr)MenuStatus, $"  Status: {_currentStatus}");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)MenuOpenDashboard, "Open Dashboard");
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)MenuRunSweep, "Run Mailbox Sweep Now");
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)MenuDuplicates, "Scan for Duplicates");
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)MenuOpenData, "Open Data Folder");
            AppendMenuW(hMenu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            AppendMenuW(hMenu, MF_STRING, (UIntPtr)MenuExit, "Exit DeskGuard");

            uint cmd = TrackPopupMenuEx(hMenu, TPM_LEFTALIGN | TPM_BOTTOMALIGN | TPM_RETURNCMD, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            PostMessageW(_hwnd, WM_NULL, UIntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case MenuOpenDashboard:
                    RequestOpenDashboard?.Invoke();
                    break;
                case MenuRunSweep:
                    RequestSweep?.Invoke();
                    break;
                case MenuDuplicates:
                    RequestDuplicateScan?.Invoke();
                    break;
                case MenuOpenData:
                    OpenDataFolder();
                    break;
                case MenuExit:
                    RequestExit?.Invoke();
                    break;
            }
        }
        finally
        {
            DestroyMenu(hMenu);
        }
    }

    private void OpenDataFolder()
    {
        try
        {
            string dir = string.IsNullOrEmpty(DataDirectory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskGuard", "data")
                : DataDirectory;

            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            ProcessStartInfo psi = new()
            {
                FileName = dir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }
        catch { }
    }

    public void StartWatchdog(uint intervalMs = 3000)
    {
        if (_hwnd != IntPtr.Zero && _timerId == UIntPtr.Zero)
        {
            _timerId = SetTimer(_hwnd, (UIntPtr)1, intervalMs, IntPtr.Zero);
        }
    }

    public void RunMessageLoop(CancellationToken ct)
    {
        using var reg = ct.Register(() =>
        {
            if (_hwnd != IntPtr.Zero)
            {
                PostMessageW(_hwnd, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            }
        });

        while (!ct.IsCancellationRequested && GetMessageW(out MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            if (msg.message == WM_QUIT) break;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public void Exit()
    {
        if (_hwnd != IntPtr.Zero)
        {
            PostMessageW(_hwnd, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    public void PumpMessages()
    {
        while (PeekMessageW(out MSG msg, IntPtr.Zero, 0, 0, 1))
        {
            if (msg.message == WM_QUIT) return;
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }

    public unsafe void Dispose()
    {
        if (_timerId != UIntPtr.Zero && _hwnd != IntPtr.Zero)
        {
            KillTimer(_hwnd, _timerId);
            _timerId = UIntPtr.Zero;
        }

        if (_iconAdded)
        {
            NOTIFYICONDATAW nid = new()
            {
                cbSize = (uint)sizeof(NOTIFYICONDATAW),
                hWnd = _hwnd,
                uID = 1
            };
            Shell_NotifyIconW(NIM_DELETE, &nid);
            _iconAdded = false;
        }

        if (_hwnd != IntPtr.Zero)
        {
            DestroyWindow(_hwnd);
        }
    }
}
