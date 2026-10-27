namespace DeskGuard.Interop.Outlook.Sta;

using System;
using System.Threading;

public sealed class OutlookNamedMutex : IDisposable
{
    private Mutex? _mutex;
    private bool _hasLock;
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
            _hasLock = _mutex.WaitOne(timeoutMs);
            return _hasLock;
        }
        catch (AbandonedMutexException)
        {
            _hasLock = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Release()
    {
        if (_hasLock && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch { }
            finally
            {
                _hasLock = false;
            }
        }
    }

    public void Dispose()
    {
        Release();
        _mutex?.Dispose();
        _mutex = null;
    }
}
