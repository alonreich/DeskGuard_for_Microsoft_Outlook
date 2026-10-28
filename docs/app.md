# DeskGuard.App - Windows Host, Tray & Modern UI

## 1. Module Overview
`DeskGuard.App` is the Windows entry point and user-facing host process. It provides:
- Single-instance lifecycle control (`Local\DeskGuard_App_SingleInstance_Mutex`).
- Native Win32 notification area (System Tray) icon and context menu via `Shell_NotifyIconW`.
- Embedded Microsoft Edge WebView2 modern dashboard window hosted in a borderless native Win32 window (`CreateWindowExW`).
- Bi-directional asynchronous JSON-RPC bridge between C# backend and HTML/JS frontend.
- Process integrity elevation detection and automatic Medium Integrity re-launch to prevent COM UIPI blockades.
- Application footprint bootstrapping, shortcut management, and Windows auto-startup configuration.

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.App/
|-- Program.cs                  # [Entrypoint] Elevation check, mutex check, service wiring, watchdog, message pump
|-- DashboardWindow.cs          # Native Win32 window hosting WebView2 control, IPC message dispatcher
|-- TrayIconController.cs       # Native Win32 message-only window, Shell_NotifyIconW, context menu, notifications
|-- Win32SynchronizationContext.cs # Custom SynchronizationContext dispatching to Win32 message loop
|-- DeploymentLifecycle.cs      # CLI flag handling (--install, --uninstall, --first-run) & explorer.exe launcher
|-- DeploymentFootprint.cs      # User data directory paths (%LocalAppData%\DeskGuard)
|-- StartupManager.cs           # Registry run-key management for auto-launch on Windows login
|-- ShellLinkWriter.cs          # Native Windows shortcut (.lnk) creation via IShellLink COM interface
`-- wwwroot/                    # Embedded frontend assets (HTML5, CSS3, Vanilla JS)
    |-- index.html              # Single-page dashboard markup, navigation sidebar & modal definitions
    |-- style.css               # Modern dark-mode UI stylesheet with CSS variables
    |-- bridge.js               # Frontend IPC bridge wrapping window.chrome.webview.postMessage
    |-- ui_main.js              # State manager (AppState), live updates, tab navigation, global shortcuts
    |-- ui_list.js              # Full-width incident table, column resizers with memory, 5 action buttons
    |-- ui_forensics.js         # Dedicated Forensic Report tab & deep inspection viewer (penalties, headers, body)
    |-- ui_duplicates.js        # Duplicate Email Finder with cluster cards, survivor lock, and redundant batch cleanup
    |-- ui_activity.js          # Security event activity feed (quarantined, released, trusted events)
    |-- ui_engines.js           # 16-engine sensitivity sliders, weights rebalancing, and threshold bar
    |-- ui_search.js            # Real-time search query builder, datalists, and vault query filters
    |-- ui_settings.js          # 5-tab settings console (General, Engines, Cloud/VT, Senders/Rules, Danger Zone)
    |-- ui_reset.js             # Vault purge and factory reset wizard (Safe Upgrade / Scorched Earth)
    `-- ui_modals.js            # Universal draggable, 8-directional resizable modal window manager
