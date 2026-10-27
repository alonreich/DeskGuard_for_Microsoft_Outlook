namespace DeskGuard.Data;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DeskGuard.Core.Models;
using DeskGuard.Core.Serialization;
using DeskGuard.Data.Migrations;
using Microsoft.Data.Sqlite;

public sealed partial class VaultRepository
{
    public void SaveDuplicates(IEnumerable<DuplicateItem> items, string status)
    {
        AssertWritable();
        EnsureReady();

        lock (_writeSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _connection!.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO duplicate_results (id, status, saved_at, items_json)
                    VALUES (1, $status, $saved_at, $items)
                    ON CONFLICT(id) DO UPDATE SET
                        status = excluded.status,
                        saved_at = excluded.saved_at,
                        items_json = excluded.items_json;
                """;
                cmd.Parameters.AddWithValue("$status", status ?? string.Empty);
                cmd.Parameters.AddWithValue("$saved_at", DateTime.UtcNow.ToString("o"));
                List<DuplicateItem> list = items is List<DuplicateItem> l ? l : (items?.ToList() ?? []);
                cmd.Parameters.AddWithValue("$items", JsonSerializer.Serialize(list, DeskGuardJsonContext.Default.ListDuplicateItem));
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] SaveDuplicates error: {ex.Message}");
            }
        }
    }

    public (List<DuplicateItem> Items, string Status, string SavedAt) GetSavedDuplicates()
    {
        EnsureReady();
        lock (_readSyncRoot)
        {
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT status, saved_at, items_json FROM duplicate_results WHERE id = 1";
                using SqliteDataReader r = cmd.ExecuteReader();
                if (r.Read())
                {
                    string status = r.IsDBNull(0) ? string.Empty : r.GetString(0);
                    string savedAt = r.IsDBNull(1) ? string.Empty : r.GetString(1);
                    string json = r.IsDBNull(2) ? "[]" : r.GetString(2);
                    List<DuplicateItem> items = JsonSerializer.Deserialize(json, DeskGuardJsonContext.Default.ListDuplicateItem) ?? [];
                    return (items, status, savedAt);
                }
            }
            catch { }
        }
        return ([], string.Empty, string.Empty);
    }

    private sealed class RawDupRow
    {
        public long RowId { get; set; }
        public string EntryId { get; set; } = string.Empty;
        public string StoreId { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Sender { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public string CurrentFolder { get; set; } = string.Empty;
        public string FolderKind { get; set; } = string.Empty;
        public long Size { get; set; }
        public int AttachmentCount { get; set; }
    }

    public (List<DuplicateItem> Items, int Indexed) FindDuplicates(bool includeJunk = false)
    {
        EnsureReady();
        lock (_readSyncRoot)
        {
            try
            {
                // Find main store (store with most items)
                Dictionary<string, int> storeCounts = [];
                using (SqliteCommand storeCmd = _readConnection!.CreateCommand())
                {
                    storeCmd.CommandText = "SELECT COALESCE(store_id, '') AS s, COUNT(*) AS c FROM emails WHERE hidden = 0 GROUP BY s";
                    using SqliteDataReader r = storeCmd.ExecuteReader();
                    while (r.Read())
                    {
                        storeCounts[r.GetString(0)] = r.GetInt32(1);
                    }
                }
                string mainStore = storeCounts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault() ?? string.Empty;

                string junkClause = includeJunk ? string.Empty : "AND COALESCE(folder_kind, '') <> 'junk'";
                string sql = $"""
                    SELECT rowid, entry_id, store_id, fingerprint, subject, sender, timestamp, current_folder, folder_kind, size, attachments_json
                    FROM emails
                    WHERE hidden = 0 AND COALESCE(fingerprint, '') <> '' AND substr(entry_id, 1, 4) <> 'msg_'
                      {junkClause}
                      AND fingerprint IN (SELECT fingerprint FROM emails WHERE hidden = 0 GROUP BY fingerprint HAVING COUNT(*) > 1)
                """;

                List<RawDupRow> rows = [];
                using (SqliteCommand cmd = _readConnection.CreateCommand())
                {
                    cmd.CommandText = sql;
                    using SqliteDataReader r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        string attJson = r.IsDBNull(10) ? "[]" : r.GetString(10);
                        int attCount = 0;
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(attJson);
                            if (doc.RootElement.ValueKind == JsonValueKind.Array)
                            {
                                attCount = doc.RootElement.GetArrayLength();
                            }
                        }
                        catch { }

                        rows.Add(new RawDupRow
                        {
                            RowId = r.GetInt64(0),
                            EntryId = r.GetString(1),
                            StoreId = r.IsDBNull(2) ? string.Empty : r.GetString(2),
                            Fingerprint = r.IsDBNull(3) ? string.Empty : r.GetString(3),
                            Subject = r.IsDBNull(4) ? "(No Subject)" : r.GetString(4),
                            Sender = r.IsDBNull(5) ? "Unknown" : r.GetString(5),
                            Timestamp = r.IsDBNull(6) ? string.Empty : r.GetString(6),
                            CurrentFolder = r.IsDBNull(7) ? string.Empty : r.GetString(7),
                            FolderKind = r.IsDBNull(8) ? string.Empty : r.GetString(8),
                            Size = r.IsDBNull(9) ? 0 : r.GetInt64(9),
                            AttachmentCount = attCount
                        });
                    }
                }

                static int KindRank(string k) => (k is "inbox" or "sent") ? 0 : (k == "junk" ? 3 : 1);

                // Rank comparator: lower is better
                bool IsBetter(RawDupRow a, RawDupRow b)
                {
                    int kA = KindRank(a.FolderKind);
                    int kB = KindRank(b.FolderKind);
                    if (kA != kB) return kA < kB;

                    int sA = a.StoreId == mainStore ? 0 : 1;
                    int sB = b.StoreId == mainStore ? 0 : 1;
                    if (sA != sB) return sA < sB;

                    if (a.AttachmentCount != b.AttachmentCount) return a.AttachmentCount > b.AttachmentCount;
                    if (a.Size != b.Size) return a.Size > b.Size;
                    return a.RowId < b.RowId;
                }

                // Group by fingerprint + mailbox kind (sent vs other)
                Dictionary<string, List<RawDupRow>> groups = [];
                foreach (RawDupRow r in rows)
                {
                    string key = $"{r.Fingerprint}|{(r.FolderKind == "sent" ? "sent" : "other")}";
                    if (!groups.TryGetValue(key, out var list))
                    {
                        list = [];
                        groups[key] = list;
                    }
                    list.Add(r);
                }

                List<DuplicateItem> duplicateItems = [];
                foreach (List<RawDupRow> list in groups.Values)
                {
                    if (list.Count < 2) continue;

                    RawDupRow survivor = list[0];
                    for (int i = 1; i < list.Count; i++)
                    {
                        if (IsBetter(list[i], survivor))
                        {
                            survivor = list[i];
                        }
                    }

                    foreach (RawDupRow r in list)
                    {
                        if (ReferenceEquals(r, survivor)) continue;

                        duplicateItems.Add(new DuplicateItem
                        {
                            EntryId = r.EntryId,
                            StoreId = r.StoreId,
                            Subject = r.Subject,
                            Sender = r.Sender,
                            Timestamp = r.Timestamp,
                            Size = r.Size,
                            Folder = r.CurrentFolder,
                            Store = string.Empty,
                            Dna = r.Fingerprint,
                            SurvivorId = survivor.EntryId,
                            SurvivorStoreId = survivor.StoreId,
                            SurvivorFolder = survivor.CurrentFolder,
                            SurvivorStore = string.Empty
                        });
                    }
                }

                int indexed = 0;
                using (SqliteCommand countCmd = _readConnection.CreateCommand())
                {
                    countCmd.CommandText = "SELECT COUNT(*) FROM emails WHERE hidden = 0";
                    indexed = Convert.ToInt32(countCmd.ExecuteScalar());
                }

                return (duplicateItems, indexed);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] FindDuplicates error: {ex.Message}");
                return ([], 0);
            }
        }
    }
}
