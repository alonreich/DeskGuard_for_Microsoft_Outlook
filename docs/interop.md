# DeskGuard.Interop.Outlook - COM Interop & STA Message Pump

## 1. Module Overview
`DeskGuard.Interop.Outlook` provides robust, crash-resilient communication with the Microsoft Outlook desktop client via its native COM Object Model and MAPI properties.

It solves Outlook COM's fundamental fragility through:
- **Dedicated STA Thread Isolation**: All COM operations execute on a dedicated message-pump thread with active OLE initialization (`OleInitialize` / `OleUninitialize`).
- **Resilient Multi-Stage COM Acquisition (`ComUtils.CreateOrGetOutlook`)**: Cascading activation strategy avoiding 60-second RPC hangs.
- **Recursive Inter-Process Named Mutex (`OutlookNamedMutex`)**: Thread-safe recursive locking preventing self-deadlocks and multi-process collisions.
- **Safe COM Lifetime (`ComRef<T>`)**: Guaranteed cleanup of COM handles via `Marshal.FinalReleaseComObject` in structural wrappers while safeguarding event sinks.
- **Dynamic Property Access (`OutlookItemAccessor`)**: Resilient retrieval of MAPI properties without throwing exceptions on missing or restricted fields.

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.Interop.Outlook/
|-- Sta/
|   |-- OutlookStaCoordinator.cs     # Dedicated background STA worker thread with message pump & job queue
|   `-- OutlookNamedMutex.cs         # Reentrant inter-process synchronization mutex with recursion tracking
|-- Com/
|   |-- ComRef.cs                    # ComRef<T> struct & ComUtils (multi-tier COM activation & safe release)
|   |-- MapiPropertyTags.cs          # Well-known MAPI DASL property tags (Message-ID, Headers, SCL, Sender)
|   `-- OutlookItemAccessor.cs       # Resilient wrapper for extracting MailItem metadata & attachment hashes
`-- Services/
    |-- OutlookMapiService.cs        # High-level facade orchestrating all Outlook actions & crawler dispatch
    |-- OutlookLiveWatcher.cs        # Real-time event listener hooking Inbox.ItemAdd
    |-- OutlookMailboxCrawler.cs     # Recursive folder traverser for historical sweeps & deduplication
    `-- OutlookActionsService.cs     # Executes folder operations (Quarantine, Release, Move, Delete)
```

---

## 3. Multi-Stage COM Activation (`ComRef.cs`)
Connecting to Microsoft Outlook via COM can easily stall if `OUTLOOK.EXE` is busy, starting up, or running under different session parameters. DeskGuard implements a 3-tier acquisition cascade in `ComUtils.CreateOrGetOutlook()`:

1. **Active Running Instance Check (`GetActiveObject`)**:
   - Resolves CLSID via native `CLSIDFromProgID("Outlook.Application")`.
   - Queries the Running Object Table (ROT) via `oleaut32.dll` `GetActiveObject`.
   - If Outlook is already running, attaches instantly without spawning new processes or triggering RPC delay.
2. **Managed Runtime Activation (`Activator.CreateInstance`)**:
   - Queries `Type.GetTypeFromProgID("Outlook.Application")`.
   - Instantiates via managed COM activation with performance timing diagnostics.
3. **Native CoCreateInstance Fallback (`CoCreateInstance`)**:
   - Invokes native `ole32.dll` `CoCreateInstance` requesting `CLSCTX_LOCAL_SERVER` with `IID_IUnknown`.
   - Marshals raw interface pointer via `Marshal.GetObjectForIUnknown`.

Every step records exact HRESULT codes, elapsed milliseconds, and exception messages to `AppLogger` (`%LOCALAPPDATA%\DeskGuard\logs\deskguard.log`).

---

## 4. The STA Threading Model (`OutlookStaCoordinator.cs`)
Outlook uses an OLE Single-Threaded Apartment (STA). Accessing COM interface pointers from thread pool workers without standard OLE marshalling causes `RPC_E_WRONG_THREAD` (`0x8001010E`) or silent data corruption.

### Architecture:
```
[Application / TaskPool] 
       |
       v EnqueueAsync(job, priority, tag)
[PriorityJobQueue]
       |
       v (Signal)
[OutlookStaCoordinator (Dedicated STA Thread)]
       | Runs Windows Message Loop (PeekMessage/DispatchMessage)
       | OLE Initialized (OleInitialize / OleUninitialize)
       | Reentrant Lock Acquisition (_globalComLock.Acquire(30000))
       v Dispatches job.Work()
