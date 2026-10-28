namespace DeskGuard.Data;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeskGuard.Core.Interfaces;
using DeskGuard.Core.Models;
using DeskGuard.Data.Migrations;
using DeskGuard.Data.Sqlite;
using Microsoft.Data.Sqlite;

public sealed partial class VaultRepository : IVaultRepository
{
    private SqliteConnection? _connection;
    private SqliteConnection? _readConnection;
    private readonly object _writeSyncRoot = new();
    private readonly object _readSyncRoot = new();
    private bool _readOnly;
    private string? _dbPath;

    private SqliteCommand? _insertStmt;
    private SqliteCommand? _indexStmt;

    [GeneratedRegex(@"^[A-Za-z0-9+/]+={0,2}$")]
    private static partial Regex Base64Regex();

    public VaultRepository()
    {
    }

    public VaultRepository(string dbDir, bool readOnly = false)
    {
        Initialize(dbDir, readOnly);
    }

    public void Initialize(string dbDir, bool readOnly = false)
    {
        lock (_writeSyncRoot)
        {
            lock (_readSyncRoot)
            {
                if (_connection != null || _readConnection != null) return;

                _readOnly = readOnly;
                if (!Directory.Exists(dbDir))
                {
                    Directory.CreateDirectory(dbDir);
                }

                _dbPath = Path.Combine(dbDir, "deskguard_vault.db");

                if (_readOnly && !File.Exists(_dbPath))
                {
                    return;
                }

                if (!_readOnly)
                {
                    _connection = SqliteConnectionFactory.CreateConnection(_dbPath, readOnly: false);
                    VaultMigrationManager.InitializeAndMigrate(_connection, _dbPath);
                    PrepareStatements();
                    _readConnection = SqliteConnectionFactory.CreateConnection(_dbPath, readOnly: true);
                }
                else
                {
                    _readConnection = SqliteConnectionFactory.CreateConnection(_dbPath, readOnly: true);
                    _connection = _readConnection;
                }
            }
        }
    }

    private void EnsureReady()
    {
        if (_readConnection == null)
        {
            if (_readOnly && _dbPath != null && File.Exists(_dbPath))
            {
                lock (_readSyncRoot)
                {
                    if (_readConnection == null)
                    {
                        _readConnection = SqliteConnectionFactory.CreateConnection(_dbPath, true);
                        _connection = _readConnection;
                    }
                }
            }
            else
            {
                throw new InvalidOperationException("VaultRepository is not initialized.");
            }
        }
    }

    private void AssertWritable()
    {
        if (_readOnly)
        {
            throw new InvalidOperationException("VaultRepository is read-only in this process; send the write to the service.");
        }
        if (_connection == null)
        {
            throw new InvalidOperationException("VaultRepository is not initialized for writing.");
        }
    }

