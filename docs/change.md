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

## [1.0.13] - 2026-10-29

### Added
- **2-Pane Split-Pane Forensics Workspace**:
  - Independent 2-pane triage workspace inside `#tab-forensics` featuring a left-side triage queue list (width 220px–550px) and a right-side deep diagnostic inspector panel, preserving 100% of dashboard screen space.
  - Interactive draggable vertical splitter (`#forensics-v-splitter`) with smooth pointer drag tracking and visual feedback.
  - Triage queue list equipped with category switcher (`All Verdicts`, `Malicious`, `Suspicious`, `Spam`, `Safe`), real-time search filtering, score deduction pill, verdict badge, AV badge, time, and auto-scrolling active selection highlight.
- **Auto-Advance Conveyor Belt**:
  - Taking action inside Forensics (`Quarantine` or `Delete`) automatically determines the next email in the queue, performs the backend action, updates `AppState` stats, and seamlessly advances the inspector to the next candidate without hitting dead ends.
  - Shows notification banner on advancement, or friendly empty-queue notice with auto-return to Dashboard when all queue emails are processed.
- **Synced Dashboard Highlight on Return**:
  - Returning to the Dashboard from Forensics (via Back to Dashboard, floating bottom pill, or `Esc` key) synchronizes `AppState.selectedId`, scrolls the virtual list to that exact row, and applies prominent active selection highlighting for continuous spatial context.
- **Always-Visible Floating Bottom Action Pill**:
  - Ergonomic centered floating pill with backdrop blur pinned to the bottom of the viewport containing quick actions with clear keyboard shortcuts: `[← Esc Dashboard]`, `[◀ Prev []`, `[X of Y counter]`, `[Next ▶ ]]`, `[✉️ Outlook O]`, `[🛡️ AV Scan S]`, `[⚠️ Quarantine Q]`, and `[🗑️ Delete D]`.
  - Added bottom padding to inspector content to ensure attachments and payload are never obscured by the floating pill.
- **Multi-Engine AV Audit Visibility & Badging**:
  - Added `AvScanned`, `AvVerdict`, and `AvThreatName` properties to `EmailRecord` and mapped them across `VaultRepository` (`GetEmailById`, `GetUnscannedAvEmails`, `GetAllCategorizedEmails`, and full-text/filtered `SearchEmails`).
  - Added real-time log streaming for every scanned email in `OutlookMapiService` (both progressive background and on-access sweeps) logging subject, sender, engines (`Microsoft Defender & ClamAV`), and verdict (`Clean` or `THREAT DETECTED`) to both `live-log` UI and `AppLogger`.
  - Added `[🛡️ Clean]` and `[⚠️ AV Threat]` status badges to the main incident list and Forensic Report profile card.
- **Config-Persistent Dashboard Column Resizing & Borders**:
  - Added `ColumnWidths` dictionary to `ScanConfiguration` with backend IPC handler `save-column-widths` saving directly to `config.json`.
  - Added distinct vertical column borders and visible hover/drag grab handles to `.col-resizer` in `.incident-grid.list-header`.
  - Configured startup column width restoration in `ui_main.js` from `cfg.columnWidths`.
- **Forensic Report Stepper & Keyboard Navigation (Proposal 3)**:
  - Added `[◀ Prev]` and `[Next ▶]` stepper buttons to the Forensic Report toolbar showing live queue position (e.g., `Viewing 3 of 18`).
  - Added full keyboard shortcuts while in Forensics: `Escape` or `Backspace` to instantly return to Dashboard, `[` / `p` / `ArrowLeft` / `ArrowUp` / `k` for previous incident, `]` / `n` / `ArrowRight` / `ArrowDown` / `j` for next incident, `O` to open in Outlook, `S` for local AV scan, `Q` for quarantine, and `D` for delete.
- **Forensic Report Sticky Section Anchors & Section Cards (Proposal 4)**:
  - Added sticky anchor navigation bar (`📊 Overview`, `⚖️ Risk Penalties`, `🔍 Transport Headers`, `✉️ Message Body`, `📎 Attachments`) with smooth auto-scroll.
  - Reorganized forensic report into clean, dedicated section cards with attachments breakdown and a 1-click `📋 Copy Headers` button.

### Changed
- **Removed Verbose From-Column Text Tags**:
  - Removed `"⭐ Trusted Org"` and `"⚠️ External"` text badges from the dashboard `From` column cell, preventing text clipping and eliminating layout clutter.
- **Scan Progress Percentage Layout**:
  - Moved `#scan-progress-text` to the left of `#scan-progress-bar` before email numeric counts in the live security engine feed.

