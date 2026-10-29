# DeskGuard Architecture & Design Overview

## 1. System Vision
DeskGuard for Microsoft Outlook is a high-performance Windows tray application engineered to provide real-time heuristic security scoring, phishing interception, threat intelligence verification, and mailbox deduplication for Microsoft Outlook desktop clients.

It is built as a **.NET 9 / C# 13 Self-Contained Standalone** application, requiring zero installed .NET runtime, zero administrative rights for standard operation, and minimal system resource footprint.

---

## 2. Component Topology

```
+-----------------------------------------------------------------------------------+
|                                 DeskGuard Process                                 |
|                                                                                   |
|  +--------------------+         +-----------------------+     +----------------+  |
|  |   System Tray UI   | <-----> |   Dashboard Window    | --> |    WebView2    |  |
|  |   (Native Win32    |         | (Native Win32 Window /|     |  Modern UI /   |  |
|  | Message-Only HWND) |         |     Message Pump)     |     |  Vanilla JS    |  |
|  +--------------------+         +-----------------------+     +----------------+  |
|            ^                                ^                                     |
|            |                                | (JSON-RPC Bridge)                   |
|            v                                v                                     |
|  +------------------------------------------------------+                         |
|  |             Application Coordinator                  |                         |
|  |     (Single Instance Mutex, Config, Integrity Check) |                         |
|  +------------------------------------------------------+                         |
|            |                                |                                     |
|            v                                v                                     |
|  +--------------------+         +-----------------------+                         |
|  |   Scoring Engine   |         |   Vault Repository    |                         |
|  |  - 16 Rule Deduct  |         |  - SQLite (WAL Mode)  |                         |
|  |  - Local AV Disasm |         |  - Schema Migrations  |                         |
|  |  - Defender/ClamAV |         |  - FTS5 Query Engine  |                         |
|  |  - SafeLinks Unpack|         |  - Deduplication Index|                         |
|  |  - Team Cymru DNS  |         |                       |                         |
|  +--------------------+         +-----------------------+                         |
|            ^                                                                      |
|            | (Dispatched Tasks)                                                   |
|  +------------------------------------------------------+                         |
|  |             OutlookStaCoordinator (STA Thread)       |                         |
|  |  - Dedicated STA Message Pump (OleInitialize)        |                         |
|  |  - Cascading COM Activation (ComUtils)               |                         |
|  |  - Reentrant Named Mutex (OutlookNamedMutex, 30s)    |                         |
|  +------------------------------------------------------+                         |
|                            | (MAPI COM Interop)                                   |
|                            v                                                      |
|              +---------------------------+                                        |
|              |  Microsoft Outlook (MAPI) |                                        |
|              |     OUTLOOK.EXE Client    |                                        |
|              +---------------------------+                                        |
+-----------------------------------------------------------------------------------+
```

---

## 3. Core Architectural Principles

### 3.1 Single-Threaded Apartment (STA) Isolation
Microsoft Outlook's COM Object Model is strictly apartment-threaded. Invoking Outlook MAPI methods from arbitrary thread pool threads causes RPC marshalling errors (`RPC_E_WRONG_THREAD`), COM deadlocks, and unexplained Outlook crashes.

- **Solution**: `OutlookStaCoordinator` maintains a dedicated background STA thread with active OLE initialization (`OleInitialize` / `OleUninitialize`) and a native message pump.
- All COM operations (item reads, folder moves, event attachments) are dispatched through this coordinator using a prioritized work queue (`PriorityJobQueue`).
- A recursive named cross-process mutex (`OutlookNamedMutex`) with a 30-second timeout ensures safe synchronization without self-deadlocks.

### 3.2 Process Integrity Level (UIPI) Architecture
Windows User Interface Privilege Isolation (UIPI) forbids standard-integrity desktop applications (`OUTLOOK.EXE`) from accepting COM LPC/RPC automation from elevated (administrator) processes.
- If DeskGuard is launched with administrative privileges (e.g. from an installer), it detects elevation via `WindowsIdentity` on startup.
- It immediately delegates launch to the interactive Windows Explorer shell session (`explorer.exe`) at standard Medium Integrity and cleanly exits the elevated instance.

