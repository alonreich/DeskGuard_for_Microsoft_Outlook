namespace DeskGuard.Core.Concurrency;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public sealed class PriorityJobQueue<T>
{
    private readonly int _starvationLimit;
    private int _consecutiveHighPriorityRuns;
    private readonly Queue<T>[] _queues = new Queue<T>[4];
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _signal = new(0);

    public PriorityJobQueue(int starvationLimit = 5)
    {
        _starvationLimit = starvationLimit;
        for (int i = 0; i < 4; i++)
        {
            _queues[i] = new Queue<T>();
        }
    }

    public void Enqueue(T item, JobPriority priority)
    {
        lock (_syncRoot)
        {
            _queues[(int)priority].Enqueue(item);
        }
        _signal.Release();
    }

    public async Task<T> DequeueAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_syncRoot)
            {
                if (TryDequeueInternal(out T item))
                {
                    return item;
                }
            }
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (_syncRoot)
        {
            if (TryDequeueInternal(out item))
            {
                // Decrement semaphore count if there is a slot
                _ = _signal.Wait(0);
                return true;
            }
            return false;
        }
    }

    public bool TryDequeueHighPriority(out T item)
    {
        lock (_syncRoot)
        {
            for (int p = 0; p <= (int)JobPriority.P1; p++)
            {
                if (_queues[p].Count > 0)
                {
                    _consecutiveHighPriorityRuns++;
                    item = _queues[p].Dequeue();
                    _ = _signal.Wait(0);
                    return true;
                }
            }
            item = default!;
            return false;
        }
    }

    private bool TryDequeueInternal(out T item)
    {
        bool hasLow = _queues[(int)JobPriority.P2].Count > 0 || _queues[(int)JobPriority.P3].Count > 0;
        bool hasHigh = _queues[(int)JobPriority.P0].Count > 0 || _queues[(int)JobPriority.P1].Count > 0;

        // Anti-starvation check
        if (hasLow && hasHigh && _consecutiveHighPriorityRuns >= _starvationLimit)
        {
            _consecutiveHighPriorityRuns = 0;
            if (_queues[(int)JobPriority.P2].Count > 0)
            {
                item = _queues[(int)JobPriority.P2].Dequeue();
                return true;
            }
            if (_queues[(int)JobPriority.P3].Count > 0)
            {
                item = _queues[(int)JobPriority.P3].Dequeue();
                return true;
            }
        }

        // Standard priority hierarchy (P0 -> P1 -> P2 -> P3)
        for (int p = 0; p < 4; p++)
        {
            if (_queues[p].Count > 0)
            {
                if (p <= (int)JobPriority.P1)
                {
                    _consecutiveHighPriorityRuns++;
                }
                else
                {
                    _consecutiveHighPriorityRuns = 0;
                }

                item = _queues[p].Dequeue();
                return true;
            }
        }

        item = default!;
        return false;
    }

    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                int total = 0;
                for (int i = 0; i < 4; i++) total += _queues[i].Count;
                return total;
            }
        }
    }

    public bool HasHighPriorityJobs
    {
        get
        {
            lock (_syncRoot)
            {
                return _queues[(int)JobPriority.P0].Count > 0 || _queues[(int)JobPriority.P1].Count > 0;
            }
        }
    }

    public void Clear()
    {
        lock (_syncRoot)
        {
            for (int i = 0; i < 4; i++) _queues[i].Clear();
            _consecutiveHighPriorityRuns = 0;
            while (_signal.Wait(0)) { }
        }
    }
}
