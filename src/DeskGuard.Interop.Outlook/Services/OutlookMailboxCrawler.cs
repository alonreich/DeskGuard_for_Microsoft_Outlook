namespace DeskGuard.Interop.Outlook.Services;

using System;
using System.Collections.Generic;
using System.Globalization;
using DeskGuard.Interop.Outlook.Com;
using Microsoft.Office.Interop.Outlook;

public sealed class CrawledFolderEntry
{
    public string FolderId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public bool IsSent { get; set; }
}

public sealed class CrawledEmailSummary
{
    public string EntryId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public long Size { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public bool IsSent { get; set; }
}

public static class OutlookMailboxCrawler
{
    public static string GetReceivedSinceFilter(DateTime sinceUtc)
    {
        return $"@SQL=\"urn:schemas:httpmail:datereceived\" >= '{sinceUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}'";
    }

    public static List<CrawledFolderEntry> DiscoverMailFolders(NameSpace ns, bool includeJunk = false)
    {
        List<CrawledFolderEntry> result = [];
        if (ns == null) return result;

        Stores? stores = null;
        try
        {
            stores = ns.Stores;
            int sCount = stores.Count;

            for (int s = 1; s <= sCount; s++)
            {
                Store? store = null;
                try
                {
                    store = stores[s];
                    string storeId = store.StoreID ?? string.Empty;
                    string storeName = store.DisplayName ?? string.Empty;

                    HashSet<string> skipFolderIds = [];
                    var skipDefaults = includeJunk
                        ? new[] { OlDefaultFolders.olFolderDeletedItems, OlDefaultFolders.olFolderOutbox, OlDefaultFolders.olFolderDrafts }
                        : new[] { OlDefaultFolders.olFolderDeletedItems, OlDefaultFolders.olFolderOutbox, OlDefaultFolders.olFolderDrafts, OlDefaultFolders.olFolderJunk };

                    foreach (OlDefaultFolders ft in skipDefaults)
                    {
                        MAPIFolder? sf = null;
                        try
                        {
                            sf = store.GetDefaultFolder(ft);
                            if (sf != null && !string.IsNullOrEmpty(sf.EntryID))
                            {
                                skipFolderIds.Add(sf.EntryID);
                            }
                        }
                        catch { }
                        finally { ComUtils.Release(sf); }
                    }

                    string sentId = string.Empty;
                    MAPIFolder? sentFolder = null;
                    try
                    {
                        sentFolder = store.GetDefaultFolder(OlDefaultFolders.olFolderSentMail);
                        if (sentFolder != null) sentId = sentFolder.EntryID ?? string.Empty;
                    }
                    catch { }
                    finally { ComUtils.Release(sentFolder); }

                    MAPIFolder? root = null;
                    try
                    {
                        root = store.GetRootFolder();
                        if (root == null) continue;

                        Queue<MAPIFolder> queue = new();
                        queue.Enqueue(root);

                        try
                        {
                            while (queue.Count > 0)
                            {
                                MAPIFolder folder = queue.Dequeue();
                                Folders? subFolders = null;
                                try
                                {
                                    string fid = folder.EntryID ?? string.Empty;
                                    if (skipFolderIds.Contains(fid) || folder.Name == "DeskGuard Quarantine")
                                    {
                                        continue;
                                    }

                                    if (folder.DefaultItemType == OlItemType.olMailItem)
                                    {
                                        result.Add(new CrawledFolderEntry
                                        {
                                            FolderId = fid,
                                            StoreId = storeId,
                                            StoreName = storeName,
                                            FolderName = folder.FolderPath ?? folder.Name,
                                            IsSent = fid == sentId
                                        });
                                    }

                                    subFolders = folder.Folders;
                                    int subCount = subFolders.Count;
                                    for (int i = 1; i <= subCount; i++)
                                    {
                                        queue.Enqueue(subFolders[i]);
                                    }
                                }
                                catch { }
                                finally
                                {
                                    ComUtils.Release(subFolders);
                                    if (!ReferenceEquals(folder, root)) ComUtils.Release(folder);
                                }
                            }
                        }
                        finally
                        {
                            while (queue.Count > 0)
                            {
                                MAPIFolder remaining = queue.Dequeue();
                                if (!ReferenceEquals(remaining, root)) ComUtils.Release(remaining);
                            }
                        }
                    }
                    finally
                    {
                        ComUtils.Release(root);
                    }
                }
                finally
                {
                    ComUtils.Release(store);
                }
            }
        }
        catch { }
        finally
        {
            ComUtils.Release(stores);
        }

        return result;
    }

    public static List<CrawledEmailSummary> ReadFolderItems(NameSpace ns, CrawledFolderEntry folderEntry, string? filter = null)
    {
        List<CrawledEmailSummary> result = [];
        if (ns == null || folderEntry == null) return result;

        MAPIFolder? folder = null;
        Table? table = null;
        Columns? columns = null;

        try
        {
            folder = ns.GetFolderFromID(folderEntry.FolderId, folderEntry.StoreId);
            if (folder == null) return result;

            table = string.IsNullOrEmpty(filter)
                ? folder.GetTable(Type.Missing, OlTableContents.olUserItems)
                : folder.GetTable(filter, OlTableContents.olUserItems);

            if (table == null) return result;

            columns = table.Columns;
            columns.RemoveAll();
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrEntryId));
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrMessageClass));
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrInternetMessageId));
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrMessageSize));
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrSubjectW));
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrSentRepresentingNameW));
            columns.Add(MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrMessageDeliveryTime));

            while (!table.EndOfTable)
            {
                Row? row = null;
                try
                {
                    row = table.GetNextRow();
                    if (row == null) break;

                    string msgClass = row[2] as string ?? string.Empty;
                    if (!msgClass.StartsWith("IPM.Note", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    object rawEid = row[1];
                    string entryId = rawEid is byte[] b ? Convert.ToHexString(b) : (rawEid as string ?? string.Empty);
                    string msgId = row[3] as string ?? string.Empty;
                    long size = Convert.ToInt64(row[4] ?? 0);
                    string subject = row[5] as string ?? string.Empty;
                    string sender = row[6] as string ?? string.Empty;
                    string ts = string.Empty;
                    try
                    {
                        if (row[7] is DateTime dt) ts = dt.ToString("yyyy-MM-dd HH:mm:ss");
                        else if (row[7] != null) ts = row[7].ToString() ?? string.Empty;
                    }
                    catch { }

                    if (!string.IsNullOrEmpty(entryId))
                    {
                        result.Add(new CrawledEmailSummary
                        {
                            EntryId = entryId,
                            StoreId = folderEntry.StoreId,
                            MessageId = msgId,
                            Subject = subject,
                            Sender = sender,
                            Timestamp = ts,
                            Size = size,
                            FolderPath = folderEntry.FolderName,
                            IsSent = folderEntry.IsSent
                        });
                    }
                }
                catch { }
                finally
                {
                    ComUtils.Release(row);
                }
            }
        }
        catch (System.Exception ex)
        {
            DeskGuard.Core.Logging.AppLogger.Warn($"[ReadFolderItems] Error querying items in '{folderEntry.FolderName}': {ex.Message}");
        }
        finally
        {
            ComUtils.Release(columns);
            ComUtils.Release(table);
            ComUtils.Release(folder);
        }

        return result;
    }
}