### Fixed
- **UI/UX Audit Ranked Issues (Ranks 1-11)**:
  - **Rank 1**: Fixed keydown filtering in `ui_main.js` to allow full triage shortcuts (`j`, `k`, `Enter`, `o`, `f`, `s`, `q`, `d`, `?`), and updated `handleListKeyNavigation` in `ui_list.js` to invoke existing methods (`openIncidentInOutlook`, `openForensicsTab`, `scanEmailAv`).
  - **Rank 2**: Fixed `ReferenceError` in `ui_settings.js` where `applyEnginePreset` called non-existent `renderSettings()`; replaced with `sync()` and `markDirty()`.
  - **Rank 3**: Fixed duplicate DOM ID `vault-search-btn` by renaming Search tab button to `vault-tab-search-btn` and updating `ui_search.js`.
  - **Rank 4**: Fixed Antivirus scan modal reopening defect by invoking `window.openModal('virustotal-modal')` instead of manually setting `.classList.add('active')` while `display: none` remained set.
  - **Rank 5**: Synchronized `window.AppState.stats.all` across `deleteIncident`, `releaseIncident`, and `quarantineIncident` in `ui_list.js` so "All Scanned" view updates immediately without page reload.
  - **Rank 6**: Added `position: sticky` to `.settings-search-bar` and `.settings-tab-nav` in `style.css` so search and tabs remain pinned while scrolling.
  - **Rank 7**: Added `role="button"`, `tabindex="0"`, `aria-label`, and keyboard Enter/Space activation to sortable `.col-header` elements.
  - **Rank 8**: Added response validation (`res && (res.ok || res.success)`) to Activity Feed release and trust action handlers in `ui_activity.js`, displaying error states if operations fail.
  - **Rank 9**: Implemented `modalStack` in `ui_main.js` (`openModal` / `closeModal`) to properly restore parent focus trap handlers and active elements when closing nested modals.
  - **Rank 10**: Overrode `--ok: #2e7d32;` and `--safe: #2e7d32;` under `[data-theme="light"]` in `style.css` to achieve WCAG AA compliant 5.14:1 contrast ratio.
  - **Rank 11**: Increased touch and click target sizes for Quick Search `#vault-search-btn` and `#vault-search-clear` to `min-height: 24px; padding: 4px 10px;`.
---

## [1.0.12] - 2026-10-28

### Added
- **Zero-Cost Anti-Tamper Authenticode & RSA Manifest Signing (`ssl-certificate/`)**:
  - Generated offline RSA 3072-bit Code Signing certificate and RSA SHA-256 signing keys in `ssl-certificate/`.
  - Added `AuthenticodeVerifier.cs` and `SigningConstants.cs` verifying WinVerifyTrust signatures on binaries and cryptographic RSA-SHA256 signature on `update.json.sig`.
  - Integrated automated binary signing and manifest signing into `Staging.cs` and `InnoInstaller.cs` via `Signing.cs`.
- **Atomic Rollback & Transactional Deployment Snapshot (`DeploymentLifecycle.cs`)**:
  - Implemented transactional backup creating timestamped `.bak` files during installation/update overwrites.
  - Automatically restores backup snapshot and rolls back if file replacement fails, protecting user data and binary integrity.
- **Graceful Process Termination (`DeploymentLifecycle.cs`)**:
  - Implemented cooperative window closing via `CloseMainWindow()` / `WM_CLOSE` with timeout before fallback force-kill, ensuring clean SQLite WAL flush and COM unregistration.
- **Split-Brain Dual Installation Prevention (`DeploymentLifecycle.cs`)**:
  - Detects existing Program Files installation when standard user declines UAC elevation, preventing fragmented parallel installs in LocalAppData.
- **Background & Manual Update Engine (`UpdateManager.cs`)**:
  - Automatic background checks and manual "Check for Updates" in both system tray context menu and Settings Tab 1.
  - Strict SemVer downgrade guard preventing rollback to older releases.
  - Verification of Authenticode signature on downloaded installer packages prior to execution.
- **In-App "What's New" Experience (`#whats-new-modal`)**:
  - Automatically displays "What's New in DeskGuard" modal on first run after an update, converting changelog into plain-English release highlights.
  - Accessible on-demand via "What's New" button in Settings Tab 1.
- **5-Release Installer Binary Retention Policy (`GitRelease.cs`)**:
  - Enforces FIFO binary retention: retains installer binaries for the 5 latest verified releases, pruning older binaries via `gh release delete-asset` while preserving release notes and git tags.
