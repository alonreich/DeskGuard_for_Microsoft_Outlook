namespace DeskGuard.Data.Migrations;

using System;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public static class VaultMigrationManager
{
    public static void InitializeAndMigrate(SqliteConnection connection, string dbPath)
    {
        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS emails (
                    id TEXT PRIMARY KEY,
                    entry_id TEXT NOT NULL UNIQUE,
                    store_id TEXT,
                    fingerprint TEXT,
                    subject TEXT,
                    sender TEXT,
                    recipient TEXT,
                    cc TEXT,
                    date TEXT,
                    time TEXT,
                    timestamp TEXT,
                    body TEXT,
                    headers TEXT,
                    verdict TEXT,
                    score REAL DEFAULT 100,
                    tier TEXT,
                    attachments_json TEXT,
                    attachments TEXT,
                    unread INTEGER DEFAULT 0,
                    user_moved INTEGER DEFAULT 0,
                    current_folder TEXT,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE VIRTUAL TABLE IF NOT EXISTS emails_fts USING fts5(
                    subject,
                    body,
                    sender,
                    recipient,
                    attachments,
                    content='emails',
                    content_rowid='rowid'
                );

                CREATE VIRTUAL TABLE IF NOT EXISTS emails_vocab USING fts5vocab(emails_fts, 'row');

                CREATE TABLE IF NOT EXISTS processed (
                    key TEXT PRIMARY KEY,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS duplicate_results (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    status TEXT,
                    saved_at TEXT,
                    items_json TEXT
                );

                CREATE TABLE IF NOT EXISTS protected (
                    fingerprint TEXT PRIMARY KEY,
                    kind TEXT NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS events (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts DATETIME DEFAULT CURRENT_TIMESTAMP,
                    kind TEXT NOT NULL,
                    message_key TEXT,
                    entry_id TEXT,
                    subject TEXT,
                    sender TEXT,
                    from_folder TEXT,
                    to_folder TEXT,
                    score REAL,
                    reasons_json TEXT
                );

                CREATE INDEX IF NOT EXISTS idx_events_ts ON events(ts DESC);
                CREATE INDEX IF NOT EXISTS idx_events_key ON events(message_key);
            """;
            cmd.ExecuteNonQuery();
        }

        // Add optional/new columns if they don't exist
        string[] optionalColumns =
        [
            "ALTER TABLE emails ADD COLUMN user_moved INTEGER DEFAULT 0;",
            "ALTER TABLE emails ADD COLUMN penalties_json TEXT;",
            "ALTER TABLE emails ADD COLUMN current_folder TEXT;",
            "ALTER TABLE emails ADD COLUMN message_id TEXT;",
            "ALTER TABLE emails ADD COLUMN hidden INTEGER DEFAULT 0;",
            "ALTER TABLE emails ADD COLUMN attachments TEXT;",
            "ALTER TABLE emails ADD COLUMN folder_kind TEXT;",
            "ALTER TABLE emails ADD COLUMN size INTEGER DEFAULT 0;"
        ];

        foreach (string alter in optionalColumns)
        {
            try
            {
                using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = alter;
                cmd.ExecuteNonQuery();
            }
            catch
            {
                // Column already exists, ignore
            }
        }

        MigrateSearchIndex(connection);
        MigrateToLocations(connection, dbPath);
    }

    private static void MigrateSearchIndex(SqliteConnection connection)
    {
        long version = GetUserVersion(connection);
        if (version < 2)
        {
            using (SqliteCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    DROP TRIGGER IF EXISTS emails_ai;
                    DROP TRIGGER IF EXISTS emails_ad;
                    DROP TRIGGER IF EXISTS emails_au;
                """;
                cmd.ExecuteNonQuery();
            }

            // Populate attachments plain text from attachments_json for full-text search
            List<(long RowId, string? Json)> rows = [];
            using (SqliteCommand selectCmd = connection.CreateCommand())
            {
                selectCmd.CommandText = "SELECT rowid, attachments_json FROM emails WHERE attachments IS NULL";
                using SqliteDataReader reader = selectCmd.ExecuteReader();
                while (reader.Read())
                {
                    long rowid = reader.GetInt64(0);
                    string? json = reader.IsDBNull(1) ? null : reader.GetString(1);
                    rows.Add((rowid, json));
                }
            }

            using (SqliteTransaction tx = connection.BeginTransaction())
            {
                using SqliteCommand updCmd = connection.CreateCommand();
                updCmd.Transaction = tx;
                updCmd.CommandText = "UPDATE emails SET attachments = $names WHERE rowid = $rowid";
                var pNames = updCmd.Parameters.Add("$names", SqliteType.Text);
                var pRowid = updCmd.Parameters.Add("$rowid", SqliteType.Integer);

                foreach (var (rowid, json) in rows)
                {
                    pNames.Value = ExtractAttachmentNames(json);
                    pRowid.Value = rowid;
                    updCmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
        }

        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE TRIGGER IF NOT EXISTS emails_ai AFTER INSERT ON emails BEGIN
                    INSERT INTO emails_fts(rowid, subject, body, sender, recipient, attachments)
                    VALUES (new.rowid, new.subject, new.body, new.sender, new.recipient, new.attachments);
                END;

                CREATE TRIGGER IF NOT EXISTS emails_ad AFTER DELETE ON emails BEGIN
                    INSERT INTO emails_fts(emails_fts, rowid, subject, body, sender, recipient, attachments)
                    VALUES ('delete', old.rowid, old.subject, old.body, old.sender, old.recipient, old.attachments);
                END;

                CREATE TRIGGER IF NOT EXISTS emails_au AFTER UPDATE ON emails BEGIN
                    INSERT INTO emails_fts(emails_fts, rowid, subject, body, sender, recipient, attachments)
                    VALUES ('delete', old.rowid, old.subject, old.body, old.sender, old.recipient, old.attachments);
                    INSERT INTO emails_fts(rowid, subject, body, sender, recipient, attachments)
                    VALUES (new.rowid, new.subject, new.body, new.sender, new.recipient, new.attachments);
                END;
            """;
            cmd.ExecuteNonQuery();
        }

        if (version < 2)
        {
            using SqliteCommand cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO emails_fts(emails_fts) VALUES('rebuild'); PRAGMA user_version = 2;";
            cmd.ExecuteNonQuery();
        }
    }

    private static void MigrateToLocations(SqliteConnection connection, string dbPath)
    {
        if (GetUserVersion(connection) >= 3) return;

        string[] extraCols = ["ALTER TABLE emails ADD COLUMN cc TEXT;", "ALTER TABLE emails ADD COLUMN headers TEXT;", "ALTER TABLE emails ADD COLUMN attachments_json TEXT;"];
        foreach (string alter in extraCols)
        {
            try
            {
                using SqliteCommand cmd = connection.CreateCommand();
                cmd.CommandText = alter;
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        bool hasUniqueFp = false;
        using (SqliteCommand indexCmd = connection.CreateCommand())
        {
            indexCmd.CommandText = "PRAGMA index_list('emails')";
            using SqliteDataReader reader = indexCmd.ExecuteReader();
            while (reader.Read())
            {
                bool isUnique = reader.GetInt32(2) == 1;
                string indexName = reader.GetString(1);
                if (isUnique)
                {
                    using SqliteCommand infoCmd = connection.CreateCommand();
                    infoCmd.CommandText = $"PRAGMA index_info('{indexName.Replace("'", "''")}')";
                    using SqliteDataReader infoReader = infoCmd.ExecuteReader();
                    while (infoReader.Read())
                    {
                        if (infoReader.GetString(2) == "fingerprint")
                        {
                            hasUniqueFp = true;
                            break;
                        }
                    }
                }
                if (hasUniqueFp) break;
            }
        }

        if (hasUniqueFp)
        {
            string backupPath = dbPath + ".v2.bak";
            if (!File.Exists(backupPath))
            {
                try
                {
                    using SqliteCommand vacCmd = connection.CreateCommand();
                    vacCmd.CommandText = $"VACUUM INTO '{backupPath.Replace("'", "''")}'";
                    vacCmd.ExecuteNonQuery();
                }
                catch { }
            }

            List<string> cols = [];
            using (SqliteCommand tableInfoCmd = connection.CreateCommand())
            {
                tableInfoCmd.CommandText = "PRAGMA table_info('emails')";
                using SqliteDataReader r = tableInfoCmd.ExecuteReader();
                while (r.Read()) cols.Add(r.GetString(1));
            }

            string keyExpr = cols.Contains("message_id")
                ? "CASE WHEN COALESCE(message_id, '') <> '' THEN 'mid:' || message_id ELSE fingerprint END"
                : "fingerprint";

            List<string> selectCols = [];
            foreach (string c in cols)
            {
                selectCols.Add(c == "fingerprint" ? $"{keyExpr} AS fingerprint" : c);
            }

            string? createSql = null;
            using (SqliteCommand getSqlCmd = connection.CreateCommand())
            {
                getSqlCmd.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name='emails'";
                createSql = Convert.ToString(getSqlCmd.ExecuteScalar());
            }

            if (!string.IsNullOrEmpty(createSql))
            {
                string v3Sql = createSql
                    .Replace("CREATE TABLE emails", "CREATE TABLE emails_v3", StringComparison.OrdinalIgnoreCase)
                    .Replace("CREATE TABLE IF NOT EXISTS emails", "CREATE TABLE emails_v3", StringComparison.OrdinalIgnoreCase)
                    .Replace("entry_id TEXT NOT NULL", "entry_id TEXT NOT NULL UNIQUE")
                    .Replace("fingerprint TEXT UNIQUE", "fingerprint TEXT");

                using SqliteTransaction tx = connection.BeginTransaction();
                try
                {
                    using (SqliteCommand cmd = connection.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = $"""
                            DROP TRIGGER IF EXISTS emails_ai;
                            DROP TRIGGER IF EXISTS emails_ad;
                            DROP TRIGGER IF EXISTS emails_au;
                            DROP TABLE IF EXISTS emails_v3;
                            {v3Sql};
                            INSERT OR IGNORE INTO emails_v3 ({string.Join(", ", cols)})
                                SELECT {string.Join(", ", selectCols)} FROM emails ORDER BY (COALESCE(verdict, '') <> '') DESC, rowid DESC;
                            DROP TABLE emails;
                            ALTER TABLE emails_v3 RENAME TO emails;
                        """;
                        cmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                CREATE INDEX IF NOT EXISTS idx_emails_date ON emails(date DESC, time DESC);
                CREATE INDEX IF NOT EXISTS idx_emails_sender ON emails(sender);
                CREATE INDEX IF NOT EXISTS idx_emails_recipient ON emails(recipient);
                CREATE INDEX IF NOT EXISTS idx_emails_verdict ON emails(verdict);
                CREATE INDEX IF NOT EXISTS idx_emails_fingerprint ON emails(fingerprint);
                CREATE INDEX IF NOT EXISTS idx_emails_user_moved ON emails(user_moved);
                CREATE INDEX IF NOT EXISTS idx_emails_message_id ON emails(message_id);
            """;
            cmd.ExecuteNonQuery();
        }

        MigrateSearchIndex(connection);

        using (SqliteCommand cmd = connection.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO emails_fts(emails_fts) VALUES('rebuild'); PRAGMA user_version = 3;";
            cmd.ExecuteNonQuery();
        }
    }

    public static string ExtractAttachmentNames(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return string.Empty;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return string.Empty;

            List<string> names = [];
            foreach (JsonElement el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("name", out JsonElement nameProp))
                {
                    string? n = nameProp.GetString();
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
                }
            }
            return string.Join(" ", names);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA user_version;";
        object? val = cmd.ExecuteScalar();
        return val != null ? Convert.ToInt64(val) : 0;
    }
}
