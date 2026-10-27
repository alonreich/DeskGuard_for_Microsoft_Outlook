# DeskGuard.App - Windows Host, Tray & Modern UI

## 1. Module Overview
`DeskGuard.App` is the Windows entry point and user-facing host process. It provides:
- Single-instance lifecycle control (`Local\DeskGuard_App_SingleInstance_Mutex`).
- Windows notification area (System Tray) icon and context menu.
- Embedded Microsoft Edge WebView2 modern dashboard window.
- Bi-directional asynchronous JSON-RPC bridge between C# backend and HTML/JS frontend.
- Application footprint bootstrapping, shortcut management, and startup configuration.

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.App/
|-- Program.cs                  # [Entrypoint] AttachConsole, mutex check, service wiring, loop
|-- DashboardWindow.cs          # Form hosting WebView2 control, IPC message router
|-- TrayIconController.cs       # WinForms NotifyIcon, context menu, notifications, icon states
|-- Win32SynchronizationContext.cs # Custom SynchronizationContext dispatching to Win32 message loop
|-- DeploymentLifecycle.cs      # CLI flag handling (--install, --uninstall, --first-run)
|-- DeploymentFootprint.cs      # User data directory paths (%LocalAppData%\DeskGuard)
|-- StartupManager.cs           # Registry run-key management for auto-launch on Windows login
|-- ShellLinkWriter.cs          # Native Windows shortcut (.lnk) creation via IShellLink
`-- wwwroot/                    # Embedded frontend assets (HTML5, CSS3, Vanilla JS)
    |-- index.html              # Single-page dashboard markup & modal definitions
    |-- style.css               # Modern dark-mode UI stylesheet
    |-- bridge.js               # Frontend IPC bridge to window.chrome.webview
    |-- ui_main.js              # Tab navigation, global event listeners, status pills
    |-- ui_list.js              # Scanned emails table, verdict badges, pagination
    |-- ui_forensics.js         # Security forensics modal (penalty breakdowns, headers)
    |-- ui_duplicates.js        # Deduplication viewer, conflict resolver, batch cleanup
    |-- ui_activity.js          # Live event audit log stream
    |-- ui_engines.js           # Engine diagnostics, Outlook COM connection state
    |-- ui_search.js            # Real-time search query builder and filter panel
    |-- ui_settings.js          # Configuration form (threat intel level, auto-quarantine)
    `-- ui_reset.js             # Vault purge and factory reset wizard
```

---

## 3. Key IPC Bridge Protocol
Communication between `DashboardWindow.cs` and `wwwroot/*.js` operates asynchronously over the WebView2 `WebMessageReceived` channel.

### 3.1 Frontend to Backend Commands
JavaScript sends JSON messages via `window.bridge.send(action, payload)` or `window.securityApi.*`:
- `get-stats`: Fetches total counts, threat tallies, and categorized emails (`{ full, stats, counts }`).
- `get-config`: Retrieves current scan configuration, engine weights, thresholds, and startup status.
- `set-enabled`: Toggles real-time mailbox sentinel monitoring on/off.
- `start-full-scan`: Dispatches background sweep of recent/historical emails.
- `set-rubrics`: Updates scoring weights, engine toggles, and threshold percentages.
- `quarantine-item`: Dispatches MAPI move to Quarantine folder.
- `release-item`: Restores quarantined email back to Inbox.
- `delete-item`: Soft/hard deletes targeted email records.
- `scan-duplicates`: Triggers on-demand mailbox duplicate audit.
- `clean-duplicates`: Deletes selected duplicate copies.
- `cloud-scan`: Executes on-demand hash threat lookup (DNS / VirusTotal).
- `save-config`: Persists updated scan parameters to local storage.

### 3.2 Backend to Frontend Event Streams
Backend pushes real-time telemetry via `DashboardWindow.PostEvent(eventName, data)`:
- `live-log`: Streaming diagnostic lines to the live activity console.
- `outlook-scan-update`: Fires whenever an incoming or crawled email is analyzed.
- `outlook-status`: Notifies frontend of Outlook process connection or disconnection.
- `stats-update`: Real-time aggregate count refresh.

---

## 4. COM Threading & Message Pump Notes
- The process explicitly invokes `OleInitialize` at entry point (`Program.Main`) to guarantee STA apartment mode before any Windows Shell or COM APIs are invoked, avoiding `RPC_E_CHANGED_MODE` (`0x80010106`).
- The WebView2 control strictly requires an STA thread with an active Windows message pump during initialization (`WaitForTaskWithPump`) and message handling.
- `Win32SynchronizationContext` marshals asynchronous IPC continuations back to the UI thread via `WM_APP_DISPATCH` (`WM_USER + 102`) to guarantee thread affinity for `PostWebMessageAsJson`.
- `DashboardWindow` runs on the application's primary STA thread.
- Long-running MAPI scans or cloud threat lookups are never invoked directly on the UI thread; they are dispatched to background tasks or the `OutlookStaCoordinator`. Database queries in IPC handlers are offloaded via `Task.Run`.