- **Database Downgrade Guard & Migration Encapsulation (`VaultMigrationManager.cs`)**:
  - Guard throws `InvalidOperationException` if SQLite database `user_version` exceeds `CurrentSupportedVersion = 3`, blocking corruption from future schema versions.
  - Encapsulated 13 startup `ALTER TABLE` statements so they execute only during version upgrades instead of every startup.
- **Local Antivirus Disassembly & Inspection Engine (`LocalAntivirusService.cs`)**:
  - Multi-piece email disassembly partitioning emails into sandboxed artifacts: message headers (`headers.txt`), body content (`body.html`), and file attachments (individually extracted and scanned isolated from other email components).
  - Dual-engine scanning pipeline via Microsoft Defender command-line utility (`MpCmdRun.exe`) and local ClamAV (`clamscan.exe`), streaming real-time stage events (`ProgressChanged`).
  - Unit tests in `LocalAntivirusServiceTests.cs` verifying path discovery, exit code handling, and disassembly execution.
- **Deep Local AV Scan Mode (`DeepLocalAvScanAll`)**:
  - Setting switch in `ScanConfiguration.cs` and `ui_settings.js` ("Scan Every Email with Local Antivirus - No Exceptions").
  - On-access scan priority (`JobPriority.P0`) for incoming and unread emails; progressive background crawl (`RunDeepAvBackgroundScanAsync`) at lowest priority (`JobPriority.P3`), yielding (`YieldIfHighPriority`) to incoming mail and user operations.
  - Persistent SQLite tracking (`av_scanned = 1`, `av_verdict`, `av_threat_name`, `av_scanned_at`) with index `idx_emails_av_scanned`, preventing redundant re-scanning of historical items.
- **FTS5 Search Engine Optimization & Inline Syntax Operators (`VaultRepository.Search.cs`)**:
  - Advanced search operator parser (`ParseSearchOperators`) supporting `from:`, `to:`, `subject:`, `folder:`, `has:attach`, `has:noattach`, `score:<N`, `score:>N`, `score:<=N`, `score:>=N`, `score:=N`, `is:unread`, `is:read`, `is:malicious`, `is:spam`, `is:suspicious`, `is:clean`, `verdict:`, `after:`, `before:`.
  - Exact phrase tokenization for multi-word queries in quotes (`"Project Titan"`).
  - Fast-path execution for operator-only queries executing indexed SQL directly without table scans or broken FTS match attempts.
  - Unit tests in `VaultRepositoryTests.cs` verifying syntax operator extraction and execution.
- **Interactive Quick Filter Chips**:
  - Added filter chips toolbar in `index.html` (`[📎 Attachments]`, `[⚡ Unread]`, `[⚠️ Malicious]`, `[🚫 Spam]`, `[🛡️ Score < 50]`, `[✅ Clean]`) with active toggle states and bidirectional synchronization with the search input.
- **Sender Domain Trust Badges in From Column**:
  - Rendered inline visual trust badges in `ui_list.js`: `[⭐ Trusted Org]` (green) for whitelisted/high-reputation senders, `[⚠️ External]` (amber) for external/first-contact senders, and `[🚫 Threat]` (red) for malicious senders, cached by sender key for smooth 60 FPS virtualization scrolling.
- **Non-Blocking Bottom-Right Toast Notification Stack**:
  - Replaced single overlay toast with `#toast-container` stack supporting concurrent notifications, severity styling (`.toast-info`, `.toast-success`, `.toast-warning`, `.toast-error`), dismiss buttons, and auto-dismissal.
- **Keyboard Triage Shortcuts & Cheat Sheet Modal**:
  - Added keybindings in `ui_list.js`: `J` / `ArrowDown` (next email), `K` / `ArrowUp` (previous email), `Enter` / `O` (open in Outlook focused), `F` (forensic report), `S` (local AV scan), `Q` (quarantine), `D` (delete), `?` (cheat sheet modal).
  - Added `#keyboard-shortcuts-modal` cheat sheet with styled `<kbd>` keycaps.
- **Interactive Grabbable Feed Splitter**:
  - Added `#feed-splitter` horizontal divider between master email table and live security engine feed, allowing drag-to-resize vertical space with an enlarged completion progress bar and clear draggable grip indicator ("▲ ≡≡≡ DRAG TO RESIZE LIVE FEED ≡≡≡ ▼").
- **Action Column Manual Local AV Scan**:
  - Added `[🛡️ AV Scan]` button in dashboard incident table and `[Scan AV (Defender & ClamAV)]` button in Forensic Report tab with real-time disassembly and result modal.

