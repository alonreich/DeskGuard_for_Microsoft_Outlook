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
|  |  (WinForms Notify) |         | (Form / Message Pump) |     |  Modern UI /   |  |
|  +--------------------+         +-----------------------+     |  Vanilla JS    |  |
|            ^                                ^                 +----------------+  |
|            |                                | (JSON-RPC Bridge)                   |
|            v                                v                                     |
|  +------------------------------------------------------+                         |
|  |             Application Coordinator                  |                         |
|  |        (Single Instance Mutex, Config)               |                         |
|  +------------------------------------------------------+                         |
|            |                                |                                     |
|            v                                v                                     |
|  +--------------------+         +-----------------------+                         |
|  |   Scoring Engine   |         |   Vault Repository    |                         |
|  |  - Heuristics      |         |  - SQLite (WAL Mode)  |                         |
|  |  - SafeLinks       |         |  - Schema Migrations  |                         |
|  |  - DNS Threat DB   |         |  - Deduplication Index|                         |
|  +--------------------+         +-----------------------+                         |
|            ^                                                                      |
|            | (Dispatched Tasks)                                                   |
|  +------------------------------------------------------+                         |
|  |             OutlookStaCoordinator (STA Thread)       |                         |
|  |  - Dedicated Windows Message Pump                    |                         |
|  |  - Safe COM Reference Lifetime (ComRef)              |                         |
|  |  - Named Cross-Process Mutex                         |                         |
|  +------------------------------------------------------+                         |
|                            | (MAPI COM Interop)                                   |
+----------------------------|------------------------------------------------------+
                             v
               +---------------------------+
               |  Microsoft Outlook (MAPI) |
               |     OUTLOOK.EXE Client    |
               +---------------------------+
```

---

## 3. Core Architectural Principles

### 3.1 Single-Threaded Apartment (STA) Isolation
Microsoft Outlook's COM Object Model is strictly apartment-threaded. Invoking Outlook MAPI methods from arbitrary thread pool threads causes RPC marshalling errors, COM deadlocks, and unexplained Outlook crashes.

- **Solution**: `OutlookStaCoordinator` maintains a dedicated background STA thread with an active Windows message pump (`Application.Run`).
- All COM operations (item reads, folder moves, event attachments) are dispatched through this coordinator using a prioritized work queue (`PriorityJobQueue`).

### 3.2 Self-Contained Standalone Single-File Execution
DeskGuard packages directly into a standalone self-contained x64 single-file executable (`<PublishSingleFile>true</PublishSingleFile>`, `<SelfContained>true</SelfContained>`).
- **Full COM & WebView2 Interop**: Includes built-in COM interop support (`BuiltInComInteropSupport=true`), ensuring seamless marshalling for Microsoft Edge WebView2 and Outlook MAPI.
- **STA Message Pumping**: Uses an active message pump during asynchronous initialization (`WaitForTaskWithPump`) to eliminate thread mode change errors (`RPC_E_CHANGED_MODE`).
- **No External Dependencies**: Ships with its own optimized runtime, requiring no .NET installation on the host machine.

### 3.3 Zero-Impact Asynchronous Pipeline
- Live incoming email events (`ItemAdd`) are captured immediately, extracted into immutable POCO records (`EmailRecord`), and handed off to background scoring tasks.
- The user's Outlook UI is never blocked while threat scoring or DNS lookups are underway.

### 3.4 Data Persistence & Concurrency
- Uses an embedded SQLite database configured in **WAL (Write-Ahead Logging)** mode with `synchronous=NORMAL`.
- Guarantees concurrent reads while the crawler or live watcher writes new email fingerprints.

---

## 4. Key Cross-Module Data Flows

1. **Live Email Ingestion**:
   - `OutlookLiveWatcher` catches `ItemAdd` on default Inbox.
   - Dispatches extraction job to `OutlookStaCoordinator`.
   - `OutlookItemAccessor` extracts subject, sender, headers, HTML body, attachments.
   - `ScoringEngine` processes text heuristics, unwraps SafeLinks, and checks malware hashes.
   - Result saved to `VaultRepository` (SQLite).
   - Event posted to `DashboardWindow` via WebView2 bridge and tray balloon notification displayed.

2. **User Remediation (Quarantine / Delete / Release)**:
   - User triggers action in Dashboard Web UI.
   - UI calls `window.chrome.webview.postMessage({ action: 'quarantine', ... })`.
   - `DashboardWindow` routes command to `OutlookMapiService`.
   - `OutlookActionsService` moves item via STA coordinator to Target Folder (e.g., Quarantine / Junk).
   - Local vault status updated and confirmation broadcast back to the frontend.
