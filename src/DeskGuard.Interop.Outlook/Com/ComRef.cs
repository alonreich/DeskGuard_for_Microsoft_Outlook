namespace DeskGuard.Interop.Outlook.Com;

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

public readonly struct ComRef<T> : IDisposable where T : class
{
    public T Item { get; }

    public ComRef(T comObject)
    {
        Item = comObject;
    }

    public void Dispose()
    {
        if (Item != null && Marshal.IsComObject(Item))
        {
            try
            {
                Marshal.FinalReleaseComObject(Item);
            }
            catch
            {
                // Suppress COM release errors during process termination or teardown
            }
        }
    }

    public static implicit operator T(ComRef<T> wrapper) => wrapper.Item;
}

public static partial class ComUtils
{
    public static void Release(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try
            {
                Marshal.FinalReleaseComObject(obj);
            }
            catch { }
        }
    }

    [LibraryImport("ole32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoCreateInstance(Guid* rclsid, IntPtr pUnkOuter, uint dwClsContext, Guid* riid, out IntPtr ppv);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    private static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, out IntPtr ppunk);

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");

    public static object? GetActiveObject(string progId)
    {
        int hr = CLSIDFromProgID(progId, out Guid clsid);
        if (hr < 0)
        {
            DeskGuard.Core.Logging.AppLogger.Warn($"[ComRef] CLSIDFromProgID failed for '{progId}' with hr=0x{hr:X8}");
            return null;
        }

        try
        {
            GetActiveObject(ref clsid, IntPtr.Zero, out IntPtr pUnk);
            if (pUnk != IntPtr.Zero)
            {
                try
                {
                    return Marshal.GetObjectForIUnknown(pUnk);
                }
                finally
                {
                    Marshal.Release(pUnk);
                }
            }
            return null;
        }
        catch (COMException ex)
        {
            DeskGuard.Core.Logging.AppLogger.Info($"[ComRef] GetActiveObject('{progId}') unavailable: 0x{ex.ErrorCode:X8} ({ex.Message})");
            return null;
        }
        catch (Exception ex)
        {
            DeskGuard.Core.Logging.AppLogger.Warn($"[ComRef] GetActiveObject('{progId}') threw {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Outlook.Application COM activation via ProgID")]
    public static object? CreateOrGetOutlook()
    {
        DeskGuard.Core.Logging.AppLogger.Info("[ComRef] Attempting GetActiveObject('Outlook.Application')...");
        object? active = GetActiveObject("Outlook.Application");
        if (active != null)
        {
            DeskGuard.Core.Logging.AppLogger.Info("[ComRef] Successfully attached to running Outlook via GetActiveObject.");
            return active;
        }

        DeskGuard.Core.Logging.AppLogger.Info("[ComRef] Attempting COM activation via Type.GetTypeFromProgID('Outlook.Application')...");
        try
        {
            Type? olType = Type.GetTypeFromProgID("Outlook.Application");
            if (olType != null)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                object? instance = Activator.CreateInstance(olType);
                if (instance != null)
                {
                    DeskGuard.Core.Logging.AppLogger.Info($"[ComRef] Successfully activated Outlook instance via Activator.CreateInstance in {sw.ElapsedMilliseconds}ms.");
                    return instance;
                }
            }
            else
            {
                DeskGuard.Core.Logging.AppLogger.Warn("[ComRef] Type.GetTypeFromProgID('Outlook.Application') returned null.");
            }
        }
        catch (Exception ex)
        {
            DeskGuard.Core.Logging.AppLogger.Warn($"[ComRef] Activator.CreateInstance failed: {ex.Message} (HR: 0x{ex.HResult:X8})");
        }

        DeskGuard.Core.Logging.AppLogger.Info("[ComRef] Falling back to native CoCreateInstance with CLSCTX_LOCAL_SERVER...");
        int hr = CLSIDFromProgID("Outlook.Application", out Guid clsid);
        if (hr >= 0)
        {
            try
            {
                Guid iid = IidIUnknown;
                unsafe
                {
                    hr = CoCreateInstance(&clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, &iid, out IntPtr pUnk);
                    DeskGuard.Core.Logging.AppLogger.Info($"[ComRef] CoCreateInstance returned hr=0x{hr:X8}, pUnk={pUnk}");
                    if (hr >= 0 && pUnk != IntPtr.Zero)
                    {
                        try
                        {
                            return Marshal.GetObjectForIUnknown(pUnk);
                        }
                        finally
                        {
                            Marshal.Release(pUnk);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DeskGuard.Core.Logging.AppLogger.Error("[ComRef] CoCreateInstance threw exception", ex);
            }
        }
        else
        {
            DeskGuard.Core.Logging.AppLogger.Warn($"[ComRef] CLSIDFromProgID('Outlook.Application') returned hr=0x{hr:X8}");
        }

        DeskGuard.Core.Logging.AppLogger.Warn("[ComRef] All Outlook COM acquisition strategies exhausted; returning null.");
        return null;
    }
}