### Changed
- **Single-Row Compact Dashboard Layout**: Compacted statistics cards (`width: 92px`, `padding: 5px 8px`) and search keywords input so all cards (including Clean) and search controls fit on a single horizontal row without wrapping, saving screen real-estate.
- **Dedicated Search & Clear Buttons**: Added dedicated `[🔍 Search]` and `[✖ Clear]` buttons with Enter key submission.
- **Draggable Feed Splitter Indicator**: Heightened feed splitter to 16px with clear grab bar.
- **Thickened Live Security Feed Progress Bar**: Enlarged progress bar to 16px pill container with inline tracking: "X / Y emails (Z left)".
- **3-Position Discrete Scan Speed Slider**: Background scan speed slider now locks cleanly into 3 discrete positions (Gentle: 10, Balanced: 50, High Throughput: 100) instead of floating between non-distinct values.
- **Unified SemVer Release Pipeline**: `build/DeskGuard.Build/Program.cs` derives target SemVer once and synchronizes assembly versions, Inno Setup `/DAppVersion`, manifest `update.json`, git tags, and registry `DisplayVersion`.
- **Strict Git Release Staging**: Replaced `git add -A` with explicit staging of changelog and metadata files; non-zero exit codes during push or release publication fail the build immediately.
- **Dashboard Table Alignment**: Centered column headers (`.col-header`) and action cells (`.row-actions-cell`) for visual consistency with table data.
- **Active Directory-Style Settings Tabs**: Enhanced tab button headers with brighter fill and distinct active states; consolidated save buttons to a single bottom "Save Changes" and red "Discard" button; added red "Reset to Defaults" button.
- **Outlook Window Activation**: Integrated Win32 `AllowSetForegroundWindow(-1)` in `DashboardWindow.cs` ensuring emails opened from DeskGuard pop up focused in the foreground above the dashboard.
- **Default Engine Weights**: Normalized default engine weights in `ScanConfiguration.cs` from `ScoringEngine.DefaultWeights` guaranteeing a complete 100% distribution across all rules.

### Fixed
- **Dashboard All Scanned List**: Fixed empty incident list when selecting "All Scanned" card by indexing safe/clean emails into `stats.all` and expanding verdict matching in `VaultRepository.cs`.
- **Settings Toggle Persistence for Deep Local AV Scan**: Added missing `deepLocalAvScanAll` property to `get-config` payload in `DashboardWindow.cs`, allowing toggle state to persist properly across settings reloads.
- **Historical Mailbox Scan Depth Setting**: Corrected UI state synchronization for Historical Scan Depth, ensuring toggling and saving updates configuration cleanly.
- **False Protection Disabled Indicator**: Fixed false "Protection Disabled" status indicator in `ui_main.js` by parsing Outlook status payload structure properly.
- **Vault Search Scope & From Filter**: Fixed `search-vault` IPC handler in `DashboardWindow.cs` to extract `scope` and pass `fromFilter` properly instead of concatenating into plain query words.
- **Duplicate Scanner Progress Feedback**: Fixed duplicate scanner progress feedback by connecting `api.onDuplicateUpdate` to stream real-time folder traversal and item discovery in `ui_duplicates.js`.
---

## [1.0.11] - 2026-10-28

