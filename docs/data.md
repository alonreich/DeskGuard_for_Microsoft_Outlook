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
|-- VaultRepository.Search.cs        # High-performance search queries with filters & sorting
|-- Migrations/
|   `-- VaultMigrationManager.cs     # Step-wise schema migration runner with PRAGMA user_version
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

### 4.2 Duplicate Classification
When duplicate fingerprints are detected:
- **Primary / Master Copy**: Oldest received item or item residing in a prioritized folder (e.g. Inbox / Archive over Trash / Junk).
- **Secondary / Redundant Copies**: Flagged for safe one-click removal.

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
