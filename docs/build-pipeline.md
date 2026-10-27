# DeskGuard Build & Release Pipeline

## 1. Overview
The DeskGuard build pipeline orchestrates automated testing, self-contained single-file compilation, SHA-256 update manifest generation, and single-file Windows installer packaging via Inno Setup 6.

---

## 2. Developer Commands

| Command            | Description                                                                              |
|--------------------|------------------------------------------------------------------------------------------|
| `.\Build.cmd`      | **Production Release**: Runs unit tests, publishes standalone single-file, packages `compiled\DeskGuard.exe`. |
| `.\dev.cmd`        | **Local Dev Loop**: Launches an isolated developer sandbox run with debug logging.        |
| `.\dev_build.cmd`  | **Fast Dev Build**: Incremental compilation without cleaning the output directory.       |

---

## 3. Build Orchestrator (`build/DeskGuard.Build`)

```
build/
|-- DevRun.ps1                       # Helper script invoked by dev.cmd
|-- DevSandbox.ps1                   # Sandboxed environment preparation script
|-- installer/
|   `-- DeskGuard.iss                # Inno Setup 6 installation script
`-- DeskGuard.Build/                 # .NET 9 build utility project
    |-- Program.cs                   # Master build loop (tests -> publish -> manifest -> Inno -> git release)
    |-- Staging.cs                   # Standalone publish invocation & manifest indexing
    |-- InnoInstaller.cs             # Inno Setup compiler (ISCC.exe) runner
    |-- GitRelease.cs                # Automated git commit, tag, push & GitHub release via gh CLI
    |-- BuildLog.cs                  # Dual file/console synchronized logger
    `-- Cli.cs                       # Process execution wrapper with streaming stdout/stderr
```

---

## 4. Production Build Lifecycle (`Build.cmd`)

When running `.\Build.cmd`:
1. **Pre-build Process Check**: Closes any existing DeskGuard instances running from this repository.
2. **Unit Test Suite**: Executes `dotnet test DeskGuard.sln -c Release --no-restore`.
3. **Standalone Staging**: Publishes `DeskGuard.App` to `obj\StandaloneTemp\Staging\` using:
   - Target framework: `net9.0-windows7.0`
   - Runtime identifier: `win-x64`
   - Self-contained single-file bundle with built-in COM interop enabled.
4. **Manifest Generation**: Generates `update.json` indexing file hashes for auto-updating.
5. **Inno Setup Compilation**: Runs `ISCC.exe` with `DeskGuard.iss` to produce the single-file installer:
   - **Output**: `compiled\DeskGuard.exe` (~5.8 MB)
6. **Artifact Cleanup**: Automatically removes temporary staging directories, leaving only the verified installer in `compiled\`.
7. **Automated Git & GitHub Release**: Automatically promotes `## [Unreleased]` in `docs/change.md` to the new version header, creates an annotated git tag (`v<version>`), pushes to `origin main` and the tag, and creates/updates the public release on GitHub with `compiled\DeskGuard.exe` as the release asset using GitHub CLI (`gh release create --latest`).

---

## 5. Public Release & Staging Workflow (For AI Agents & Maintainers)

1. Ensure all code changes are staged in `docs/change.md` under `## [Unreleased]`.
2. Verify module documentation is up to date in `docs/*.md`.
3. Run `.\Build.cmd`. It automatically tests, compiles, packages the installer, promotes the changelog, and publishes the release to Git and GitHub.
4. The direct download URL (`https://github.com/alonreich/DeskGuard_for_Microsoft_Outlook/releases/latest/download/DeskGuard.exe`) immediately serves the latest published installer.
