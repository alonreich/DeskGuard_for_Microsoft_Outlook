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
        if (hr < 0) return null;

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
        catch
        {
            return null;
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "Outlook.Application COM activation via ProgID")]
    public static object? CreateOrGetOutlook()
    {
        object? active = GetActiveObject("Outlook.Application");
        if (active != null) return active;

        int hr = CLSIDFromProgID("Outlook.Application", out Guid clsid);
        if (hr >= 0)
        {
            try
            {
                Guid iid = IidIUnknown;
                unsafe
                {
                    hr = CoCreateInstance(&clsid, IntPtr.Zero, CLSCTX_LOCAL_SERVER, &iid, out IntPtr pUnk);
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
            catch { }
        }

        try
        {
            Type? olType = Type.GetTypeFromProgID("Outlook.Application");
            if (olType != null)
            {
                return Activator.CreateInstance(olType);
            }
        }
        catch { }

        return null;
    }
}