    private void PrepareStatements()
    {
        if (_connection == null || _readOnly) return;

        _insertStmt = _connection.CreateCommand();
        _insertStmt.CommandText = """
            INSERT INTO emails (
                id, entry_id, store_id, fingerprint, subject, sender, recipient, cc,
                date, time, timestamp, body, headers, verdict, score, tier,
                attachments_json, unread, current_folder, message_id, attachments
            ) VALUES (
                $id, $entry_id, $store_id, $fingerprint, $subject, $sender, $recipient, $cc,
                $date, $time, $timestamp, $body, $headers, $verdict, $score, $tier,
                $attachments_json, $unread, $current_folder, $message_id, $attachments
            )
            ON CONFLICT(entry_id) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                attachments = CASE WHEN length(COALESCE(emails.attachments, '')) >= length(COALESCE(excluded.attachments, '')) THEN emails.attachments ELSE excluded.attachments END,
                store_id = CASE WHEN excluded.store_id <> '' THEN excluded.store_id ELSE emails.store_id END,
                verdict = excluded.verdict,
                score = excluded.score,
                tier = excluded.tier,
                unread = excluded.unread,
                hidden = 0,
                current_folder = COALESCE(excluded.current_folder, emails.current_folder),
                message_id = COALESCE(excluded.message_id, emails.message_id),
                body = CASE WHEN excluded.body <> '' THEN excluded.body ELSE emails.body END,
                headers = CASE WHEN excluded.headers <> '' THEN excluded.headers ELSE emails.headers END;
        """;
        _insertStmt.Parameters.Add("$id", SqliteType.Text);
        _insertStmt.Parameters.Add("$entry_id", SqliteType.Text);
        _insertStmt.Parameters.Add("$store_id", SqliteType.Text);
        _insertStmt.Parameters.Add("$fingerprint", SqliteType.Text);
        _insertStmt.Parameters.Add("$subject", SqliteType.Text);
        _insertStmt.Parameters.Add("$sender", SqliteType.Text);
        _insertStmt.Parameters.Add("$recipient", SqliteType.Text);
        _insertStmt.Parameters.Add("$cc", SqliteType.Text);
        _insertStmt.Parameters.Add("$date", SqliteType.Text);
        _insertStmt.Parameters.Add("$time", SqliteType.Text);
        _insertStmt.Parameters.Add("$timestamp", SqliteType.Text);
        _insertStmt.Parameters.Add("$body", SqliteType.Text);
        _insertStmt.Parameters.Add("$headers", SqliteType.Text);
        _insertStmt.Parameters.Add("$verdict", SqliteType.Text);
        _insertStmt.Parameters.Add("$score", SqliteType.Real);
        _insertStmt.Parameters.Add("$tier", SqliteType.Text);
        _insertStmt.Parameters.Add("$attachments_json", SqliteType.Text);
        _insertStmt.Parameters.Add("$unread", SqliteType.Integer);
        _insertStmt.Parameters.Add("$current_folder", SqliteType.Text);
        _insertStmt.Parameters.Add("$message_id", SqliteType.Text);
        _insertStmt.Parameters.Add("$attachments", SqliteType.Text);
        _insertStmt.Prepare();

        _indexStmt = _connection.CreateCommand();
        _indexStmt.CommandText = """
            INSERT INTO emails (
                id, entry_id, store_id, fingerprint, subject, sender, recipient, cc,
                date, time, timestamp, body, headers, verdict, score, tier,
                attachments_json, unread, current_folder, message_id, attachments, folder_kind, size
            ) VALUES (
                $id, $entry_id, $store_id, $fingerprint, $subject, $sender, $recipient, $cc,
                $date, $time, $timestamp, $body, $headers, '', NULL, '',
                $attachments_json, $unread, $current_folder, $message_id, $attachments, $folder_kind, $size
            )
            ON CONFLICT(entry_id) DO UPDATE SET
                fingerprint = excluded.fingerprint,
                store_id = excluded.store_id,
                folder_kind = excluded.folder_kind,
                size = excluded.size,
                hidden = 0,
                current_folder = excluded.current_folder,
                message_id = COALESCE(excluded.message_id, emails.message_id),
                attachments_json = excluded.attachments_json,
                attachments = excluded.attachments,
                body = CASE WHEN COALESCE(emails.body, '') = '' THEN excluded.body ELSE emails.body END,
                headers = CASE WHEN COALESCE(emails.headers, '') = '' THEN excluded.headers ELSE emails.headers END;
        """;
        _indexStmt.Parameters.Add("$id", SqliteType.Text);
        _indexStmt.Parameters.Add("$entry_id", SqliteType.Text);
        _indexStmt.Parameters.Add("$store_id", SqliteType.Text);
        _indexStmt.Parameters.Add("$fingerprint", SqliteType.Text);
        _indexStmt.Parameters.Add("$subject", SqliteType.Text);
        _indexStmt.Parameters.Add("$sender", SqliteType.Text);
        _indexStmt.Parameters.Add("$recipient", SqliteType.Text);
        _indexStmt.Parameters.Add("$cc", SqliteType.Text);
        _indexStmt.Parameters.Add("$date", SqliteType.Text);
        _indexStmt.Parameters.Add("$time", SqliteType.Text);
        _indexStmt.Parameters.Add("$timestamp", SqliteType.Text);
        _indexStmt.Parameters.Add("$body", SqliteType.Text);
        _indexStmt.Parameters.Add("$headers", SqliteType.Text);
        _indexStmt.Parameters.Add("$attachments_json", SqliteType.Text);
        _indexStmt.Parameters.Add("$unread", SqliteType.Integer);
        _indexStmt.Parameters.Add("$current_folder", SqliteType.Text);
        _indexStmt.Parameters.Add("$message_id", SqliteType.Text);
        _indexStmt.Parameters.Add("$attachments", SqliteType.Text);
        _indexStmt.Parameters.Add("$folder_kind", SqliteType.Text);
        _indexStmt.Parameters.Add("$size", SqliteType.Integer);
        _indexStmt.Prepare();
    }

