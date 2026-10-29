# DeskGuard.Core - Threat Scoring, Heuristics & Models

## 1. Module Overview
`DeskGuard.Core` contains all business logic independent of UI and COM interop:
- Heuristic phishing analysis, domain parsing, and multi-engine penalty deduction scoring.
- Multi-piece email disassembly and local antivirus inspection (`LocalAntivirusService`) via Microsoft Defender (`MpCmdRun.exe`) and ClamAV (`clamscan.exe`).
- Microsoft SafeLinks URL unwrapper (`SafeLinksDecoder`).
- Domain spoofing, cousin-domain, lookalike typosquatting, and homograph detection.
- Hybrid threat intelligence (DNS-based Team Cymru Malware Hash DB + VirusTotal v3 REST API).
- Centralized thread-safe diagnostic file logger (`AppLogger`).
- Source-generated JSON serialization context compatible with Native AOT trimming (`DeskGuardJsonContext`).

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.Core/
|-- Concurrency/
|   |-- JobPriority.cs               # Priority levels (P0=Realtime, P1=User, P2=Batch, P3=Idle)
|   `-- PriorityJobQueue.cs          # Lock-free prioritized asynchronous queue with signals
|-- Interfaces/
|   |-- IOutlookMapiService.cs       # Abstraction for Outlook MAPI operations
|   `-- IVaultRepository.cs          # Abstraction for SQLite persistence and queries
|-- Logging/
|   `-- AppLogger.cs                 # Centralized thread-safe file logger (%LocalAppData%\DeskGuard\logs\deskguard.log)
|-- Models/
|   |-- EmailRecord.cs               # Canonical email entity with score, headers, fingerprint
|   |-- SecurityVerdict.cs           # Verdict model: Score (0-100), Verdict, Penalties, TotalDeduction
|   |-- SecurityPenalty.cs           # Individual scoring deduction record (rule, key, points, evidence)
|   |-- ScanConfiguration.cs         # DPAPI-encrypted keys, engine weights, thresholds, whitelists, themes
|   |-- DuplicateGroup.cs            # Grouping of duplicate emails sharing identity hash
|   |-- DuplicateItem.cs             # Single instance within a duplicate cluster
|   |-- SearchParameters.cs          # Query filter criteria for vault lookups
|   |-- SecurityEvent.cs             # Audit log telemetry model
|   `-- EngineStats.cs               # Aggregate counters (scanned, safe, quarantined, etc.)
|-- Scoring/
|   |-- ScoringEngine.cs             # 16-engine heuristic scoring logic and penalty deductions
|   |-- SafeLinksDecoder.cs          # Unwraps Microsoft Defender SafeLinks URLs
|   |-- RegisteredDomainParser.cs    # TLD / effective second-level domain extraction
|   |-- StringDistance.cs            # Levenshtein / Jaro-Winkler brand similarity scoring
|   `-- EmlParser.cs                 # MIME header parser and attachment extractor
|-- Serialization/
|   `-- DeskGuardJsonContext.cs      # Native AOT System.Text.Json source generator context
`-- Services/
    |-- CloudThreatService.cs        # DNS-based malware hash lookup (Zero-API) & VirusTotal
    `-- LocalAntivirusService.cs     # Multi-piece email disassembly & local AV scanning (Defender & ClamAV)