```

---

## 3. UAC Elevation & Medium Integrity Re-Launch
Windows User Interface Privilege Isolation (UIPI) strictly forbids lower-integrity applications (like standard-user `OUTLOOK.EXE`) from accepting COM IPC messages sent by higher-integrity (elevated/admin) applications.
- If an installer runs with Administrator privileges and launches DeskGuard directly, DeskGuard would inherit high integrity, causing all Outlook COM connections to silently hang or fail (`RPC_E_ACCESS_DENIED` / `0x80070005`).
- **Solution (`Program.cs`)**: DeskGuard inspects the caller's Windows token integrity on startup using `WindowsIdentity`. If elevated, it automatically re-spawns itself at standard Medium Integrity via the Windows Explorer desktop session and cleanly exits the elevated instance.
- **Installer Delegation (`DeploymentLifecycle.cs`)**: `DeploymentLifecycle.LaunchInstalledApp` delegates execution to `explorer.exe <appPath>` to ensure the newly installed binary launches at desktop user integrity.

---

## 4. Key IPC Bridge Protocol
Communication between `DashboardWindow.cs` and `wwwroot/*.js` operates asynchronously over the WebView2 `WebMessageReceived` channel.

### 4.1 Frontend to Backend Commands
JavaScript invokes backend operations through `window.securityApi.*`:
- `get-stats`: Fetches total counts, threat tallies, and categorized emails (`{ full, stats, counts }`).
- `get-config`: Retrieves current scan configuration, engine weights, thresholds, whitelists, and startup status.
- `set-enabled`: Toggles master real-time mailbox sentinel monitoring on/off (triggers 7-day sweep only when transitioning from disabled to enabled).
- `set-history-enabled`: Toggles background sweep processing.
- `set-scanning-speed`: Adjusts scan speed delay `(100 - speed) * 4` ms.
- `set-threat-intel-level`: Updates threat intelligence tier (0=Local, 1=Community DNS, 2=Aggressive Cloud).
- `set-vt-key`: Updates VirusTotal API key (encrypted at rest using Win32 DPAPI).
- `test-vt-key`: Validates VirusTotal API key against live cloud endpoint using EICAR test hash.
- `set-theme`: Persists and applies UI theme preference (`"dark"` or `"light"`).
- `restore-defaults`: Resets all scan settings, engine weights, and threshold values to factory defaults.
- `set-rubrics`: Updates scoring weights, engine toggles, and threshold percentages.
- `set-scan-modes`: Updates on-access mode, deep history toggle, history scan scope (`"unread"` vs `"all"`), spam destination, and Outlook labeling.
- `start-full-scan`: Dispatches background sweep of all historical emails across all discovered mail folders.
- `set-startup`: Enables/disables auto-start via Windows Registry Run key.
- `check-startup`: Queries current status of Windows Registry Run key.
- `open-logs-folder`: Launches Windows Explorer displaying `%LocalAppData%\DeskGuard\logs`.
- `release-email`: Restores quarantined email back to Outlook Inbox.
- `quarantine-email`: Manually moves targeted email to Quarantine folder.
- `delete-email`: Soft or hard deletes targeted email items from Outlook.
- `open-email`: Locates and displays the email item natively inside Microsoft Outlook.
- `verify-existence`: Verifies presence of incident items in Outlook and reconciles local database records.
- `search-vault`: Executes indexed text and metadata search across all cached email records.
- `get-sender-suggestions`: Provides autocompletion suggestions for sender search queries.
- `scan-duplicates`: Dispatches asynchronous duplicate discovery crawl across mail stores.
- `pause-duplicate-scan` / `resume-duplicate-scan`: Controls active duplicate scanner state.
- `reset-duplicate-engine`: Cancels duplicate audit and clears duplicate working cache.
- `get-saved-duplicates`: Retrieves saved duplicate scan items from vault.
- `delete-duplicates`: Deletes selected duplicate copies in safe (trash) or permanent mode.
- `get-events`: Retrieves audit log security events for the activity feed.
- `get-forensics`: Fetches complete forensics snapshot (headers, penalties, body) for an email.
- `scan-virus`: Executes on-demand multi-engine malware audit via Team Cymru DNS / VirusTotal.
- `trust-sender`: Whitelists sender address and records trust event.
- `set-whitelist` / `set-blacklist`: Persists approved/blocked sender addresses.
- `set-spam-keywords`: Updates custom spam keyword heuristic filters.
- `export-config`: Exports configuration policy as sanitized JSON (redacting DPAPI secrets and window coordinates).
- `import-config`: Safely imports and merges non-destructive security policy configuration without overwriting master power state.
- `app-reset`: Executes safe incident cache purge or complete scorched-earth reset.
- `save-modal-config` / `get-modal-config`: Persists and restores custom modal window dimensions and positions.

### 4.2 Backend to Frontend Event Streams
Backend pushes real-time telemetry via `DashboardWindow.PostEvent(channel, data)`:
- `live-log`: Streaming diagnostic lines to the live activity console and audit logs.
- `outlook-scan-update`: Fires whenever an incoming or crawled email is analyzed (contains evaluated `item` payload).
- `duplicate-update`: Streams duplicate discovery progress (count, store, folder) and results.
- `outlook-status`: Notifies frontend of Outlook process connection or disconnection.
- `stats-update`: Real-time aggregate count refresh across categories.
- `status-sync`: Synchronizes master power toggle across UI controls.

### 4.3 Modern Navigation & User Interface Layout
- **7-Tier Sidebar Hierarchy**: Ordered by operational relevance and workflow priority:
  1. **Dashboard** (`#tab-dashboard`): Full-width incident table with resizable columns (persisted via `localStorage`), verdict pill badges, double-click to view in Outlook, and 5 direct row-action buttons: `[Open in Outlook]`, `[Forensic Report]`, `[VirusTotal]`, `[Delete Item]`, `[Quarantine]`.
  2. **Duplicate Email Finder** (`#tab-duplicates`): Clustered duplicate grouping cards with folder-hierarchy-based survivor election (`Inbox` > `Sent` > user folders > `Junk` > `Conflicts` > `Deleted`). Displays a locked `🛡️ SOLE SURVIVOR: KEEPING SAFE` banner with direct Outlook navigation. Batch selection targets only redundant copies, preventing accidental loss of the surviving copy.
  3. **Forensic Report** (`#tab-forensics`): Dedicated full-screen investigation workspace featuring email overview badges, 100-point penalty deduction breakdown, full message body viewer, complete MIME transport headers inspector, and direct remediation controls.
  4. **Search Through Emails** (`#tab-search`): Indexed metadata and keyword query builder across all cached mail records.
  5. **Activity Feed** (`#tab-activity`): Live security audit trail for quarantined, released, and trusted sender events.
  6. **Audit Logs** (`#tab-logs`): Real-time streaming diagnostic console and system event logs.
  7. **Settings** (`#tab-settings`): Streamlined 5-tab preferences console (`General & Startup`, `Anti-Spam Engines`, `Threat Cloud & VirusTotal`, `Senders & Rules`, `Maintenance & Danger Zone`) with instant search, DPAPI password masking/unmasking, live VirusTotal API validation, theme toggle, and factory restore controls.

---

## 5. Native Win32 Window Architecture & Threading
- **No WinForms Dependencies**: Both `DashboardWindow` and `TrayIconController` are implemented directly against Win32 unmanaged APIs (`user32.dll`, `shell32.dll`) using `[LibraryImport]` and unsafe `delegate*` function pointers.
- **OLE STA Thread Initialization**: The application entry point explicitly calls `OleInitialize` before any Shell or COM calls to ensure the main thread is an initialized STA apartment.
- **WebView2 STA Requirement**: Microsoft Edge WebView2 runs strictly on the main STA thread. Asynchronous environment and controller initialization pumps Windows messages via `WaitForTaskWithPump`.
- **Synchronization Context**: `Win32SynchronizationContext` marshals asynchronous continuations back to the main UI thread via `WM_APP_DISPATCH` (`WM_USER + 102`).
- **Background Offloading**: Heavy SQLite queries, mailbox sweeps, and COM crawl jobs are dispatched to worker threads or the `OutlookStaCoordinator` to keep the UI message pump responsive.
