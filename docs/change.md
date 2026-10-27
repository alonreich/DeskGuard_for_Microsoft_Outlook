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

## [1.0.7] - 2026-10-27

### Added
- Titlebar window dragging support via native Win32 `WM_NCLBUTTONDOWN / HTCAPTION` IPC channel (`window.winDrag()` in `bridge.js`, `mousedown` on `.titlebar` in `ui_main.js`, issue 30 & Rank 4).
- Titlebar double-click to maximize/restore window.
- Main window border and corner resizing support via native Win32 `WM_NCCALCSIZE` and `WM_NCHITTEST` message routing combined with `window.winResize()` edge strip triggers (`DashboardWindow.cs`, `bridge.js`, `style.css`).
- Native window geometry persistence: saves main window position (`WindowX`, `WindowY`), size (`WindowWidth`, `WindowHeight`), and maximized state (`WindowMaximized`) on move/resize in `ScanConfiguration.cs` and restores them on launch.
- Universal modal dialog window manager (`ui_modals.js`): makes every popup screen (`settings-modal`, `forensics-modal`, `sensitivity-modal`, `confirm-modal`, etc.) smoothly draggable by its header, resizable via 8 border/corner handles, maximizable/restorable via header double-click or maximize button, with custom geometry persisted in config for future sessions.
- Defined fallback `--safe: #6bb700;` variable in `:root` CSS, preventing missing styling across activity events and badges (Rank 7).
- Added `role="button"` and keyboard navigation (`tabindex="0"`, `Enter` / `Space` activation) to stat cards (issue 32) and titlebar controls (Rank 3).
- Added accessible `aria-label` attributes to sensitivity rubric weight buttons, toggles, and threshold sliders (`ui_engines.js`, Rank 6).

### Changed
- Brightened overall app background and surface theme from pitch black (`#0f111a` / `#12141d`) to a modern, legible dark slate palette (`--bg: #1b2030`, `--surface-low: #1f2436`, `--surface-mid: #272e45`, `--surface-high: #303955`, issue 33).
- Redesigned real-time protection control in dashboard header into a unified, intuitive ON/OFF toggle switch (`.protection-toggle-btn`) displaying clear "Protection Active" / "Protection Disabled" state and removed redundant "Monitoring Active" badge that wasted screen space (issue 31).
- Renamed stat cards: "Blocked Threats" -> "Malicious", "Spam Filtered" -> "Spam", "Clean Emails" -> "Clean", and restyled them to be slightly smaller and visually recognizable as interactive filter buttons with hover elevation and active bottom border indicators (issue 32).
- Lowered STA COM coordinator mutex acquisition timeout from 30,000ms to 5,000ms (`OutlookStaCoordinator.cs`) so that lock contention recovers rapidly rather than freezing the application thread for 30s.

### Fixed
- Fixed app freeze/hang where background Outlook `-Embedding` processes hung on OST locks or modal dialogs (issue 34 & Rank 8): `ComUtils.CreateOrGetOutlook()` in `ComRef.cs` now retries `GetActiveObject` when `OUTLOOK.EXE` is already running rather than spawning duplicate out-of-process COM servers via `CoCreateInstance(CLSCTX_LOCAL_SERVER)` that lock MAPI OST data stores.
- Fixed duplicate scanning and scan hang in `OutlookMapiService.cs`: deduplicated emails by Message-ID (`mid:{item.MessageId}`) in `RunSweepAsync` and `ProcessSingleEmail`, ensuring identical messages are never re-crawled, and properly released COM references on connection errors.
- Fixed IPC response schema mismatches (Rank 1 & 2): updated `DashboardWindow.cs` to return `{ ok, success }` for `release-email` and `quarantine-email`, robust multi-field envelopes (`items`, `rows`, `total`, `results`) for `search-vault`, and supported `kind` filtering in `get-events`.
- Fixed frontend response parsers in `ui_main.js`, `ui_search.js`, `ui_activity.js`, and `ui_list.js` to accept both direct array and nested envelope formats, and handle polymorphic property keys (`fromFolder` / `from_folder`, `toFolder` / `to_folder`, `timestamp` / `ts`).
---

## [1.0.6] - 2026-10-27

### Added
- Live sweep progress tracking event (`SweepProgress`) on `IOutlookMapiService` and `OutlookMapiService`, dispatching real-time `outlook-scan-update` IPC messages to WebView2 with current item count and processed volume.
- Sender extraction (`PrSentRepresentingNameW`) in `OutlookMailboxCrawler.ReadFolderItems` and `DuplicateItem.Sender` field propagation.
- Double-click item activation and explicit "Open" action button in `ui_duplicates.js` allowing users to jump directly to original emails in Microsoft Outlook via `window.securityApi.openEmail`.
- Modal focus trap and focus restoration in `ui_main.js` (`openModal`, `closeModal`) with `Tab` / `Shift+Tab` cycling and previous element restoration.
- Keyboard navigation support (`Enter` / `Space`) for sidebar navigation tabs with `role="tab"` and `aria-selected` status updates.
- Accessible ARIA dialog semantics (`role="dialog"`, `aria-modal="true"`, `aria-labelledby`) across all 7 application modal dialogs (`index.html`).
- Visible `:focus-visible` styling in `style.css` for buttons, navigation tabs, stat cards, inputs, and close controls.
- Accessible `aria-label` attributes for `#vault-search-input`, `#vault-from-input`, and `#vault-search-box`.

