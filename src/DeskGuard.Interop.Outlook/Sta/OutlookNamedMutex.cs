namespace DeskGuard.Interop.Outlook.Sta;

using System;
using System.Threading;
using DeskGuard.Core.Logging;

public sealed class OutlookNamedMutex : IDisposable
{
    private Mutex? _mutex;
    private int _lockCount;
    private readonly string _name;

    public OutlookNamedMutex(string name = @"Global\MOS_Outlook_COM_Lock")
    {
        _name = name;
        try
        {
            _mutex = new Mutex(false, _name);
        }
        catch
        {
            try
            {
                _mutex = Mutex.OpenExisting(_name);
            }
            catch
            {
                // Fallback to Local named mutex if Global namespace is restricted
                string localName = @"Local\MOS_Outlook_COM_Lock";
                try
                {
                    _mutex = new Mutex(false, localName);
                    _name = localName;
                }
                catch
                {
                    _mutex = null;
                }
            }
        }
    }

    public bool Acquire(int timeoutMs = 30000)
    {
        if (_mutex == null) return true;
        try
        {
            if (_lockCount > 0)
            {
                _lockCount++;
                return true;
            }

            if (_mutex.WaitOne(timeoutMs))
            {
                _lockCount = 1;
                return true;
            }
            AppLogger.Warn($"[OutlookNamedMutex] Timed out after {timeoutMs}ms waiting for {_name}");
            return false;
        }
        catch (AbandonedMutexException)
        {
            AppLogger.Warn($"[OutlookNamedMutex] Acquired abandoned mutex: {_name}");
            _lockCount = 1;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[OutlookNamedMutex] Exception acquiring {_name}", ex);
            return false;
        }
    }

    public void Release()
    {
        if (_mutex != null && _lockCount > 0)
        {
            _lockCount--;
            if (_lockCount == 0)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"[OutlookNamedMutex] ReleaseMutex exception: {ex.Message}");
                }
            }
        }
    }

    public void Dispose()
    {
        while (_lockCount > 0)
        {
            Release();
        }
        _mutex?.Dispose();
        _mutex = null;
    }
}