    public static string DecodeMaybeBase64(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
        string clean = text.Trim();
        if (clean.Length % 4 == 0 && clean.Length > 20 && !clean.Contains(' ') && Base64Regex().IsMatch(clean))
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(clean);
                string decoded = Encoding.UTF8.GetString(bytes);
                if (!string.IsNullOrEmpty(decoded) && !decoded.Contains('\uFFFD'))
                {
                    return decoded;
                }
            }
            catch { }
        }
        return text;
    }

    public static string LikeEscape(string term)
    {
        StringBuilder sb = new(term.Length + 4);
        foreach (char c in term)
        {
            if (c is '\\' or '%' or '_')
            {
                sb.Append('\\').Append(c);
            }
            else if (c == '*')
            {
                sb.Append('%');
            }
            else if (c == '?')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    public EmailRecord NormalizeItem(EmailRecord item)
    {
        string entryId = !string.IsNullOrWhiteSpace(item.EntryId)
            ? item.EntryId
            : (!string.IsNullOrWhiteSpace(item.Fingerprint) ? item.Fingerprint : $"msg_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}");

        string fingerprint = !string.IsNullOrWhiteSpace(item.Fingerprint)
            ? item.Fingerprint
            : (!string.IsNullOrWhiteSpace(item.MessageId) ? $"mid:{item.MessageId}" : entryId);

        string dateStr = item.Date;
        string timeStr = item.Time;
        string tsStr = item.Timestamp;

        if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(timeStr))
        {
            DateTime dt = DateTime.TryParse(tsStr, out DateTime parsed) ? parsed : DateTime.Now;
            if (string.IsNullOrWhiteSpace(dateStr)) dateStr = dt.ToString("yyyy-MM-dd");
            if (string.IsNullOrWhiteSpace(timeStr)) timeStr = dt.ToString("HH:mm:ss");
        }
        if (string.IsNullOrWhiteSpace(tsStr)) tsStr = $"{dateStr} {timeStr}";

        string attsJson = !string.IsNullOrWhiteSpace(item.AttachmentsJson) ? item.AttachmentsJson : "[]";
        string attNames = VaultMigrationManager.ExtractAttachmentNames(attsJson);
        string attSearch = string.IsNullOrWhiteSpace(item.Attachments) ? attNames : $"{attNames} {item.Attachments}".Trim();

        return new EmailRecord
        {
            Id = !string.IsNullOrWhiteSpace(item.Id) ? item.Id : $"loc_{Guid.NewGuid():D}",
            EntryId = entryId,
            StoreId = item.StoreId ?? string.Empty,
            Fingerprint = fingerprint,
            Subject = !string.IsNullOrWhiteSpace(item.Subject) ? item.Subject : "(No Subject)",
            Sender = !string.IsNullOrWhiteSpace(item.Sender) ? item.Sender : "Unknown",
            Recipient = item.Recipient ?? string.Empty,
            Cc = item.Cc ?? string.Empty,
            Date = dateStr,
            Time = timeStr,
            Timestamp = tsStr,
            Body = DecodeMaybeBase64(item.Body),
            Headers = DecodeMaybeBase64(item.Headers),
            Verdict = !string.IsNullOrWhiteSpace(item.Verdict) ? item.Verdict : "Safe",
            Score = item.Score,
            Tier = item.Tier ?? string.Empty,
            AttachmentsJson = attsJson,
            Attachments = attSearch,
            Unread = item.Unread,
            UserMoved = item.UserMoved,
            CurrentFolder = item.CurrentFolder ?? string.Empty,
            MessageId = item.MessageId ?? string.Empty,
            Hidden = item.Hidden,
            FolderKind = item.FolderKind ?? string.Empty,
            Size = item.Size,
            PenaltiesJson = !string.IsNullOrWhiteSpace(item.PenaltiesJson) ? item.PenaltiesJson : "[]"
        };
    }

    public void InsertEmail(EmailRecord item)
    {
        AssertWritable();
        EnsureReady();
        if (item == null) return;

        lock (_writeSyncRoot)
        {
            try
            {
                EmailRecord n = NormalizeItem(item);

                if (_insertStmt == null) PrepareStatements();
                if (_insertStmt == null) return;

                _insertStmt.Parameters["$id"].Value = n.Id;
                _insertStmt.Parameters["$entry_id"].Value = n.EntryId;
                _insertStmt.Parameters["$store_id"].Value = n.StoreId;
                _insertStmt.Parameters["$fingerprint"].Value = n.Fingerprint;
                _insertStmt.Parameters["$subject"].Value = n.Subject;
                _insertStmt.Parameters["$sender"].Value = n.Sender;
                _insertStmt.Parameters["$recipient"].Value = n.Recipient;
                _insertStmt.Parameters["$cc"].Value = n.Cc;
                _insertStmt.Parameters["$date"].Value = n.Date;
                _insertStmt.Parameters["$time"].Value = n.Time;
                _insertStmt.Parameters["$timestamp"].Value = n.Timestamp;
                _insertStmt.Parameters["$body"].Value = n.Body;
                _insertStmt.Parameters["$headers"].Value = n.Headers;
                _insertStmt.Parameters["$verdict"].Value = n.Verdict;
                _insertStmt.Parameters["$score"].Value = n.Score;
                _insertStmt.Parameters["$tier"].Value = n.Tier;
                _insertStmt.Parameters["$attachments_json"].Value = n.AttachmentsJson;
                _insertStmt.Parameters["$unread"].Value = n.Unread ? 1 : 0;
                _insertStmt.Parameters["$current_folder"].Value = string.IsNullOrEmpty(n.CurrentFolder) ? DBNull.Value : n.CurrentFolder;
                _insertStmt.Parameters["$message_id"].Value = string.IsNullOrEmpty(n.MessageId) ? DBNull.Value : n.MessageId;
                _insertStmt.Parameters["$attachments"].Value = n.Attachments;

                _insertStmt.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] InsertEmail error: {ex.Message}");
            }
        }
    }

    public void InsertIndexed(EmailRecord item)
    {
        AssertWritable();
        EnsureReady();
        if (item == null) return;

        lock (_writeSyncRoot)
        {
            try
            {
                EmailRecord n = NormalizeItem(item);

                // Revive hidden row if it left its old folder
                using (SqliteCommand existsCmd = _connection!.CreateCommand())
                {
                    existsCmd.CommandText = "SELECT 1 FROM emails WHERE entry_id = $eid";
                    existsCmd.Parameters.AddWithValue("$eid", n.EntryId);
                    if (existsCmd.ExecuteScalar() == null)
                    {
                        using SqliteCommand oldCmd = _connection.CreateCommand();
                        oldCmd.CommandText = "SELECT rowid FROM emails WHERE hidden = 1 AND fingerprint = $fp AND COALESCE(store_id, '') IN ($sid, '') LIMIT 1";
                        oldCmd.Parameters.AddWithValue("$fp", n.Fingerprint);
                        oldCmd.Parameters.AddWithValue("$sid", n.StoreId);
                        object? oldRowid = oldCmd.ExecuteScalar();
                        if (oldRowid != null)
                        {
                            using SqliteCommand updOld = _connection.CreateCommand();
                            updOld.CommandText = "UPDATE emails SET entry_id = $eid WHERE rowid = $rid";
                            updOld.Parameters.AddWithValue("$eid", n.EntryId);
                            updOld.Parameters.AddWithValue("$rid", oldRowid);
                            updOld.ExecuteNonQuery();
                        }
                    }
                }

                if (_indexStmt == null) PrepareStatements();
                if (_indexStmt == null) return;

                _indexStmt.Parameters["$id"].Value = n.Id;
                _indexStmt.Parameters["$entry_id"].Value = n.EntryId;
                _indexStmt.Parameters["$store_id"].Value = n.StoreId;
                _indexStmt.Parameters["$fingerprint"].Value = n.Fingerprint;
                _indexStmt.Parameters["$subject"].Value = n.Subject;
                _indexStmt.Parameters["$sender"].Value = n.Sender;
                _indexStmt.Parameters["$recipient"].Value = n.Recipient;
                _indexStmt.Parameters["$cc"].Value = n.Cc;
                _indexStmt.Parameters["$date"].Value = n.Date;
                _indexStmt.Parameters["$time"].Value = n.Time;
                _indexStmt.Parameters["$timestamp"].Value = n.Timestamp;
                _indexStmt.Parameters["$body"].Value = n.Body;
                _indexStmt.Parameters["$headers"].Value = n.Headers;
                _indexStmt.Parameters["$attachments_json"].Value = n.AttachmentsJson;
                _indexStmt.Parameters["$unread"].Value = n.Unread ? 1 : 0;
                _indexStmt.Parameters["$current_folder"].Value = string.IsNullOrEmpty(n.CurrentFolder) ? DBNull.Value : n.CurrentFolder;
                _indexStmt.Parameters["$message_id"].Value = string.IsNullOrEmpty(n.MessageId) ? DBNull.Value : n.MessageId;
                _indexStmt.Parameters["$attachments"].Value = n.Attachments;
                _indexStmt.Parameters["$folder_kind"].Value = string.IsNullOrEmpty(n.FolderKind) ? DBNull.Value : n.FolderKind;
                _indexStmt.Parameters["$size"].Value = n.Size;

                _indexStmt.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] InsertIndexed error: {ex.Message}");
            }
        }
    }

    public EmailRecord? GetEmailById(string id)
    {
        EnsureReady();
        if (string.IsNullOrWhiteSpace(id)) return null;

        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = """
                    SELECT id, entry_id, store_id, fingerprint, subject, sender, recipient, cc,
                           date, time, timestamp, body, headers, verdict, score, tier,
                           attachments_json, attachments, unread, user_moved, current_folder,
                           message_id, hidden, folder_kind, size, penalties_json
                    FROM emails
                    WHERE id = $id OR entry_id = $id OR fingerprint = $id
                    ORDER BY (entry_id = $id) DESC, hidden ASC LIMIT 1;
                """;
                cmd.Parameters.AddWithValue("$id", id);

                using SqliteDataReader reader = cmd.ExecuteReader();
                if (!reader.Read()) return null;

                return MapEmailRecord(reader, light: false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] GetEmailById error: {ex.Message}");
                return null;
            }
        }
    }

    public void RecordAvScanResult(string entryId, string verdict, string? threatName)
    {
        AssertWritable();
        EnsureReady();
        if (string.IsNullOrEmpty(entryId)) return;

        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    UPDATE emails
                    SET av_scanned = 1,
                        av_verdict = $v,
                        av_threat_name = $t,
                        av_scanned_at = $ts
                    WHERE entry_id = $eid OR fingerprint = $eid;
                """;
                cmd.Parameters.AddWithValue("$v", verdict ?? "Clean");
                cmd.Parameters.AddWithValue("$t", (object?)threatName ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ts", DateTime.UtcNow.ToString("o"));
                cmd.Parameters.AddWithValue("$eid", entryId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] RecordAvScanResult error: {ex.Message}");
            }
        }
    }

    public List<EmailRecord> GetUnscannedAvEmails(int limit = 50)
    {
        EnsureReady();
        List<EmailRecord> list = [];
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = """
                    SELECT id, entry_id, store_id, fingerprint, subject, sender, recipient, cc,
                           date, time, timestamp, body, headers, verdict, score, tier,
                           attachments_json, attachments, unread, user_moved, current_folder,
                           message_id, hidden, folder_kind, size, penalties_json
                    FROM emails
                    WHERE (av_scanned IS NULL OR av_scanned = 0)
                      AND (hidden IS NULL OR hidden = 0)
                    ORDER BY unread DESC, created_at DESC
                    LIMIT $lim;
                """;
                cmd.Parameters.AddWithValue("$lim", limit);
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(MapEmailRecord(reader, light: false));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] GetUnscannedAvEmails error: {ex.Message}");
            }
        }
        return list;
    }

    public List<string> GetAllEntryIds()
    {
        EnsureReady();
        List<string> result = [];

        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT entry_id FROM emails WHERE hidden = 0";
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0))
                    {
                        string eid = reader.GetString(0);
                        if (!string.IsNullOrEmpty(eid)) result.Add(eid);
                    }
                }
            }
            catch { }
        }
        return result;
    }

    public int GetIndexCount()
    {
        EnsureReady();
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM emails WHERE hidden = 0";
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch { return 0; }
        }
    }

    public void AddProcessed(IEnumerable<string> keys)
    {
        AssertWritable();
        EnsureReady();
        if (keys == null) return;

        lock (_writeSyncRoot)
        {
            using SqliteTransaction tx = _connection!.BeginTransaction();
            try
            {
                using SqliteCommand stmt = _connection.CreateCommand();
                stmt.Transaction = tx;
                stmt.CommandText = "INSERT OR IGNORE INTO processed(key) VALUES ($k)";
                var pKey = stmt.Parameters.Add("$k", SqliteType.Text);

                foreach (string k in keys)
                {
                    if (!string.IsNullOrWhiteSpace(k))
                    {
                        pKey.Value = k;
                        stmt.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
            }
        }
    }

    public List<string> GetProcessedKeys()
    {
        EnsureReady();
        List<string> result = [];
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT key FROM processed";
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0)) result.Add(reader.GetString(0));
                }
            }
            catch { }
        }
        return result;
    }

    public void ClearProcessed()
    {
        AssertWritable();
        EnsureReady();
        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM processed";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public void AddProtected(IEnumerable<string> fingerprints, string kind)
    {
        AssertWritable();
        EnsureReady();
        if (fingerprints == null) return;

        lock (_writeSyncRoot)
        {
            using SqliteTransaction tx = _connection!.BeginTransaction();
            try
            {
                using SqliteCommand stmt = _connection.CreateCommand();
                stmt.Transaction = tx;
                stmt.CommandText = "INSERT OR IGNORE INTO protected(fingerprint, kind) VALUES ($fp, $k)";
                var pFp = stmt.Parameters.Add("$fp", SqliteType.Text);
                var pK = stmt.Parameters.Add("$k", SqliteType.Text);
                pK.Value = kind;

                foreach (string fp in fingerprints)
                {
                    if (!string.IsNullOrWhiteSpace(fp))
                    {
                        pFp.Value = fp;
                        stmt.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
            }
        }
    }

    public List<string> GetProtected(string kind)
    {
        EnsureReady();
        List<string> result = [];
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT fingerprint FROM protected WHERE kind = $k";
                cmd.Parameters.AddWithValue("$k", kind);
                using SqliteDataReader reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(0)) result.Add(reader.GetString(0));
                }
            }
            catch { }
        }
        return result;
    }

    public void ClearProtected()
    {
        AssertWritable();
        EnsureReady();
        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = "DELETE FROM protected";
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }

    public void ClearIncidents()
    {
        AssertWritable();
        EnsureReady();
        lock (_writeSyncRoot)
        {
            using SqliteTransaction tx = _connection!.BeginTransaction();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM emails; DELETE FROM events; DELETE FROM duplicate_results;";
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
            }
        }
    }

    public void NuclearReset()
    {
        AssertWritable();
        EnsureReady();
        lock (_writeSyncRoot)
        {
            using SqliteTransaction tx = _connection!.BeginTransaction();
            try
            {
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "DELETE FROM emails; DELETE FROM events; DELETE FROM processed; DELETE FROM protected; DELETE FROM duplicate_results;";
                cmd.ExecuteNonQuery();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
            }
        }
    }

    public long? RecordEvent(SecurityEvent ev)
    {
        AssertWritable();
        EnsureReady();
        if (ev == null) return null;

        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO events (kind, message_key, entry_id, subject, sender, from_folder, to_folder, score, reasons_json)
                    VALUES ($kind, $key, $eid, $subject, $sender, $from, $to, $score, $reasons);
                    SELECT last_insert_rowid();
                """;
                cmd.Parameters.AddWithValue("$kind", ev.Kind ?? "unknown");
                cmd.Parameters.AddWithValue("$key", ev.MessageKey ?? string.Empty);
                cmd.Parameters.AddWithValue("$eid", ev.EntryId ?? string.Empty);
                cmd.Parameters.AddWithValue("$subject", ev.Subject ?? string.Empty);
                cmd.Parameters.AddWithValue("$sender", ev.Sender ?? string.Empty);
                cmd.Parameters.AddWithValue("$from", ev.FromFolder ?? string.Empty);
                cmd.Parameters.AddWithValue("$to", ev.ToFolder ?? string.Empty);
                cmd.Parameters.AddWithValue("$score", ev.Score.HasValue ? ev.Score.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("$reasons", ev.ReasonsJson ?? "[]");

                object? rowid = cmd.ExecuteScalar();
                return rowid != null ? Convert.ToInt64(rowid) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] RecordEvent error: {ex.Message}");
                return null;
            }
        }
    }

    public List<SecurityEvent> GetEvents(int limit = 100, int offset = 0, string? kind = null)
    {
        EnsureReady();
        List<SecurityEvent> list = [];
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                string sql = "SELECT id, ts, kind, message_key, entry_id, subject, sender, from_folder, to_folder, score, reasons_json FROM events";
                if (!string.IsNullOrWhiteSpace(kind))
                {
                    string kindNorm = kind.Trim().ToLowerInvariant();
                    if (kindNorm == "quarantine")
                    {
                        sql += " WHERE LOWER(kind) IN ('quarantine', 'malicious', 'spam')";
                    }
                    else
                    {
                        sql += " WHERE LOWER(kind) = $kind";
                        cmd.Parameters.AddWithValue("$kind", kindNorm);
                    }
                }
                sql += " ORDER BY id DESC LIMIT $limit OFFSET $offset";
                cmd.Parameters.AddWithValue("$limit", limit);
                cmd.Parameters.AddWithValue("$offset", offset);
                cmd.CommandText = sql;

                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new SecurityEvent
                    {
                        Id = r.GetInt64(0),
                        Timestamp = r.IsDBNull(1) ? string.Empty : r.GetString(1),
                        Kind = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                        MessageKey = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                        EntryId = r.IsDBNull(4) ? string.Empty : r.GetString(4),
                        Subject = r.IsDBNull(5) ? string.Empty : r.GetString(5),
                        Sender = r.IsDBNull(6) ? string.Empty : r.GetString(6),
                        FromFolder = r.IsDBNull(7) ? string.Empty : r.GetString(7),
                        ToFolder = r.IsDBNull(8) ? string.Empty : r.GetString(8),
                        Score = r.IsDBNull(9) ? null : r.GetDouble(9),
                        ReasonsJson = r.IsDBNull(10) ? "[]" : r.GetString(10)
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] GetEvents error: {ex.Message}");
            }
        }
        return list;
    }

    public Dictionary<string, int> GetKnownDomains(int limit = 20000)
    {
        EnsureReady();
        Dictionary<string, int> outMap = new(StringComparer.OrdinalIgnoreCase);
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT LOWER(sender) AS s, COUNT(*) AS c FROM emails WHERE sender LIKE '%@%' GROUP BY LOWER(sender)";
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string sender = r.GetString(0);
                    int count = r.GetInt32(1);
                    int atIdx = sender.IndexOf('@');
                    if (atIdx != -1)
                    {
                        string domain = sender[(atIdx + 1)..].Trim().TrimEnd('>');
                        if (domain.Length > 3 && domain.Contains('.'))
                        {
                            outMap[domain] = outMap.GetValueOrDefault(domain) + count;
                        }
                    }
                }
            }
            catch { }
        }

        // Return top sorted by count
        Dictionary<string, int> top = [];
        var ordered = outMap.OrderByDescending(kv => kv.Value).Take(limit);
        foreach (var kv in ordered) top[kv.Key] = kv.Value;
        return top;
    }

    public List<string> GetSenderSuggestions(string prefix, int limit = 15)
    {
        EnsureReady();
        List<string> result = [];
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                string pattern = $"%{LikeEscape(prefix.Trim())}%";
                cmd.CommandText = """
                    SELECT DISTINCT sender
                    FROM emails
                    WHERE sender LIKE $pattern ESCAPE '\' AND sender != '' AND sender != 'Unknown'
                    ORDER BY sender ASC
                    LIMIT $limit;
                """;
                cmd.Parameters.AddWithValue("$pattern", pattern);
                cmd.Parameters.AddWithValue("$limit", limit);

                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!r.IsDBNull(0)) result.Add(r.GetString(0));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] GetSenderSuggestions error: {ex.Message}");
            }
        }
        return result;
    }

    public EngineStats GetStats()
    {
        EnsureReady();
        EngineStats stats = new();
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = """
                    SELECT verdict, COUNT(*) AS c
                    FROM emails
                    WHERE hidden = 0 AND COALESCE(verdict, '') <> ''
                    GROUP BY verdict
                """;
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string v = r.GetString(0).ToLowerInvariant();
                    int c = r.GetInt32(1);

                    if (v.Contains("malicious")) stats.Malicious += c;
                    else if (v.Contains("suspicious")) stats.Suspicious += c;
                    else if (v.Contains("spam")) stats.Spam += c;
                    else if (v.Contains("safe")) stats.Safe += c;

                    stats.Total += c;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] GetStats error: {ex.Message}");
            }
        }
        return stats;
    }

    public Dictionary<string, List<EmailRecord>> GetAllCategorizedEmails(int limitPerCat = 100)
    {
        EnsureReady();
        Dictionary<string, List<EmailRecord>> result = new(StringComparer.OrdinalIgnoreCase)
        {
            ["malicious"] = [],
            ["suspicious"] = [],
            ["spam"] = [],
            ["safe"] = []
        };

        lock (_readSyncRoot)
        {
            try
            {
                foreach (string cat in result.Keys)
                {
                    using SqliteCommand cmd = _readConnection!.CreateCommand();
                    cmd.CommandText = """
                        SELECT id, entry_id, store_id, fingerprint, subject, sender, recipient, cc,
                               date, time, timestamp, body, headers, verdict, score, tier,
                               attachments_json, attachments, unread, user_moved, current_folder,
                               message_id, hidden, folder_kind, size, penalties_json
                        FROM emails
                        WHERE hidden = 0 AND LOWER(verdict) LIKE $cat
                        ORDER BY date DESC, time DESC
                        LIMIT $limit
                    """;
                    cmd.Parameters.AddWithValue("$cat", $"%{cat}%");
                    cmd.Parameters.AddWithValue("$limit", limitPerCat);

                    List<EmailRecord> catRows = [];
                    using (SqliteDataReader r = cmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            catRows.Add(MapEmailRecord(r, light: true));
                        }
                    }
                    catRows.Reverse();
                    result[cat] = catRows;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] GetAllCategorizedEmails error: {ex.Message}");
            }
        }
        return result;
    }

    public void ImportLegacyData(string jsonContent)
    {
        AssertWritable();
        EnsureReady();
        if (string.IsNullOrWhiteSpace(jsonContent)) return;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(jsonContent);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("processedIds", out JsonElement pIds) && pIds.ValueKind == JsonValueKind.Array)
            {
                List<string> ids = [];
                foreach (JsonElement el in pIds.EnumerateArray())
                {
                    string? s = el.GetString();
                    if (!string.IsNullOrEmpty(s)) ids.Add(s);
                }
                AddProcessed(ids);
            }

            if (root.TryGetProperty("releasedFingerprints", out JsonElement rFps) && rFps.ValueKind == JsonValueKind.Array)
            {
                List<string> fps = [];
                foreach (JsonElement el in rFps.EnumerateArray())
                {
                    string? s = el.GetString();
                    if (!string.IsNullOrEmpty(s)) fps.Add(s);
                }
                AddProtected(fps, "released");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VaultRepository] ImportLegacyData error: {ex.Message}");
        }
    }

    public static EmailRecord MapEmailRecord(SqliteDataReader r, bool light = false)
    {
        bool userMoved = !r.IsDBNull(19) && r.GetInt32(19) == 1;
        string currentFolder = r.IsDBNull(20) ? string.Empty : r.GetString(20);

        string userMovedStory = userMoved
            ? $"Originally quarantined by DeskGuard, but you manually moved this email to folder '{(string.IsNullOrEmpty(currentFolder) ? "another folder" : currentFolder)}' inside Outlook. DeskGuard respects your choice and will not move it again."
            : string.Empty;

        return new EmailRecord
        {
            Id = r.GetString(0),
            EntryId = r.GetString(1),
            StoreId = r.IsDBNull(2) ? string.Empty : r.GetString(2),
            Fingerprint = r.IsDBNull(3) ? string.Empty : r.GetString(3),
            Subject = r.IsDBNull(4) ? "(No Subject)" : r.GetString(4),
            Sender = r.IsDBNull(5) ? "Unknown" : r.GetString(5),
            Recipient = r.IsDBNull(6) ? string.Empty : r.GetString(6),
            Cc = r.IsDBNull(7) ? string.Empty : r.GetString(7),
            Date = r.IsDBNull(8) ? string.Empty : r.GetString(8),
            Time = r.IsDBNull(9) ? string.Empty : r.GetString(9),
            Timestamp = r.IsDBNull(10) ? string.Empty : r.GetString(10),
            Body = light || r.IsDBNull(11) ? string.Empty : r.GetString(11),
            Headers = light || r.IsDBNull(12) ? string.Empty : r.GetString(12),
            Verdict = r.IsDBNull(13) ? "Safe" : r.GetString(13),
            Score = r.IsDBNull(14) ? 100.0 : r.GetDouble(14),
            Tier = r.IsDBNull(15) ? string.Empty : r.GetString(15),
            AttachmentsJson = r.IsDBNull(16) ? "[]" : r.GetString(16),
            Attachments = light || r.IsDBNull(17) ? string.Empty : r.GetString(17),
            Unread = !r.IsDBNull(18) && r.GetInt32(18) == 1,
            UserMoved = userMoved,
            CurrentFolder = currentFolder,
            MessageId = r.IsDBNull(21) ? string.Empty : r.GetString(21),
            Hidden = !r.IsDBNull(22) && r.GetInt32(22) == 1,
            FolderKind = r.IsDBNull(23) ? string.Empty : r.GetString(23),
            Size = r.IsDBNull(24) ? 0 : r.GetInt64(24),
            PenaltiesJson = r.IsDBNull(25) ? "[]" : r.GetString(25),
            UserMovedStory = userMovedStory
        };
    }

    public void Close()
    {
        lock (_writeSyncRoot)
        {
            lock (_readSyncRoot)
            {
                _insertStmt?.Dispose();
                _insertStmt = null;
                _indexStmt?.Dispose();
                _indexStmt = null;

                if (_connection != null)
                {
                    try { _connection.Close(); } catch { }
                    if (!ReferenceEquals(_connection, _readConnection))
                    {
                        _connection.Dispose();
                    }
                    _connection = null;
                }

                if (_readConnection != null)
                {
                    try { _readConnection.Close(); } catch { }
                    _readConnection.Dispose();
                    _readConnection = null;
                }
            }
        }
    }

    public void Dispose()
    {
        Close();
    }
}
