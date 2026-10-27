# DeskGuard Build & Release Pipeline

## 1. Overview
The DeskGuard build pipeline orchestrates automated testing, Native AOT compilation via the .NET 9 ILC toolchain, SHA-256 update manifest generation, and single-file Windows installer packaging via Inno Setup 6.

---

## 2. Developer Commands

| Command            | Description                                                                              |
|--------------------|------------------------------------------------------------------------------------------|
| `.\Build.cmd`      | **Production Release**: Runs unit tests, compiles Native AOT, packages `compiled\DeskGuard.exe`. |
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
    |-- Program.cs                   # Master build loop (tests -> publish -> manifest -> Inno)
    |-- Staging.cs                   # Native AOT publish invocation & crash retry handling
    |-- InnoInstaller.cs             # Inno Setup compiler (ISCC.exe) runner
    |-- BuildLog.cs                  # Dual file/console synchronized logger
    `-- Cli.cs                       # Process execution wrapper with streaming stdout/stderr
```

---

## 4. Production Build Lifecycle (`Build.cmd`)

When running `.\Build.cmd`:
1. **Pre-build Process Check**: Closes any existing DeskGuard instances running from this repository.
2. **Unit Test Suite**: Executes `dotnet test DeskGuard.sln -c Release --no-restore`.
3. **Native AOT Staging**: Publishes `DeskGuard.App` to `obj\StandaloneTemp\Staging\` using:
   - Target framework: `net9.0-windows7.0`
   - Runtime identifier: `win-x64`
   - Stripped debug symbols and speed-optimized machine code.
   - Automatic single-threaded ILC fallback if multi-threaded compiler race condition occurs (`dotnet/runtime#108743`).
4. **Manifest Generation**: Generates `update.json` indexing file hashes for auto-updating.
5. **Inno Setup Compilation**: Runs `ISCC.exe` with `DeskGuard.iss` to produce the single-file installer:
   - **Output**: `compiled\DeskGuard.exe` (~5.8 MB)
6. **Artifact Cleanup**: Automatically removes temporary staging directories, leaving only the verified installer in `compiled\`.

---

## 5. Public Release & Staging Workflow (For AI Agents & Maintainers)

Before running `.\Build.cmd` to publish a new release:
1. Ensure all code changes are staged in `docs/change.md` under `## [Unreleased]`.
2. Verify module documentation is up to date in `docs/*.md`.
3. Promote `## [Unreleased]` in `docs/change.md` to `## [<version>] - <date>`.
4. Run `.\Build.cmd`.
5. Verify test pass and successful creation of `compiled\DeskGuard.exe`.
6. Commit changes to Git with release message.