[Microsoft Outlook COM Objects (MAPI)]
```

### Critical Concurrency & Mutex Rules:
- **OLE Apartment Lifecycle**: The coordinator thread calls `OleInitialize` immediately upon starting and `OleUninitialize` upon thread termination.
- **Recursive Named Mutex (`OutlookNamedMutex`)**: Tracks `_lockCount` to support reentrancy. Nested calls on the same thread increment the lock count without blocking; the underlying OS mutex is released only when `_lockCount` reaches 0.
- **Extended Lock Timeout (30 Seconds)**: Lock acquisition timeout is set to 30,000ms (`_globalComLock.Acquire(30000)`) to allow intensive folder traversal and bulk operations to complete without false-positive timeouts.
- **Yielding to High-Priority Work**: Long-running loops (sweeps, deduplication) call `YieldIfHighPriority(ct)`, temporarily relinquishing the COM lock so real-time incoming email arrivals (`P0`) or interactive user commands (`P1`) execute immediately.
- **Scoped Circuit Breaker**: If COM calls repeatedly fail with busy/retry errors (`0x8001010A`), the circuit breaker trips for background tasks (`P2`/`P3`) only, preserving critical live email monitoring (`P0`) and user remediation (`P1`).

---

## 5. Process Integrity & UAC Constraint
Windows User Interface Privilege Isolation (UIPI) forbids standard-integrity applications (such as a standard user running `OUTLOOK.EXE`) from accepting COM LPC/RPC automation from elevated (administrator) processes.
- DeskGuard must run at **Medium Integrity**.
- If launched with elevated privileges (e.g. following an Inno Setup installation), `Program.cs` automatically detects elevation and re-launches at Medium Integrity via Windows Explorer before initializing COM interop.
- Watchdog retry loops in `Program.cs` implement a **15-second backoff** upon COM failure to prevent CPU spinning when Outlook is shutting down or updating.

---

## 6. Real-Time Inbox Watching (`OutlookLiveWatcher.cs`)
- Connects to Outlook's default Inbox (`GetDefaultFolder(olFolderInbox)`).
- Retains root folder references in `_watchedFolders` for the lifespan of the watcher to anchor COM event sinks and prevent table detachment.
- Subscribes to `Items.ItemAdd`, extracts `EntryID` and `StoreID`, and immediately releases temporary RCWs in `finally` blocks.
- **On-Access Scope Restriction**: In live arrival mode (`isLiveArrival`), real-time scoring is applied strictly to unread messages (`mailItem.UnRead`), avoiding redundant re-evaluations of existing read items.
- **Continuous Historical Crawl**: Startup executes a fast priority sweep followed by a continuous background crawler (`RunCrawlerAsync`) traversing older messages down to `DateTime.MinValue`, preventing scans from stalling at intermediate batch thresholds (e.g., at 525 emails).
- Automatically handles Outlook restarts: detects process exit and re-binds as soon as Outlook re-launches.

---

## 7. Local Antivirus Scanning & Progressive Mailbox Sweep (`OutlookMapiService.cs`)

### 7.1 On-Access Priority Ingestion (P0)
When an incoming email arrives:
- If `DeepLocalAvScanAll` is enabled or the heuristic score is below `Thresholds.SuspiciousThresholdPercent`, the message is disassembled and scanned via `LocalAntivirusService` using Microsoft Defender (`MpCmdRun.exe`) and ClamAV (`clamscan.exe`).
- Attachments are scanned individually, isolated from the message body and headers.
- If a threat is detected, the email is immediately rerouted to the Quarantine Threats folder (`OutlookActionsService.GetQuarantineSubfolder`).
- The scan verdict is stamped in the local SQLite vault (`RecordAvScanResult`).

### 7.2 Progressive Historical AV Sweep (P3)
When `DeepLocalAvScanAll` is activated:
- The coordinator initiates `RunDeepAvBackgroundScanAsync` at `JobPriority.P3` (lowest priority).
- Evaluates batches of unscanned historical emails (`GetUnscannedAvEmails`), scanning headers, body, and extracted attachments.
- Between email items, the worker calls `_coordinator.YieldIfHighPriority(ct)`, ensuring that live incoming emails (`P0`) and user remediation actions (`P1`) immediately preempt background scanning.
- Each scanned email is tagged `av_scanned = 1` in SQLite, ensuring no email is ever scanned more than once.

---

## 8. Window Focus & Foreground Activation (`DashboardWindow.cs`)
When opening an email in Microsoft Outlook:
- Uses `AllowSetForegroundWindow(-1)` via Win32 interop prior to invoking `mailItem.Display()`.
- Guarantees the Outlook inspector window pops up focused directly in the foreground above the DeskGuard dashboard.