```

---

## 3. Threat Scoring Logic (`ScoringEngine.cs`)
DeskGuard calculates a **safety score from 100.0% down to 0.0%**:
- **Baseline**: Every evaluated email starts with a pristine score of **100.0%** (Clean / Safe).
- **Deductions**: Each failed verification check deducts points based on its configured engine weight.
- **Score Calculation**:
  $$\text{Score} = \max(0.0, \min(100.0, \text{round}(100.0 - \text{TotalDeduction})))$$
- **Dangerous Attachment Override**: If a high-risk executable or script attachment is detected (`DANGEROUS_ATTACHMENT`), the score is immediately capped at a maximum of **10.0%**, guaranteeing a Malicious verdict regardless of SPF/DKIM authentication.

### 3.1 The 16 Detection Engines & Default Weights (100 Points Total)

| Engine Key | Engine Description | Default Weight | Deduction Trigger & Behavior |
| :--- | :--- | :---: | :--- |
| `spf` | **SPF Authorization** | **10 pts** | Full weight on `fail` / `softfail`; half weight on `none`. |
| `dmarc` | **DMARC Authentication** | **10 pts** | Full weight on policy `fail`; half weight when domain publishes no policy (`none`). |
| `dkim` | **DKIM Cryptographic Seal** | **5 pts** | Full weight when DKIM signature is missing, broken, or fails verification. |
| `alignment` | **Sender Envelope Alignment** | **5 pts** | Full weight if From header domain does not match Return-Path bounce domain. |
| `rdns` | **Reverse DNS (PTR) Verification** | **4 pts** | Full weight if connecting mail server lacks valid reverse DNS or appears dynamic. |
| `rbl` | **Global Blacklists (RBL)** | **10 pts** | Full weight if sending IP is listed on Spamhaus, SpamCop, or Barracuda. |
| `body` | **Hidden Text & Body Entropy Shield** | **5 pts** | Full weight if invisible fonts, hidden HTML divs, or zero-pixel text tricks are detected. |
| `heuristics` | **Smart Scam & Spam Keywords** | **6 pts** | Deducts 1/3 weight per matched phishing/scam keyword (full deduction at 3+ keywords). |
| `spamflag` | **Upstream Server Spam Flag** | **4 pts** | Full weight if mail hop headers contain `X-Spam-Flag: YES` or Forefront spam marks. |
| `mime` | **Dangerous MIME Attachments** | **6 pts** | Flags `.exe`, `.js`, `.vbs`, `.bat`, `.iso`, `.scr`, `.lnk` (caps final score at <= 10%). |
| `msscl` | **Microsoft Spam Score (SCL/BCL)** | **10 pts** | Full weight at SCL 7-9; 70% at SCL 5-6; 50% at Bulk Complaint Level (BCL) 7+. |
| `linkmismatch`| **Fake Links (SafeLinks Mismatch)** | **8 pts** | 60% weight for 1 deceptive link, 100% for 2+ links where displayed text differs from destination. |
| `displayspoof`| **Display Name Brand Spoofing** | **7 pts** | Full weight if display name mimics trusted brands (PayPal, Microsoft) but sender domain is unrelated. |
| `lookalike` | **Look-Alike / Typosquatting Domain**| **5 pts** | Full weight if sender domain is 1-2 edit distance characters from known enterprise brands. |
| `replyto` | **Replies Go Elsewhere (Reply-To)** | **3 pts** | Full weight if replies redirect to free webmail (Gmail/Outlook.com), half for separate domain. |
| `firstcontact`| **First-Time Sender Domain** | **2 pts** | Small deduction if recipient has never received mail from this sender domain in search vault. |

### 3.2 Verdict Thresholds

| Score Range | Verdict | UI Status & Styling | Action Taken |
| :--- | :--- | :--- | :--- |
| **0.0% – 20.0%** | **Malicious** | Danger (Red) | Automatically isolated to **DeskGuard Quarantine** (or Junk if configured). |
| **20.1% – 40.0%** | **Spam** | Warning (Orange) | Moved to **Junk Email** folder (or Quarantine). |
| **40.1% – 55.0%** | **Suspicious**| Attention (Yellow) | Retained in **Inbox**; tagged with Outlook color category `DeskGuard: Suspicious`. |
| **55.1% – 100.0%**| **Safe** | Clean (Green) | Pristine; untouched in original folder. |

### 3.3 Configuration Security & Sensitive Data Storage (`ScanConfiguration.cs`)
- **DPAPI Encryption**: Sensitive keys (such as VirusTotal API keys) are encrypted at rest using Windows Data Protection API (`CryptProtectData` / `CryptUnprotectData`).
- **Sanitized Export**: `ScanConfiguration.CreateExportPackage()` redacts all secrets and host-specific window coordinates prior to exporting backup files.
- **Safe Import Merge**: `ScanConfiguration.ImportSettings()` safely merges imported engine weights and thresholds without overwriting master power state or machine-specific coordinates.
- **Scan Scope Control**: Supports `HistoryScanScope` (`"unread"` vs `"all"`) to target only actionable messages during background sweeps.
- **Deep Local AV Flag (`DeepLocalAvScanAll`)**: Boolean configuration enabling mandatory dual-engine local antivirus scanning across all emails without exception.
- **Engine Weights Normalization**: Default engine weights map from `ScoringEngine.DefaultWeights` guaranteeing a complete 100% allocation across all heuristic inspection rules.

---

## 4. Microsoft SafeLinks Decoding (`SafeLinksDecoder.cs`)
Outlook frequently wraps links in corporate environments:
`https://eur01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fbadsite.com%2F...`
`SafeLinksDecoder` unwraps and extracts the true target URL before evaluating domain reputation, ensuring attackers cannot bypass heuristic inspections via Microsoft redirectors.

---

## 5. Local Antivirus Disassembly & Multi-Engine Inspection (`LocalAntivirusService.cs`)
For emails requiring deep malware inspection (when `DeepLocalAvScanAll` is active or safety score is below the safe threshold):
- **Email Disassembly**: The service extracts the email into isolated sandboxed temporary artifacts:
  1. `headers.txt`: Raw transport headers.
  2. `body.html`: Rendered message HTML and plaintext body.
  3. Individual Attachments: Extracted and scanned as standalone files, completely isolated from each other.
- **Dual Local Engine Execution**:
  1. **Microsoft Defender**: Invokes `MpCmdRun.exe -Scan -ScanType 3 -File "<path>"` against candidate locations in ProgramData and Program Files. Exit code 0 indicates clean; exit code 2 indicates threat identified.
  2. **ClamAV (Optional/Local)**: Invokes `clamscan.exe --no-summary "<path>"` if installed or available on PATH.
- **Event Streaming**: Dispatches `ProgressChanged` events reporting current step, total steps, active piece, and scanning engine.
- **Threat Escalation**: If any piece is infected, `OverallVerdict` becomes `"Malicious"`, assigns a 100-point penalty under rule `LOCAL_AV`, records the threat name, and moves the email to Quarantine.

---

## 6. Diagnostic Logging (`AppLogger.cs`)
All engine decisions, scoring breakdowns, COM activations, and thread operations are persisted by `AppLogger` to `%LOCALAPPDATA%\DeskGuard\logs\deskguard.log` with rolling retention, thread IDs, and timestamp precision.

---

## 7. Native AOT & Trimming Compliance
Do NOT use `JsonSerializer.Deserialize<T>(...)` with un-annotated types. Always pass `DeskGuardJsonContext.Default.<Type>` to ensure trimming safety during Native AOT and single-file compilation.
