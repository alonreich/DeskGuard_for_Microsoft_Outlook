# DeskGuard for Microsoft Outlook - Changelog & Change Staging

All notable changes to DeskGuard are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **CRITICAL RULE FOR AI AGENTS & CONTRIBUTORS:**
> Every proposed or executed change MUST be staged under the unreleased section below.
> When a new public build is triggered (`Build.cmd`), this section is promoted to the
> new version header with a timestamp. AI agents must NEVER complete a task without
> updating this file.

---

## [Unreleased]

### Added
- None.

### Changed
- None.

### Fixed
- None.

---

## [1.0.4] - 2026-10-27

### Added
- None.

### Changed
- Reduced the font and heading size of the direct download link in `README.md` from H1 to H3 for clean, proportionate visual hierarchy.

### Fixed
- Fixed startup white blank page freeze (`RPC_E_CHANGED_MODE / 0x80010106`) during WebView2 initialization by refactoring `Program.Main` from `async Task<int>` to synchronous `[STAThread] static int Main(string[] args)`, ensuring Roslyn assigns `STAThreadAttribute` directly to the CLR entry point so the main UI thread enters Single-Threaded Apartment (STA) mode.
- Converted `DashboardWindow.InitializeAsync()` to synchronous `public void Initialize()`, properly executing WebView2 environment and controller creation on the primary STA thread with message pumping.
---

## [1.0.3] - 2026-10-27

### Added
- Custom `Win32SynchronizationContext` installed on the Win32 UI STA message loop to marshal asynchronous IPC bridge continuations back to the UI thread, ensuring thread affinity for WebView2 COM calls.
- High-priority cooperative preemption in `OutlookStaCoordinator` (`YieldIfHighPriority`) and `PriorityJobQueue.TryDequeueHighPriority` allowing user actions and live email scans (P0/P1) to preempt background crawler and duplicate audits (P2/P3).
- Comprehensive diagnostic logging in `DashboardWindow.InitializeAsync` recording base directory, user data path, thread apartment state, and exact exception details to `%LocalAppData%\DeskGuard\webview2.log`.
- Automated public release publishing in `Build.cmd` (`DeskGuard.Build`) via GitHub CLI, automatically tagging, pushing, and publishing the `DeskGuard.exe` release asset to Git and GitHub Releases.

### Changed
- Streamlined `VaultRepository.GetAllCategorizedEmails` default limit from 2000 to 100 items per category to eliminate unbounded JSON payloads and IPC bridge serialization stalls.
- Optimized attachment hashing in `OutlookItemAccessor.GetAttachmentDetails` to stream directly via `FileStream` with `SHA256.HashData(Stream)` and `MD5.HashData(Stream)`, eliminating 50MB Large Object Heap (LOH) buffering.
- Linked caller `CancellationToken` in `OutlookStaCoordinator.EnqueueAsync` across all `OutlookMapiService` methods (`InitializeAsync`, `RunSweepAsync`, `RunCrawlerAsync`, `ScanDuplicatesAsync`, `CleanDuplicatesAsync`, `DeleteEmailsAsync`, `ReleaseEmailAsync`, `QuarantineEmailAsync`, `OpenInOutlookAsync`, `CheckExistenceAsync`).
- Offloaded synchronous SQLite vault read queries (`get-stats`, `search-vault`, `get-events`, `get-forensics`, `get-sender-suggestions`, `get-saved-duplicates`) in `DashboardWindow.HandleIpcMessageAsync` to thread pool workers via `Task.Run` to prevent freezing the Win32 UI message pump.
- Redesigned `README.md` to feature a crystal-clear, prominent direct download link for end users without clutter or technical jargon.

### Fixed
- Fixed white screen freeze on application launch (`RPC_E_CHANGED_MODE / 0x80010106`) by explicitly calling `OleInitialize` at the entry point of `Program.Main` prior to any Windows Shell or COM invocation.
- Fixed COM lifetime bug in `OutlookLiveWatcher` by retaining `MAPIFolder` references in `_watchedFolders` for the watcher's lifespan and eliminating destructive `Marshal.FinalReleaseComObject` calls on Outlook `ItemAdd` event arguments.

---

## [1.0.2] - 2026-10-27

### Fixed
- Fixed blank white window on application launch by pumping Windows messages during WebView2 asynchronous initialization on the STA thread (`WaitForTaskWithPump`).
- Resolved COM marshalling crash (`System.NotSupportedException: COM Interop requires ComWrapper instance registered for marshalling`) by transitioning packaging to self-contained single-file CoreCLR runtime with full built-in COM interop.
- Added comprehensive fallback path resolution and diagnostic logging (`%LocalAppData%\DeskGuard\webview2.log`) for WebView2 environment, controller, and `wwwroot` virtual host mapping.

---

## [1.0.1] - 2026-10-27

### Added
- Prominent direct download badge and release link for `DeskGuard.exe` at the top and quick start sections of `README.md`.
- Token-saving documentation architecture with `project_structure.txt` router and modular domain docs in `docs/`.
- Repository `.gitignore` tailored for .NET 9 Native AOT, Inno Setup, SQLite WAL, and Windows build artifacts.
- Human-centric condensed `README.md` highlighting real-time threat detection, deduplication, and zero-runtime Native AOT architecture.

### Security
- Verified zero credential/secret leakage across all source files and configuration templates.
- Enforced strict exclusion of local build outputs, SQLite databases, and user runtime state from version control.

---

## [1.0.0] - 2026-10-27

### Added
- **Native AOT Desktop Core**: Full .NET 9 / C# 13 Native AOT standalone executable with zero runtime dependencies.
- **Real-Time Inbox Sentinel**: Outlook MAPI COM `ItemAdd` event listener running on a dedicated STA thread message pump (`OutlookStaCoordinator`).
- **Multi-Vector Phishing & Threat Scoring Engine**:
  - Urgent call-to-action heuristic penalties.
  - Microsoft SafeLinks URL unwrapper (`SafeLinksDecoder`).
  - Homograph and spoofed domain detection (`RegisteredDomainParser` + `StringDistance`).
  - Executable/macro attachment flags.
- **Hybrid Threat Intelligence**:
  - Zero-API Level 1 DNS malware hash lookups via Team Cymru Malware Hash Database.
  - Optional Level 2 VirusTotal v3 REST API integration.
- **Mailbox Deduplication & Cleanup**:
  - SHA-256 body/attachment fingerprinting.
  - Safe duplicate deletion and folder reconciliation.
- **Embedded Modern UI**:
  - Windows Forms tray presence with status tooltips and desktop balloon notifications.
  - WebView2 local HTML5/CSS3/JS dashboard connected via asynchronous JSON-RPC bridge.
- **Persistence & Migration Engine**:
  - High-performance SQLite database operating in Write-Ahead Logging (WAL) mode.
  - Automated incremental schema migration manager.
- **Automated Packaging Pipeline**:
  - Custom C# build orchestrator (`DeskGuard.Build`) running pre-build unit tests, Native AOT `ilc.exe` compilation, manifest SHA-256 indexing, and Inno Setup installer generation (`compiled/DeskGuard.exe`).