### 3.3 Self-Contained Standalone Single-File Execution
DeskGuard packages directly into a standalone self-contained x64 single-file executable (`<PublishSingleFile>true</PublishSingleFile>`, `<SelfContained>true</SelfContained>`).
- **Full COM & WebView2 Interop**: Includes built-in COM interop support (`BuiltInComInteropSupport=true`), ensuring seamless marshalling for Microsoft Edge WebView2 and Outlook MAPI.
- **STA Message Pumping**: Uses an active message pump during asynchronous initialization (`WaitForTaskWithPump`) to eliminate thread mode change errors (`RPC_E_CHANGED_MODE`).
- **Zero External Dependencies**: Ships with its own runtime and native libraries, requiring no .NET SDK or runtime installation on the host machine.

### 3.4 Zero-Impact Asynchronous Pipeline
- Live incoming email events (`ItemAdd`) are captured immediately, extracted into immutable POCO records (`EmailRecord`), and handed off to background scoring tasks.
- Long-running sweeps and deduplication jobs yield to high-priority live incoming arrivals (`P0`) and user actions (`P1`).
- The user's Outlook client is never frozen while threat scoring or DNS lookups are underway.

### 3.5 Data Persistence & Concurrency
- Uses an embedded SQLite database configured in **WAL (Write-Ahead Logging)** mode with `synchronous=NORMAL`.
- Maintains dedicated read and write connection instances to guarantee concurrent read queries during background crawls without `SQLITE_BUSY` contention.

### 3.6 Multi-Stage Local Antivirus Disassembly Pipeline
- **On-Access Realtime Mode (`P0`)**: Newly arriving unread emails requiring deep inspection are disassembled into sandboxed files (headers, body, individual attachments) and scanned via Microsoft Defender and ClamAV. Threat detections automatically route the message into Quarantine.
- **Progressive Background Crawler (`P3`)**: When deep AV inspection is enabled across all mail, historical messages are progressively scanned in batches at lowest priority (`P3`), yielding (`YieldIfHighPriority`) to incoming mail and user operations.
- **Persistent Scan State**: Each completed scan updates `av_scanned = 1` in SQLite, ensuring that upgrading installations or repeating crawls never re-scan previously evaluated items.

---

## 4. Key Cross-Module Data Flows

1. **Live Email Ingestion**:
   - `OutlookLiveWatcher` catches `ItemAdd` on default Inbox.
   - Dispatches extraction job to `OutlookStaCoordinator`.
   - `OutlookItemAccessor` extracts subject, sender, headers, HTML body, attachments.
   - `ScoringEngine` computes safety score starting from 100.0% minus deductions across 16 detection engines.
   - Result saved to `VaultRepository` (SQLite).
   - Event posted to `DashboardWindow` via WebView2 bridge (`outlook-scan-update`) and tray balloon notification displayed if flagged.

2. **User Remediation (Quarantine / Delete / Release)**:
   - User triggers action in Dashboard Web UI.
   - UI calls `window.securityApi.quarantineEmail({ entryId, storeId })`.
   - `DashboardWindow` routes command to `OutlookMapiService`.
   - `OutlookActionsService` moves item via STA coordinator to Target Folder (DeskGuard Quarantine or Junk Email).
   - Local vault status updated and confirmation broadcast back to the frontend.

3. **Mailbox Deduplication**:
   - User or tray triggers duplicate audit across all stores and folders.
   - `OutlookMailboxCrawler` traverses mail folders via STA coordinator, streaming progress events.
   - Emails clustered by composite SHA-256 fingerprint with folder-hierarchy-based survivor election (`Inbox` [Rank 1] > `Sent` [Rank 2] > user folders/archive [Rank 3/4] > `Junk` [Rank 10] > `Conflicts` [Rank 20] > `Deleted Items` [Rank 99]; tie-breaker: oldest `SentUtc`).
   - Results persisted in SQLite and surfaced in `ui_duplicates.js` with visual cluster cards, direct Outlook navigation, and a locked sole survivor protection banner ensuring surviving copies cannot be accidentally checked or deleted during batch actions.

4. **Multi-Piece Local Antivirus Disassembly & Inspection**:
   - Triggered on-access for incoming mail (`P0`), on-demand via dashboard/forensic action (`P1`), or during progressive background crawl (`P3`).
   - `LocalAntivirusService` partitions the email into message headers, body HTML, and individually extracted attachments in a sandboxed session folder.
   - Pieces are scanned sequentially by Microsoft Defender (`MpCmdRun.exe`) and ClamAV (`clamscan.exe`), streaming real-time stage progress (`av-scan-progress`).
   - Final verdict and detected threats are recorded in SQLite via `RecordAvScanResult`. If malicious, quarantine relocation is executed.
