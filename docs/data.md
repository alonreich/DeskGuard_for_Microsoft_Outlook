# DeskGuard.Data - SQLite Persistence, Migrations & Deduplication

## 1. Module Overview
`DeskGuard.Data` handles all persistent storage, historical query indexes, and duplicate detection records using an embedded SQLite database.

Key attributes:
- **Write-Ahead Logging (WAL)**: Allows simultaneous reader connections while the crawler writes new entries.
- **Embedded Schema Migrations**: Safe, versioned migrations that automatically upgrade user databases across versions without data loss.
- **Deduplication Engine**: Fingerprint hashing and multi-factor matching for detecting duplicate emails across folders and archives.

---

## 2. File Minimap & Responsibilities

```
src/DeskGuard.Data/
|-- VaultRepository.cs               # Core CRUD, statistics aggregation, connection lifecycle
|-- VaultRepository.Duplicates.cs    # Duplicate detection algorithms, group collation, cleanup
|-- VaultRepository.Reconciliation.cs# Syncs local records when emails are moved/deleted in Outlook
|-- VaultRepository.Search.cs        # FTS5 full-text search with inline syntax operators & phrase support
|-- Migrations/
|   `-- VaultMigrationManager.cs     # Step-wise schema migration runner with PRAGMA user_version (V3: AV scan status)
`-- Sqlite/
    `-- SqliteConnectionFactory.cs   # Factory configuring WAL mode, synchronous=NORMAL, timeouts
```

---

## 3. Storage Location & Database Configuration
- **Database Path**: `%LocalAppData%\DeskGuard\data\vault.db`
- **Configured PRAGMAs**:
  ```sql
  PRAGMA journal_mode = WAL;
  PRAGMA synchronous = NORMAL;
  PRAGMA foreign_keys = ON;
  PRAGMA temp_store = MEMORY;
  PRAGMA busy_timeout = 5000;
  ```
- **Decoupled Connection Architecture**:
  - `_connection`: Dedicated write connection used for schema migrations and mutating operations (`InsertEmail`, `InsertIndexed`, `AddProcessed`, `NuclearReset`, `SaveDuplicates`, `ReconcileFolder`), synchronized via `_writeSyncRoot`.
  - `_readConnection`: Dedicated read-only connection used for querying (`SearchEmails`, `GetStats`, `FindDuplicates`, `GetEvents`), synchronized via `_readSyncRoot`.
  - Under SQLite WAL mode, readers and writers operate concurrently without lock contention or `SQLITE_BUSY` blocking.

---

## 4. Deduplication Engine (`VaultRepository.Duplicates.cs`)

### 4.1 Fingerprinting Algorithm
Emails are fingerprinted using a composite hash:
`SHA-256(Normalized_Sender + Normalized_Subject + SentUtc_Timestamp + BodyText_Snippet + AttachmentCount)`

### 4.2 Duplicate Classification & Survivor Protection
When duplicate fingerprints are detected across mailbox folders:
- **Folder Priority Hierarchy**: The surviving primary copy is elected based on deterministic folder importance:
  1. `olFolderInbox` (Rank 1 - Highest priority, default surviving copy)
  2. `olFolderSentMail` (Rank 2)
  3. User Folders & PST Archive Folders (Rank 3/4)
  4. `olFolderJunk` (Rank 10)
  5. `\SUMC ISSUES\CONFLICTS` and Sync Issues (Rank 20)
  6. `olFolderDeletedItems` (Rank 99 - Lowest priority)
- **Tie-Breaking Rule**: If duplicates reside in the same folder tier, the oldest received item (`SentUtc`) is elected as the primary survivor.
- **Survivor Protection Invariant**: The elected survivor copy is permanently marked as protected (`🛡️ SOLE SURVIVOR`). It cannot be checked or targeted by batch selection ("Select All"), guaranteeing that at least one authentic copy is preserved without accidental data loss.
- **Redundant Copies**: All non-surviving copies are presented with individual selection checkboxes and direct "Open in Outlook" jump buttons.

### 4.3 Safe Deletion Modes
- **`safe` (Recommended)**: Moves duplicate items to Outlook's "Deleted Items" folder so the action can be undone by the user.
- **`permanent`**: Hard delete via MAPI permanent deletion flags.

---

## 5. Schema Migration Protocol (`VaultMigrationManager.cs`)
When altering database tables:
1. Increment the target version integer in `VaultMigrationManager.cs`.
2. Add a migration step checking `PRAGMA user_version`.
3. Wrap all DDL statements in transactions.
4. Update `PRAGMA user_version = <new_version>;`.

- **Migration V2**: Configured `emails_fts` FTS5 virtual table and synchronization triggers (`emails_ai`, `emails_ad`, `emails_au`).
- **Migration V3**: Added local antivirus tracking columns (`av_scanned`, `av_verdict`, `av_threat_name`, `av_scanned_at`) and partial index `idx_emails_av_scanned` on `emails(av_scanned)` for high-speed background crawl lookups.

---

## 6. Full-Text Search (FTS5) & Search Syntax Engine (`VaultRepository.Search.cs`)

### 6.1 Advanced Inline Search Syntax Operators
The search engine parses inline operator tokens from the user's query and converts them into SQL constraints:
- `from:<sender>`: Filter sender address or domain.
- `to:<recipient>`: Filter recipient address.
- `subject:<text>`: Filter subject line.
- `folder:<name>`: Filter Outlook folder name.
- `has:attach` / `has:attachment`: Require email to contain attachments.
- `has:noattach`: Require email to have no attachments.
- `score:<op><val>`: Filter by safety score (supports `<`, `<=`, `>`, `>=`, `=`).
- `is:unread` / `is:read`: Filter read status.
- `is:malicious` / `is:spam` / `is:suspicious` / `is:clean`: Filter verdict categories.
- `after:<date>` / `since:<date>`: Received on or after `YYYY-MM-DD`.
- `before:<date>` / `until:<date>`: Received on or before `YYYY-MM-DD`.

### 6.2 Tokenization & Phrase Matching
- Quoted multi-word strings (e.g. `"wire transfer"`, `"project titan"`) are tokenized as exact phrase units in SQLite FTS5 rather than broken individual words.
- Operator-only queries (e.g. `is:malicious has:attach`) skip FTS parsing entirely, executing direct indexed SQLite queries with maximum speed.

### 6.3 Local Antivirus Tracking
- `RecordAvScanResult(entryId, verdict, threatName)`: Records scan completion and marks `av_scanned = 1`.
- `GetUnscannedAvEmails(limit)`: Retrieves pending historical emails with `av_scanned = 0` for background progressive scanning.
