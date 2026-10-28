namespace DeskGuard.Interop.Outlook.Sta;

using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Concurrency;

public sealed partial class OutlookStaCoordinator : IDisposable
{
    private readonly Thread _staThread;
    private readonly CancellationTokenSource _cts = new();
    private readonly PriorityJobQueue<StaJobWrapper> _queue = new(starvationLimit: 5);
    private readonly OutlookNamedMutex _globalComLock = new(@"Global\MOS_Outlook_COM_Lock");
    private uint _staNativeThreadId;
    private readonly ManualResetEventSlim _initSignal = new(false);

    private bool _circuitOpen;
    private DateTime _circuitResetTime = DateTime.MinValue;
    private static readonly int[] BackoffIntervals = [50, 150, 500, 1000];

    public event Action<string>? LogMessage;
    public bool HasHighPriorityWork => _queue.HasHighPriorityJobs;

    private sealed class StaJobWrapper
    {
        public required Action<CancellationToken> Work { get; init; }
        public required Action SetResult { get; init; }
        public required Action<Exception> SetException { get; init; }
        public required string Tag { get; init; }
        public required JobPriority Priority { get; init; }
        public CancellationToken CancellationToken { get; init; } = default;
    }

    public OutlookStaCoordinator()
    {
        _staThread = new Thread(StaThreadMain)
        {
            Name = "DeskGuard_Outlook_STA_Pump",
            IsBackground = true
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
        _initSignal.Wait(5000);
    }

    public Task EnqueueAsync(Action<CancellationToken> job, JobPriority priority, string tag, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        StaJobWrapper wrapper = new()
        {
            Work = job,
            SetResult = () => tcs.TrySetResult(true),
            SetException = ex => tcs.TrySetException(ex),
            Tag = tag,
            Priority = priority,
            CancellationToken = cancellationToken
        };

        _queue.Enqueue(wrapper, priority);
        return tcs.Task;
    }

    public Task<T> EnqueueAsync<T>(Func<CancellationToken, T> job, JobPriority priority, string tag, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        T result = default!;
        StaJobWrapper wrapper = new()
        {
            Work = ct => { result = job(ct); },
            SetResult = () => tcs.TrySetResult(result),
            SetException = ex => tcs.TrySetException(ex),
            Tag = tag,
            Priority = priority,
            CancellationToken = cancellationToken
        };

        _queue.Enqueue(wrapper, priority);
        return tcs.Task;
    }

    public static void PumpDelay(int delayMs, CancellationToken ct)
    {
        if (delayMs <= 0) return;
        int elapsed = 0;
        while (elapsed < delayMs && !ct.IsCancellationRequested)
        {
            while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
            {
                if (msg.message == WM_QUIT) return;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
            uint slice = (uint)Math.Min(50, delayMs - elapsed);
            MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, slice, QS_ALLINPUT, MWMO_ALERTABLE);
            elapsed += (int)slice;
        }
    }

    private void StaThreadMain()
    {
        _ = OleInitialize(IntPtr.Zero);
        try
        {
            _staNativeThreadId = GetCurrentThreadId();
            _initSignal.Set();

            while (!_cts.IsCancellationRequested)
            {
                // 1. Pump pending Win32 messages (handles COM callbacks / ItemAdd events)
                while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_QUIT) return;
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                // 2. Execute queued COM work
                if (_queue.TryDequeue(out StaJobWrapper? job) && job != null)
                {
                    ExecuteJobWithRetry(job);
                }
                else
                {
                    // Sleep briefly to yield CPU while remaining responsive to message pump
                    MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 50, QS_ALLINPUT, MWMO_ALERTABLE);
                }
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"[STA Thread] Unhandled exception: {ex}");
        }
        finally
        {
            OleUninitialize();
        }
    }

    public void YieldIfHighPriority(CancellationToken ct = default)
    {
        // 1. Pump messages regardless so COM events (ItemAdd) are delivered
        while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            if (msg.message == WM_QUIT) return;
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // 2. Check if high-priority work is waiting
        if (!_queue.HasHighPriorityJobs)
        {
            return;
        }

        // 3. Temporarily release COM lock to execute high-priority work
        _globalComLock.Release();
        try
        {
            while (_queue.HasHighPriorityJobs && !ct.IsCancellationRequested && !_cts.IsCancellationRequested)
            {
                while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    if (msg.message == WM_QUIT) return;
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                if (_queue.TryDequeueHighPriority(out StaJobWrapper? highPriJob) && highPriJob != null)
                {
                    ExecuteJobWithRetry(highPriJob);
                }
                else
                {
                    break;
                }
            }
        }
        finally
        {
            if (!_globalComLock.Acquire(30000))
            {
                string msg = "WARN: Reacquiring Global\\MOS_Outlook_COM_Lock timed out during yield (30000ms).";
                LogMessage?.Invoke(msg);
                DeskGuard.Core.Logging.AppLogger.Warn(msg);
                throw new TimeoutException("Timed out reacquiring Global\\MOS_Outlook_COM_Lock after yield.");
            }
        }
    }

    private void ExecuteJobWithRetry(StaJobWrapper job)
    {
        if (_circuitOpen)
        {
            if (DateTime.UtcNow < _circuitResetTime)
            {
                // Only throttle low priority background tasks (P2, P3).
                // High-priority operations (P0 real-time arrival, P1 interactive user commands) must be attempted.
                if (job.Priority >= JobPriority.P2)
                {
                    job.SetException(new InvalidOperationException("Circuit Open: Outlook overloaded or unresponsive."));
                    return;
                }
            }
            else
            {
                _circuitOpen = false;
            }
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, job.CancellationToken);
        var effectiveCt = linkedCts.Token;
        if (effectiveCt.IsCancellationRequested)
        {
            job.SetException(new OperationCanceledException(effectiveCt));
            return;
        }

        bool lockTaken = false;
        try
        {
            lockTaken = _globalComLock.Acquire(30000);
            if (!lockTaken)
            {
                string warnMsg = $"WARN: Timed out waiting for Global\\MOS_Outlook_COM_Lock (30000ms) for job '{job.Tag}'";
                LogMessage?.Invoke(warnMsg);
                DeskGuard.Core.Logging.AppLogger.Warn(warnMsg);
                job.SetException(new TimeoutException($"Timed out waiting for Global\\MOS_Outlook_COM_Lock for job '{job.Tag}'"));
                return;
            }

            int retryCount = 0;
            const int maxRetries = 5;

            while (retryCount < maxRetries)
            {
                if (effectiveCt.IsCancellationRequested)
                {
                    job.SetException(new OperationCanceledException(effectiveCt));
                    return;
                }

                try
                {
                    job.Work(effectiveCt);
                    job.SetResult();
                    return;
                }
                catch (OperationCanceledException ex)
                {
                    job.SetException(ex);
                    return;
                }
                catch (COMException ex) when (IsRetryableComException(ex.ErrorCode))
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        break;
                    }

                    int baseDelay = retryCount <= BackoffIntervals.Length ? BackoffIntervals[retryCount - 1] : 1000;
                    int jitter = Random.Shared.Next(10, 50);
                    PumpDelay(baseDelay + jitter, effectiveCt);
                }
                catch (Exception ex)
                {
                    job.SetException(ex);
                    return;
                }
            }

            _circuitOpen = true;
            _circuitResetTime = DateTime.UtcNow.AddSeconds(30);
            string errMsg = "Outlook busy timeout (RPC_E_SERVERCALL_RETRYLATER). Circuit broken for 30s.";
            LogMessage?.Invoke($"ERROR: {errMsg}");
            job.SetException(new InvalidOperationException(errMsg));
        }
        finally
        {
            if (lockTaken)
            {
                _globalComLock.Release();
            }
        }
    }

    private static bool IsRetryableComException(int errorCode)
    {
        // 0x80010001 (RPC_E_CALL_REJECTED = -2147418111)
        // 0x8001010A (RPC_E_SERVERCALL_RETRYLATER = -2147417846)
        // 0x8004012C (MAPI temporary error = -2147220948)
        return errorCode is -2147418111 or -2147417846 or -2147220948;
    }

    private bool _isDisposed;

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try { _cts.Cancel(); } catch { }
        if (_staThread.IsAlive && _staNativeThreadId != 0)
        {
            PostThreadMessage(_staNativeThreadId, WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _staThread.Join(2000);
        }
        _globalComLock.Dispose();
        _initSignal.Dispose();
        _cts.Dispose();
    }

    #region Win32 Interop P/Invoke
    private const uint PM_REMOVE = 0x0001;
    private const uint WM_QUIT = 0x0012;
    private const uint QS_ALLINPUT = 0x04FF;
    private const uint MWMO_ALERTABLE = 0x0002;

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

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint msg, UIntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint nCount, IntPtr pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [LibraryImport("ole32.dll")]
    private static partial int OleInitialize(IntPtr pvReserved);

    [LibraryImport("ole32.dll")]
    private static partial void OleUninitialize();
    #endregion
}