### Changed
- Redesigned Duplicate Email Discovery grid layout with 7 balanced columns: Date Received (135px), Subject (minmax 180px, 1.5fr), Sender (minmax 140px, 1fr), Size (75px), Duplicate Location pill (minmax 120px, 1fr), Survivor Protection badge (minmax 160px, 1.2fr), and Action button (85px).
- Adjusted activity feed timestamp (`.activity-ts`) and message text (`.activity-stream`, `.activity-msg`) contrast colors to meet WCAG 2.2 AA standards (4.8:1 and 6.2:1 against `#080a11`).
- Replaced blocking browser `window.alert()` and `window.confirm()` calls across all UI modules with non-blocking toast notifications (`window.showNotification`) and styled confirmation modals (`window.showConfirm`).
- `start-full-scan` now crawls all historical mailbox folders without date restriction by passing `DateTime.MinValue` to `RunSweepAsync`, resolving the issue where mailbox crawling stopped at 555 recent emails and ignored historical/archive stores.

### Fixed
- Resolved scan stopping at 555 emails: primary mailbox recent filter restriction (`AddDays(-30)`) previously caused `RunSweepAsync` to crawl only recent emails while ignoring historical archive stores (including PST stores with 70,000+ emails). Full audit now crawls all stores unbounded and streams live progress updates.
- Fixed `get-forensics` IPC handler in `DashboardWindow.cs` throwing `System.InvalidOperationException` by accepting polymorphic string or object payloads containing `entryId`, `fingerprint`, or `id`.
- Fixed missing `duplicate-update` event emission in `DashboardWindow.cs:scan-duplicates`, which previously caused the duplicate discovery UI to remain stuck on scanning spinners.
- Fixed `window.deleteIncident` in `ui_list.js`: now removes deleted item from `AppState.stats`, updates counters, clears detail panel if selected, and displays confirmation toast.
- Fixed settings modal silently dropping unsaved changes on backdrop click or `Escape` key by introducing dirty state checking (`window.settingsDirty`) and confirmation prompt before closing.
- Fixed Scorched Earth and Safe Upgrade resets in `ui_reset.js` falling back to raw browser `confirm()` by properly exporting `window.showConfirmModal` and requiring typed `"RESET"` keyword confirmation.
- Fixed "Reset Factory Defaults" in `ui_engines.js` immediately wiping custom engine weights without user confirmation.
---

## [1.0.5] - 2026-10-27

### Added
- Prominent protection toggle button (`#dashboard-power-btn`) in the dashboard header area (`index.html`, `style.css`, `ui_main.js`) with visual active/inactive states (`Protection ON` / `Protection OFF`), wired to `api.setEnabled`.
- `start-full-scan` IPC endpoint in `DashboardWindow.cs` and `bridge.js`, connecting the "Full Audit" button directly to `OutlookMapiService.RunSweepAsync`.
- Retry copy mechanism (`CopyFileWithRetry`) in `DeploymentLifecycle.cs` with exponential backoff to eliminate file locking failures during application upgrades.
- Script tag for `ui_reset.js` in `index.html` to enable Safe Upgrade and Scorched Earth factory reset operations from Settings.
- Configuration persistence via `ScanConfiguration.Load` and `ScanConfiguration.Save` targeting `%LocalAppData%\DeskGuard\config.json`.

### Changed
- Refactored `DashboardWindow.cs` IPC response and event serialization to enforce `CamelCase` property naming policy, aligning C# payloads with frontend property expectations.
- Configured NativeAOT JSON source generation context (`DeskGuardJsonContext.Default.ScanConfiguration`) in `ScanConfiguration.cs` to eliminate reflection trimmer warnings.
- Updated `ui_main.js`'s `AppState.updateStats` and `init()` to handle both direct and nested stats data envelopes, restoring email count counters and incident rendering.
- Enhanced `Program.cs` to trigger initial recent mailbox sweep on connect if history scan is enabled and synchronize tray status with configured protection state.

### Fixed
- Fixed settings modal blank hang caused by unhandled string-wrapped JSON in `DashboardWindow.OnWebMessageReceived` / `DispatchIpcMessageAsync` throwing `JsonElement is not an Object` when processing stringified bridge messages.
- Fixed post-install launch failure in `DeploymentLifecycle.cs` by replacing invalid `Process.Start("explorer.exe", targetExe)` with `Shell.Application.ShellExecute` to launch the upgraded application at interactive medium integrity.
- Fixed process termination in `DeploymentLifecycle.TerminateRunningAppInstances` to protect current installer and temp extraction processes from being terminated during self-updates.
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
