# DeskGuard.Core - Threat Scoring, Heuristics & Models

## 1. Module Overview
`DeskGuard.Core` contains all business logic independent of UI and COM interop:
- Heuristic phishing analysis and multi-factor penalty scoring.
- Microsoft SafeLinks URL unwrapper.
- Domain spoofing, cousin-domain, and homograph detection.
- Hybrid threat intelligence (DNS-based Team Cymru Malware Hash DB + VirusTotal v3).
- Source-generated JSON serialization context compatible with Native AOT trimming.

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.Core/
|-- Concurrency/
|   |-- JobPriority.cs               # Priority levels (High, Normal, Low, Background)
|   `-- PriorityJobQueue.cs          # Lock-free prioritized asynchronous queue with signals
|-- Interfaces/
|   |-- IOutlookMapiService.cs       # Abstraction for Outlook MAPI operations
|   `-- IVaultRepository.cs          # Abstraction for SQLite persistence and queries
|-- Logging/
|   `-- AppLogger.cs                 # Centralized thread-safe file logger (%LocalAppData%\DeskGuard\logs)
|-- Models/
|   |-- EmailRecord.cs               # Canonical email entity with score, headers, fingerprint
|   |-- SecurityVerdict.cs           # Enum: Safe, Suspicious, Malicious, Whitelisted
|   |-- SecurityPenalty.cs           # Individual scoring deduction record (code, points, reason)
|   |-- ScanConfiguration.cs         # Heuristic thresholds, API keys, auto-quarantine rules
|   |-- DuplicateGroup.cs            # Grouping of duplicate emails sharing identity hash
|   |-- DuplicateItem.cs             # Single instance within a duplicate cluster
|   |-- SearchParameters.cs          # Query filter criteria for vault lookups
|   |-- SecurityEvent.cs             # Audit log telemetry model
|   `-- EngineStats.cs               # Aggregate counters (scanned, safe, quarantined, etc.)
|-- Scoring/
|   |-- ScoringEngine.cs             # Master heuristic scoring logic and penalty rules
|   |-- SafeLinksDecoder.cs          # Unwraps Microsoft Defender SafeLinks URLs
|   |-- RegisteredDomainParser.cs    # TLD / effective second-level domain extraction
|   |-- StringDistance.cs            # Levenshtein / Jaro-Winkler brand similarity scoring
|   `-- EmlParser.cs                 # MIME header parser and attachment extractor
|-- Serialization/
|   `-- DeskGuardJsonContext.cs      # Native AOT System.Text.Json source generator context
`-- Services/
    `-- CloudThreatService.cs        # DNS-based malware hash lookup (Zero-API) & VirusTotal
```

---

## 3. Threat Scoring Logic (`ScoringEngine.cs`)
DeskGuard calculates a risk score from `0.0` (Completely Safe) to `100.0+` (Severe Hazard):

| Trigger / Condition                                 | Penalty Score | Description                                              |
|-----------------------------------------------------|---------------|----------------------------------------------------------|
| **High-Risk Attachment Extension** (`.exe`, `.scr`)| +50.0         | Direct executable or installer attached                  |
| **Dangerous Macro/Script** (`.vbs`, `.js`, `.xlsm`)| +35.0         | Scriptable payload capable of remote execution           |
| **Cousin / Spoofed Domain**                         | +30.0 - 45.0  | Brand typo-squatting (e.g. `micros0ft.com`, `paypa1.com`)|
| **Sender / Reply-To Mismatch**                      | +25.0         | Hidden redirection of responses                          |
| **Urgent Call to Action**                           | +15.0 - 25.0  | "Account suspended", "wire immediately", "password reset"|
| **Punycode / Homograph Cyrillic Char**              | +35.0         | Lookalike unicode characters mimicking ASCII domains     |
| **DNS Malware Hash Hit (Team Cymru)**               | +60.0         | Verified malicious attachment hash in global database     |

### Verdict Classification:
- **`0.0 - 29.9`**: Safe (Green)
- **`30.0 - 64.9`**: Suspicious (Yellow)
- **`65.0+`**: Malicious / Quarantined (Red)

---

## 4. Microsoft SafeLinks Decoding (`SafeLinksDecoder.cs`)
Outlook frequently wraps links in corporate environments:
`https://eur01.safelinks.protection.outlook.com/?url=https%3A%2F%2Fbadsite.com%2F...`
`SafeLinksDecoder` extracts the target URL before evaluating domain reputation, preventing attackers from bypassing heuristics via Microsoft redirectors.

---

## 5. Native AOT Compliance Note
Do NOT use `JsonSerializer.Deserialize<T>(...)` with un-annotated types. Always pass `DeskGuardJsonContext.Default.<Type>` to ensure trimming safety during Native AOT builds.
