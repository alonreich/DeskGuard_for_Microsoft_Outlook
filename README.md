# DeskGuard for Microsoft Outlook

<div align="center">

![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?style=flat-square&logo=windows)
![.NET 9](https://img.shields.io/badge/.NET-9.0%20Standalone-512BD4?style=flat-square&logo=dotnet)
![C# 13](https://img.shields.io/badge/C%23-13.0-239120?style=flat-square&logo=csharp)
![Client](https://img.shields.io/badge/Outlook-2016%20%7C%202019%20%7C%202021%20%7C%20365-0078D4?style=flat-square&logo=microsoftoutlook)
![Storage](https://img.shields.io/badge/Storage-SQLite%20(WAL)-003B57?style=flat-square&logo=sqlite)

**High-performance, zero-runtime sentinel & intelligent mailbox deduplicator for Microsoft Outlook desktop.**

<p align="center">
  <a href="https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook/releases/latest/download/DeskGuard.exe">
    <img src="https://img.shields.io/badge/%E2%AC%87%EF%B8%8F%20Direct%20Download-DeskGuard.exe%20(Latest%20Installer)-0078D4?style=for-the-badge&logo=windows&logoColor=white" alt="Download Latest Release" />
  </a>
</p>

[📥 Download Latest Installer](https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook/releases/latest/download/DeskGuard.exe) • [All Releases](https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook/releases) • [Features](#-key-capabilities) • [Quick Start](#-quick-start) • [Developer Commands](#-developer-commands) • [What's New](docs/change.md)

</div>

---

## ⚡ What is DeskGuard?

**DeskGuard** is an ultra-lightweight Windows desktop companion that lives in your system tray. It attaches directly to Microsoft Outlook via native MAPI COM interop, intercepting threats, decoding phishing links, and eliminating duplicate emails before they clutter your workflow.

Compiled as a **standalone self-contained single-file executable**, DeskGuard starts instantly, requires **no installed .NET runtime**, and runs with a minuscule memory footprint.

---

## 🛡️ Key Capabilities

| Feature | Description |
|---|---|
| **🛡️ Real-Time Inbox Sentinel** | Hooks Outlook's live incoming message stream via an isolated STA message pump without slowing down Outlook. |
| **🧠 Multi-Vector Threat Scoring** | Scores every email for urgency triggers, homograph spoofing, cousin domains, and dangerous attachment types. |
| **🔗 SafeLinks Unwrapper** | Unwraps Microsoft Defender SafeLinks to inspect and evaluate actual destination URLs behind redirectors. |
| **🌐 Zero-API Threat Intel** | Fast Level 1 DNS malware hash lookups via Team Cymru Malware Hash DB, with optional VirusTotal v3 integration. |
| **🧹 Smart Deduplication** | Fingerprints emails across folders to safely identify and declutter identical copies in one click. |
| **💻 Modern Embedded UI** | Sleek dark-mode dashboard powered by Microsoft Edge WebView2 with real-time telemetry and forensics. |

---

## 🚀 Quick Start

### For Users
1. Download **[DeskGuard.exe (Direct Download)](https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook/releases/latest/download/DeskGuard.exe)** (or view all versions on [GitHub Releases](https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook/releases)).
2. Run `DeskGuard.exe` to install.
3. DeskGuard starts automatically in your system tray and safeguards your Outlook inbox.

### For Developers
```cmd
:: 1. Clone repository
git clone https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook.git
cd DeskGuard_for_Microsoft_Outlook

:: 2. Launch live development sandbox
dev.cmd

:: 3. Build production standalone installer
Build.cmd
```

---

## 🛠️ Developer Commands

| Script | Purpose |
|---|---|
| `dev.cmd` | Launches the developer runner with live console logging. |
| `dev_build.cmd` | Fast incremental dev build without wiping compiled outputs. |
| `Build.cmd` | Full production build: executes unit tests, publishes standalone single-file, and Inno Setup installer packaging (`compiled\DeskGuard.exe`). |

---

## 📚 Documentation & AI Agents

DeskGuard uses a **token-saving documentation architecture**:
- **Entry Point / Docs Router**: [`project_structure.txt`](project_structure.txt)
- **Changelog & Staged Changes**: [`docs/change.md`](docs/change.md)
- **Architecture Overview**: [`docs/architecture.md`](docs/architecture.md)
- **Host & Modern UI**: [`docs/app.md`](docs/app.md)
- **Threat Scoring & Heuristics**: [`docs/core.md`](docs/core.md)
- **Outlook MAPI Interop & STA**: [`docs/interop.md`](docs/interop.md)
- **SQLite Persistence & Deduplication**: [`docs/data.md`](docs/data.md)
- **Build Pipeline**: [`docs/build-pipeline.md`](docs/build-pipeline.md)

> **AI Contributor Rule**: All code changes must be staged in [`docs/change.md`](docs/change.md) under `## [Unreleased]`. Never modify code without keeping documentation in sync.

---

## 📄 License
Licensed under the [MIT License](LICENSE).