### Added
- **5-Tab Settings Menu Architecture**: Overhauled Settings preferences into 5 clear tabs: `General & Startup`, `Anti-Spam Engines`, `Threat Cloud & VirusTotal`, `Senders & Rules`, and `Maintenance & Danger Zone` with live instant search filtering across all settings.
- **Native Win32 DPAPI Encryption for VirusTotal API Key**: Secured `VtKey` at rest using `CryptProtectData` and `CryptUnprotectData` via native Windows `Crypt32.dll` (`"dpapi:..."`), preventing plain-text credential leaks in `config.json`.
- **VirusTotal Connection Test & Visibility Eye Toggle**: Added password reveal/mask toggle (`•••••` / plain text) and interactive `[Test Connection]` button verifying live API key connectivity against cloud threat intelligence endpoints.
- **Theme Selection & Dynamic Switching**: Added `Theme` property (`dark`, `light`, `system`) with instant CSS variable switching and session persistence.
- **Interactive Senders & Rules Tag Managers**: Implemented interactive pill tag managers for Trusted Senders (Whitelist), Blocked Senders (Blacklist), and Custom Spam Phrases with immediate add/remove.
- **Sanitized Export & Safe Merge Import**: Added `CreateExportPackage()` redacting secrets and screen coordinates for safe export sharing, and `ImportSettings()` merging policy settings safely without clobbering credentials or window geometry.
- **Restore Factory Defaults**: Added one-click default policy restore with confirmation modal.
- **Isolated Danger Zone**: Isolated destructive actions (Clear Incident Vault, Scorched Earth Factory Reset) in an explicit red warning card with keyword confirmation dialogs.
- **Duplicate Cluster Cards Architecture**: Overhauled Duplicate Email Finder with grouped Cluster Cards for 2, 3, or more copies, displaying the authentic survivor in a locked green shield banner (`🛡️ SOLE SURVIVOR: KEEPING SAFE`), with direct `[Open Survivor in Outlook]` button and zero-accidental-deletion protection (survivor checkbox is locked/disabled).
- **Redundant Copy Inspection & Safe Cleanup**: Listed all redundant copies with individual checkboxes, folder badges, sizes, timestamps, and `[Open Copy in Outlook]` buttons; `Select All` selects redundant copies only, leaving 100% of survivors untouched.
- **Full-Width Dashboard Table with 5 Row Actions**: Expanded dashboard table to 100% width by removing the right detail pane; added 5 per-row action buttons in exact sequence: `[Open in Outlook]`, `[Forensic Report]`, `[VirusTotal]`, `[Delete Item]`, and `[Quarantine]`.
- **Row Double-Click Integration**: Double-clicking any incident row immediately launches the message in Microsoft Outlook inspector.
- **Draggable Table Column Resizers**: Implemented draggable column resize handles for dashboard and duplicate tables with session persistence in `localStorage`.
- **Dedicated Forensic Report Tab**: Added a full-page `#tab-forensics` navigation tab with diagnostic profile card, visual score deduction bar, full message body preview, technical MIME transport header inspector, and action buttons.
- **Left Navigation Reorganization**: Reordered sidebar to: Dashboard &rarr; Duplicate Email Finder &rarr; Forensic Report &rarr; Search Through Emails &rarr; Activity Feed &rarr; Audit Logs &rarr; Settings; renamed "Vault Search" to "Search Through Emails" and "Duplicate Finder" to "Duplicate Email Finder".
- **Continuous Historical Mailbox Sweeping**: Background scanning now performs a fast priority sweep for recent items, then seamlessly continues crawling older historical emails across all folders (`RunSweepAsync(DateTime.MinValue)`) and indexes the search vault (`RunCrawlerAsync`), resolving the issue where scanning halted at 525 emails.
- **Targeted On-Access Surveillance**: Restricted on-access scanning events strictly to unread and newly arriving messages (`isLiveArrival && mailItem.UnRead`).
- Added comprehensive unit tests in `ScanConfigurationTests.cs` for DPAPI round-trip encryption, sanitized export package, safe merge import, and default configuration values.

### Changed
- Removed top dashboard control line ("All scanned Emails", verify list, engine weights); engine weights and sensitivity tuning moved into Settings Tab 2.
- Streamlined "Full Audit" confirmation dialog in `ui_main.js` to a single-click "Yes, Proceed" button, eliminating the required keyword typing ("AUDIT").
- Corrected threat scoring documentation in `docs/core.md` from legacy inverted hazard scores to the true 100.0% safety deduction model with exact threshold bounds.
- Corrected host window architecture descriptions in `docs/app.md`, `docs/architecture.md`, and `project_structure.txt` from legacy WinForms references to native Win32 unmanaged windows.

### Fixed
- Fixed unintended mailbox sweeps on settings save: `DashboardWindow.cs` now guards `set-enabled` with `!wasEnabled`, preventing repetitive sweep triggers when saving settings.
- Fixed mailbox scanning stopping at 525 emails by chaining background deep sweeps past the 7-day cutoff.
- Fixed sweep completion handler in `ui_main.js` silently disabling `HistoryScanEnabled` (`api.setHistoryEnabled(false)`) upon sweep completion.
- Fixed duplicate items deletion logic enforcing strict folder hierarchy (Inbox > Sent > user folders / archive > Junk > Conflicts > Deleted Items) in both backend election and UI rendering.
---

## [1.0.10] - 2026-10-28

### Added
- Centralized persistent file logger `AppLogger` (`%LOCALAPPDATA%\DeskGuard\logs\deskguard.log`) capturing detailed timestamps, log levels, COM activation steps, folder traversals, STA queue jobs, duplicate audits, and IPC events.
- Sidebar protection control panel (`.sidebar-protection-box`) hosting the "Protection Active" toggle switch in the left navigation pane directly above Settings for intuitive access across all tabs.
- Real-time incident list updates in `ui_main.js`: incoming `outlook-scan-update` events with `item` payloads now immediately prepend newly evaluated emails into `AppState.stats` and re-render the dashboard.
- Immediate persistence for `cfg-speed` changes in `ui_settings.js`, instantly saving the user's selected scan speed to `config.json`.
- Visual error reporting and graceful termination in `ui_duplicates.js` for duplicate scan failures.

