# DeskGuard for Microsoft Outlook - Changelog & Change Staging

All notable changes to DeskGuard are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

> **CRITICAL RULE FOR AI AGENTS & CONTRIBUTORS:**
> Every proposed or executed change MUST be staged under `## [Unreleased]` below.
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
