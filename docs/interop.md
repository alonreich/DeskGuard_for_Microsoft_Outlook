# DeskGuard.Interop.Outlook - COM Interop & STA Message Pump

## 1. Module Overview
`DeskGuard.Interop.Outlook` provides robust, crash-resilient communication with the Microsoft Outlook desktop client via its native COM Object Model and MAPI properties.

It solves Outlook COM's fundamental fragility through:
- **Dedicated STA Thread Isolation**: All COM operations execute on a dedicated message-pump thread.
- **Safe COM Lifetime (`ComRef<T>`)**: Guaranteed calls to `Marshal.ReleaseComObject` to prevent orphaned Outlook process locks.
- **Dynamic Property Access (`OutlookItemAccessor`)**: Safely retrieves MAPI properties without throwing exceptions on missing or restricted fields.

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.Interop.Outlook/
|-- Sta/
|   |-- OutlookStaCoordinator.cs     # Dedicated background STA worker thread with message pump
|   `-- OutlookNamedMutex.cs         # Inter-process synchronization mutex
|-- Com/
|   |-- ComRef.cs                    # IDisposable wrapper ensuring Marshal.ReleaseComObject
|   |-- MapiPropertyTags.cs          # Well-known MAPI DASL property tags (Message-ID, Headers)
|   `-- OutlookItemAccessor.cs       # Resilient wrapper for extracting MailItem metadata
`-- Services/
    |-- OutlookMapiService.cs        # High-level facade orchestrating all Outlook actions
    |-- OutlookLiveWatcher.cs        # Real-time event listener hooking Inbox.ItemAdd
    |-- OutlookMailboxCrawler.cs     # Recursive folder traverser for historical sweeps & dedupe
    `-- OutlookActionsService.cs     # Executes folder operations (Quarantine, Move, Delete)
```

---

## 3. The STA Threading Model (`OutlookStaCoordinator.cs`)
Outlook uses an OLE Single-Threaded Apartment (STA). If a COM interface pointer created on an STA thread is accessed by a thread pool thread without standard OLE marshalling, the call fails with `RPC_E_WRONG_THREAD` (`0x8001010E`).

### Architecture:
```
[Application / TaskPool] 
       |
       v EnqueueAsync(job, priority, tag)
[PriorityJobQueue]
       |
       v (Signal)
[OutlookStaCoordinator (Dedicated Thread)]
       | Runs Windows Message Loop (Application.Run)
       v Dispatches job.Work()
[Microsoft Outlook COM Objects]
```

### Best Practices for AI Agents:
- Never call Outlook COM objects directly from an async continuation or UI event handler.
- Always use `_coordinator.EnqueueAsync(...)` or call through `OutlookMapiService`, passing caller `CancellationToken`.
- Background batch scans (crawler, sweeps, deduplication) must periodically call `_coordinator.YieldIfHighPriority(ct)` to allow P0/P1 user operations and live email alerts to preempt long-running loops.
- Wrap temporary COM objects in `using ComRef<T>` or ensure explicit `Marshal.ReleaseComObject` in `finally` blocks, but never invoke `FinalReleaseComObject` on event sink arguments (e.g. `ItemAdd` parameters).

---

## 4. Real-Time Inbox Watching (`OutlookLiveWatcher.cs`)
- Connects to Outlook's `GetDefaultFolder(olFolderInbox)`.
- Retains parent `MAPIFolder` references in `_watchedFolders` for the entire watcher lifetime to anchor the COM event sinks and prevent GC or table detachment.
- Subscribes to the `Items.ItemAdd` event without releasing the incoming event COM argument.
- When fired, checks if the item is a `MailItem`, extracts its `EntryID` and headers, and triggers asynchronous heuristic scoring without blocking Outlook's message handling.
- Gracefully handles Outlook restarts: if the `OUTLOOK.EXE` process exits, the watcher detects disconnection and automatically re-binds when Outlook restarts.