### Changed
- Compact dashboard statistics cards: `.stats-grid` refactored to flexbox with `.stat-card` width reduced by more than half (max 145px, 7px padding) to reclaim horizontal and vertical screen space while maintaining complete text clarity.
- Default `ScanningSpeed` in `ScanConfiguration.cs` set to `10` ("Gentle (Low CPU)"), persisting end-user preferences across restarts.
- Removed redundant `Security Dashboard` title and `.header-area` bar from `index.html` to eliminate vertical space waste.
- Updated `open-logs-folder` IPC action in `DashboardWindow.cs` to launch the active `%LOCALAPPDATA%\DeskGuard\logs` directory.
- Refactored `RunSweepAsync` in `OutlookMapiService.cs` to evaluate emails folder-by-folder incrementally, surfacing live scanned items within seconds.

### Fixed
- Fixed Outlook COM connection hang caused by process integrity level / UAC elevation mismatch: when DeskGuard is started elevated (e.g. from an installer running with administrator rights), Windows UIPI blocks COM automation to standard-user Outlook instances. DeskGuard now detects elevation on startup and automatically re-launches at Medium Integrity via the Windows Explorer desktop session, while `DeploymentLifecycle.LaunchInstalledApp` delegates launch to `explorer.exe`.
- Fixed COM activation in `ComRef.cs`: ordered activation to attempt `GetActiveObject` then fast .NET COM factory `Activator.CreateInstance(Type.GetTypeFromProgID("Outlook.Application"))` with native `CoCreateInstance` fallback, eliminating 60-second RPC hangs and logging full diagnostics for every step.
- Initialized OLE subsystem via `OleInitialize` and `OleUninitialize` on the dedicated STA worker thread in `OutlookStaCoordinator.cs`.
- Added connection failure backoff (15 seconds) to watchdog in `Program.cs` to prevent high-frequency connection retry loops while acquiring Outlook sessions.
- Fixed duplicate scanner hanging without output or errors by routing discovery asynchronously via `_mapiService.ScanDuplicatesAsync` and streaming progress events to `ui_duplicates.js`.
- Fixed mutex reentrancy lockouts in `OutlookNamedMutex.cs` by tracking recursion count (`_lockCount`) before releasing the Win32 named mutex.
- Extended lock acquisition timeout in `OutlookStaCoordinator.cs` from 5s to 30s to prevent spurious circuit breaker tripping during intensive Outlook MAPI operations.
- Fixed `set-enabled` toggle in `DashboardWindow.cs` to initiate a 7-day mailbox sweep when turned on, ensuring immediate scanning results in the dashboard.
---

## [1.0.9] - 2026-10-28

### Added
- Implemented interactive column sorting (Date, Subject, Sender, Size, Duplicate Folder, Survivor Folder) with visual sort indicators in `ui_duplicates.js`.
- Implemented persistent and resizable column widths with draggable resizer handles in `ui_duplicates.js` and `style.css`, saving custom widths in `localStorage`.
- Implemented instant duplicate in-list search filter in `ui_duplicates.js` supporting subject, sender, and folder queries.
- Implemented individual checkbox selection with "Select All" toggle and selective deletion in `ui_duplicates.js`.
- Added recoverable storage metrics displaying total and selected reclaimable disk space (KB/MB/GB) in `ui_duplicates.js`.
- Added visual cluster grouping for duplicate copies belonging to the same email in `ui_duplicates.js` and `style.css`.
- Added direct "Engine Weights" configuration button to Security Dashboard header in `index.html`.
- Implemented `pause-duplicate-scan`, `resume-duplicate-scan`, and `reset-duplicate-engine` IPC endpoints in `DashboardWindow.cs`, enabling interactive duplicate scanning control and engine reset.
- Added duplicate scan persistence and event broadcasting from system tray in `Program.cs` via `vault.SaveDuplicates()` and `dashboard.PostEvent("duplicate-update", ...)`.
- Added `role="tablist"`, `role="tab"`, and `aria-selected` keyboard tab management to forensic security analysis tabs in `ui_forensics.js`.
- Implemented accessible `.modal-close-btn` SVG buttons and `aria-label="Close"` across all modal dialogs in `index.html`.
- Implemented `export-config` and `import-config` IPC endpoints in `DashboardWindow.cs` alongside interactive JSON file download and upload triggers in `bridge.js` and `ui_settings.js` for security policy backups and migrations.
- Implemented `set-whitelist`, `set-blacklist`, and `set-spam-keywords` IPC endpoints in `DashboardWindow.cs` with robust array and object JSON parsing to persist trusted/blocked senders and custom spam keywords.
- Implemented real VirusTotal attachment and body artifact auditing in `DashboardWindow.cs` (`case "scan-virus"`), supporting live attachment hash extraction via `GetAttachmentsWithHashesAsync` and structured report payloads formatted for `ui_list.js`.
- Implemented active evaluation logic in `ScoringEngine.cs` for previously dormant engines: `rdns` (Reverse DNS verification `RDNS_MISSING` and dynamic host `RDNS_GENERIC`), `rbl` (Global DNSBL blacklists `GLOBAL_RBL_HIT`), `heuristics` (custom spam keywords from `ScanConfiguration.SpamKeywords` and `Rubrics`), and `spamflag` (upstream server headers `HEADER:X-Spam-Flag`).
- Added unit tests in `ScoringEngineTests.cs` verifying `rdns`, `rbl`, `heuristics`, and `spamflag` rule scoring and penalties.

