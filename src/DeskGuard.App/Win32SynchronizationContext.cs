namespace DeskGuard.App;

using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;

internal sealed class Win32SynchronizationContext : SynchronizationContext
{
    private readonly IntPtr _hwnd;
    private readonly uint _msg;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly int _ownerThreadId;

    public Win32SynchronizationContext(IntPtr hwnd, uint msg = 0x0400 + 102)
    {
        _hwnd = hwnd;
        _msg = msg;
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue(() =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Win32SyncContext] Exception during posted callback: {ex}");
            }
        });

        if (_hwnd != IntPtr.Zero)
        {
            TrayIconController.PostMessageW(_hwnd, _msg, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Environment.CurrentManagedThreadId == _ownerThreadId)
        {
            d(state);
            return;
        }

        using var mres = new ManualResetEventSlim(false);
        Exception? error = null;

        _queue.Enqueue(() =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                mres.Set();
            }
        });

        if (_hwnd != IntPtr.Zero)
        {
            TrayIconController.PostMessageW(_hwnd, _msg, UIntPtr.Zero, IntPtr.Zero);
        }

        mres.Wait();
        if (error != null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    public void DrainQueue()
    {
        while (_queue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Win32SyncContext] Exception in DrainQueue: {ex}");
            }
        }
    }
}
