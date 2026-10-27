namespace DeskGuard.Data;

using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public sealed partial class VaultRepository
{
    public (int Hidden, bool Skipped) ReconcileFolder(string storeId, string folderPath, IReadOnlySet<string> presentKeys)
    {
        AssertWritable();
        EnsureReady();
        if (string.IsNullOrWhiteSpace(folderPath) || presentKeys == null)
        {
            return (0, false);
        }

        lock (_writeSyncRoot)
        {
            List<(long RowId, string EntryId, string MessageId)> rows = [];
            using (SqliteCommand cmd = _connection!.CreateCommand())
            {
                cmd.CommandText = "SELECT rowid, entry_id, message_id FROM emails WHERE hidden = 0 AND current_folder = $folder AND COALESCE(store_id, '') = $sid";
                cmd.Parameters.AddWithValue("$folder", folderPath);
                cmd.Parameters.AddWithValue("$sid", storeId ?? string.Empty);
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    rows.Add((
                        r.GetInt64(0),
                        r.IsDBNull(1) ? string.Empty : r.GetString(1),
                        r.IsDBNull(2) ? string.Empty : r.GetString(2)
                    ));
                }
            }

            List<long> goneRowIds = [];
            foreach (var (rowid, entryId, messageId) in rows)
            {
                bool present = presentKeys.Contains(entryId) ||
                               (!string.IsNullOrEmpty(messageId) && presentKeys.Contains($"mid:{messageId}"));

                if (!present)
                {
                    goneRowIds.Add(rowid);
                }
            }

            // Safety guard: if folder has 20+ items and more than half are missing, skip hiding
            if (rows.Count >= 20 && goneRowIds.Count > rows.Count / 2)
            {
                return (0, true);
            }

            if (goneRowIds.Count > 0)
            {
                using SqliteTransaction tx = _connection.BeginTransaction();
                try
                {
                    using SqliteCommand hideCmd = _connection.CreateCommand();
                    hideCmd.Transaction = tx;
                    hideCmd.CommandText = "UPDATE emails SET hidden = 1 WHERE rowid = $rid";
                    var pRid = hideCmd.Parameters.Add("$rid", SqliteType.Integer);

                    foreach (long rid in goneRowIds)
                    {
                        pRid.Value = rid;
                        hideCmd.ExecuteNonQuery();
                    }
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }

            return (goneRowIds.Count, false);
        }
    }

    public void RelocateEntry(string oldEntryId, string newEntryId, string storeId)
    {
        AssertWritable();
        EnsureReady();
        if (string.IsNullOrWhiteSpace(oldEntryId) || string.IsNullOrWhiteSpace(newEntryId) || oldEntryId == newEntryId)
        {
            return;
        }

        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand existsCmd = _connection!.CreateCommand();
                existsCmd.CommandText = "SELECT 1 FROM emails WHERE entry_id = $new";
                existsCmd.Parameters.AddWithValue("$new", newEntryId);
                if (existsCmd.ExecuteScalar() != null) return;

                using SqliteCommand updCmd = _connection.CreateCommand();
                updCmd.CommandText = """
                    UPDATE emails
                    SET entry_id = $new,
                        store_id = CASE WHEN $sid <> '' THEN $sid ELSE store_id END,
                        hidden = 0
                    WHERE entry_id = $old;
                """;
                updCmd.Parameters.AddWithValue("$new", newEntryId);
                updCmd.Parameters.AddWithValue("$sid", storeId ?? string.Empty);
                updCmd.Parameters.AddWithValue("$old", oldEntryId);
                updCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] RelocateEntry error: {ex.Message}");
            }
        }
    }

    public bool UpdateVerdict(string entryId, string fingerprint, string verdict, string tier, double score, string? newEntryId = null)
    {
        AssertWritable();
        EnsureReady();
        if (string.IsNullOrWhiteSpace(entryId) && string.IsNullOrWhiteSpace(fingerprint)) return false;

        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    UPDATE emails
                    SET verdict = $verdict,
                        tier = $tier,
                        score = $score,
                        entry_id = COALESCE($newEntryId, entry_id),
                        hidden = 0
                    WHERE fingerprint = $fp OR entry_id = $eid;
                """;
                cmd.Parameters.AddWithValue("$verdict", verdict ?? "Safe");
                cmd.Parameters.AddWithValue("$tier", tier ?? string.Empty);
                cmd.Parameters.AddWithValue("$score", score);
                cmd.Parameters.AddWithValue("$newEntryId", string.IsNullOrEmpty(newEntryId) ? DBNull.Value : newEntryId);
                cmd.Parameters.AddWithValue("$fp", fingerprint ?? string.Empty);
                cmd.Parameters.AddWithValue("$eid", entryId ?? string.Empty);

                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] UpdateVerdict error: {ex.Message}");
                return false;
            }
        }
    }

    public bool UpdateUserMoved(string id, string currentFolder)
    {
        AssertWritable();
        EnsureReady();
        if (string.IsNullOrWhiteSpace(id)) return false;

        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    UPDATE emails
                    SET user_moved = 1, current_folder = $folder
                    WHERE id = $id OR entry_id = $id OR fingerprint = $id;
                """;
                cmd.Parameters.AddWithValue("$folder", currentFolder ?? "Outlook Folder");
                cmd.Parameters.AddWithValue("$id", id);

                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] UpdateUserMoved error: {ex.Message}");
                return false;
            }
        }
    }

    public void HideEmails(IEnumerable<string> entryIds)
    {
        AssertWritable();
        EnsureReady();
        if (entryIds == null) return;

        lock (_writeSyncRoot)
        {
            using SqliteTransaction tx = _connection!.BeginTransaction();
            try
            {
                using SqliteCommand stmt = _connection.CreateCommand();
                stmt.Transaction = tx;
                stmt.CommandText = "UPDATE emails SET hidden = 1 WHERE entry_id = $eid";
                var pEid = stmt.Parameters.Add("$eid", SqliteType.Text);

                foreach (string id in entryIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        pEid.Value = id;
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

    public void DeleteEmails(IEnumerable<string> entryIds)
    {
        AssertWritable();
        EnsureReady();
        if (entryIds == null) return;

        lock (_writeSyncRoot)
        {
            using SqliteTransaction tx = _connection!.BeginTransaction();
            try
            {
                using SqliteCommand stmt = _connection.CreateCommand();
                stmt.Transaction = tx;
                stmt.CommandText = "DELETE FROM emails WHERE entry_id = $eid";
                var pEid = stmt.Parameters.Add("$eid", SqliteType.Text);

                foreach (string id in entryIds)
                {
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        pEid.Value = id;
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
}