### Changed
- `ui_duplicates.js`: Streamlined initial duplicate finder screen into a focused 1-click scan card; removed repetitive "(Protected)" label text and redundant row "Open" buttons to reallocate horizontal space to email Subject and Sender.
- `index.html`: Cleaned up primary navigation sidebar by removing modal opener `nav-engines`, keeping the sidebar strictly dedicated to top-level view navigation.
- `OutlookMapiService.ScanDuplicatesAsync`: Refactored duplicate election to cluster all scanned emails by DNA hash, electing a single definitive survivor per cluster (prioritizing non-junk folders and larger file sizes) and pointing all duplicates directly to that survivor, eliminating intermediate survivor chains and duplicate deletion rejections.
- `DashboardWindow.cs`: Updated `verify-existence` to deserialize items and invoke `_mapiService.CheckExistenceAsync`, returning real prune counts and user-relocation metrics.
- `DashboardWindow.cs`: Updated `scan-duplicates` to prioritize live Outlook MAPI scanning with `includeJunk` support, falling back to SQLite vault duplicate queries only when Outlook is unavailable or returns 0 items.
- `OutlookMailboxCrawler.DiscoverMailFolders`: Added `bool includeJunk = false` parameter allowing junk folder crawling when requested while maintaining junk exclusion by default.
- `ui_reset.js` and `DashboardWindow.cs`: Enhanced "Scorched Earth Factory Reset" to reset in-memory configuration `_config = new ScanConfiguration()`, wipe `config.json` on disk, notify `_mapiService`, and trigger UI reload.
- `ui_engines.js`: Updated `loadFrom()` to parse sensitivity weights, toggles, and thresholds directly from root configuration properties, preventing reset to factory defaults upon reopening the dialog.
- `index.html`: Replaced hardcoded `#stat-spam` inline color `#996100` with design system token `var(--warn)` to guarantee WCAG 2.2 AA compliant contrast.

### Fixed
- Fixed attachment check false positives in `ScoringEngine.cs`: replaced substring search with strict extension termination matching (`EndsWith`), preventing safe files (e.g. `log.cmd.txt`, `react.js.pdf`) from triggering malicious quarantine penalties.
- Fixed duplicate email DNA validation mismatch in `VaultRepository.Duplicates.cs` and `OutlookMapiService.cs`: normalized duplicate item DNA to computed SHA-256 hash matching `OutlookItemAccessor.GetItemDna()`, allowing duplicate emails to move or delete cleanly.
- Fixed factory reset in-memory cache retention in `DashboardWindow.cs`: invoking `ClearProcessedCache` on `OutlookMapiService` during factory resets so subsequent sweeps rescann and index historical emails.
- Fixed Activity Feed filtering and missing action buttons in `ui_activity.js` and `VaultRepository.cs`: normalized event kind filtering case-insensitively and enabled quick "Release" and "Trust" action buttons for quarantine records.
- Fixed "Open in Outlook" for moved emails in `OutlookActionsService.cs` and `DashboardWindow.cs`: handled binary byte arrays for `PR_ENTRYID` conversions and added fallback search by fingerprint and Message-ID.
- Fixed synthetic forensics transport headers in `DashboardWindow.cs` and `ui_forensics.js`: exposed raw RFC 822 transport headers in `get-forensics` response and prioritized actual headers over synthetic fallbacks.
- Fixed incident list selection, counters, and detail panel clearing on release/quarantine in `ui_list.js`: updated `AppState.stats`, decremented counts, refreshed dashboard cards, and cleared detail panel when selected incident is removed.
- Fixed dirty tracking in `ui_settings.js`: registered change listeners for `cfg-spam-dest` and `cfg-label-outlook` so modified settings prompt for save.
- Fixed accessible naming in `index.html`: added explicit `aria-label` attributes to vault search date, folder, and verdict inputs.
- Fixed syntax error in `ui_activity.js:103`: removed duplicate unmatched `});` closing token that crashed JavaScript execution and broke the Activity tab feed.
- Fixed log stream suppression in `ui_main.js:328-340`: removed uninitialized `originalAddLog` check so `window.addLog()` reliably appends logs to `#live-logs` in the System Audit Logs tab.
- Fixed unhandled whitelist/blacklist IPC channels in `DashboardWindow.cs` and `ui_main.js`: "Release + Trust sender" now persists trusted sender addresses to `_config.Whitelist` and `config.json`.
- Fixed Outlook quarantine folder creation type in `OutlookActionsService.cs:116`: replaced `OlDefaultFolders.olFolderInbox` (value 6 = `olPostItem`) with `Type.Missing`, ensuring new quarantine folders are standard `MailItem` folders.
- Fixed SQLite database desynchronization on release and quarantine moves: updated `IVaultRepository.UpdateVerdict` and `VaultRepository.Reconciliation.cs` to accept `newFolder` and persist updated `current_folder` in the database.
- Fixed historical email timestamps in `OutlookMapiService.cs`: replaced `DateTime.Now` with `mailItem.ReceivedTime` in `ProcessSingleEmail` and `IndexSingleEmail`, restoring accurate email received dates and historical sorting.
- Fixed Outlook COM connection failure in `ComUtils.CreateOrGetOutlook` (`ComRef.cs`): removed loop that returned `null` whenever `OUTLOOK.EXE` was running, allowing standard COM `CoCreateInstance` / `Activator` fallback to attach to active Outlook 365 / Click-to-Run processes that do not register in the legacy Running Object Table (`GetActiveObject`).
---

## [1.0.8] - 2026-10-27

### Added
- Decoupled SQLite WAL reader-writer architecture in `VaultRepository`: dedicated `_readConnection` alongside primary `_connection` with independent synchronization roots (`_readSyncRoot`, `_writeSyncRoot`), permitting simultaneous background STA writes and UI/ThreadPool search, stats, and duplicate queries without contention.
- Reliable live mail retry queue (`_retryQueue`) and `DrainRetryQueue()` on `IOutlookMapiService` and `OutlookMapiService`, ensuring incoming live emails are never permanently lost if transiently deferred.
- Comprehensive concurrent read/write test `ConcurrentReadsAndWrites_ExecuteSimultaneouslyWithoutBlocking` in `VaultRepositoryTests.cs`.
- Comprehensive atomic configuration persistence test `ScanConfiguration_AtomicSaveAndLoad_MaintainsIntegrity` in `UnitTest1.cs`.

### Changed
- `OutlookStaCoordinator.ExecuteJobWithRetry`: Priority-scoped circuit breaker trips so that background batch scans (`P2` sweeps, `P3` duplicate detection) cannot block real-time `P0` incoming mail ingestion or `P1` interactive user operations.
- `DashboardWindow.cs`: Offloaded synchronous heavy vault operations (`scan-duplicates` containing `FindDuplicates` and `SaveDuplicates`, `trust-sender`, and `app-reset`) from the UI STA thread into background `Task.Run` workers, preventing UI hangs and WebView2 message pump freezes.
- `DeploymentLifecycle.cs`: Made cryptographic `update.json` manifest verification mandatory in `RunInstallWorkerDirect`, eliminating insecure installation fallback that allowed unverified binary execution under elevated privileges.

### Fixed
- Fixed SQLite database concurrency lock contention where UI queries blocked STA message processing in `VaultRepository.cs`, `VaultRepository.Search.cs`, and `VaultRepository.Duplicates.cs`.
- Fixed global circuit breaker dropping incoming emails in `OutlookStaCoordinator.cs` and `OutlookMapiService.cs` when background sweeps experience COM busy timeouts.
- Fixed COM RCW use-after-free and folder type conversion failure in `OutlookActionsService.GetQuarantineSubfolder`: prevented releasing parent folder when returning it on subfolder creation error, and replaced invalid enum parameter with `Type.Missing`.
- Fixed COM RCW handle leak in `OutlookLiveWatcher.RegisterWatchers`: wrapped `ItemAdd` event handler in `try/finally` with `ComUtils.Release(item)` to release unmanaged COM references immediately upon mail arrival.
- Fixed non-atomic file writes in `ScanConfiguration.Save`: implemented atomic write-and-replace using temporary staging file and `File.Move(..., overwrite: true)` with validation in `ScanConfiguration.Load`.
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
