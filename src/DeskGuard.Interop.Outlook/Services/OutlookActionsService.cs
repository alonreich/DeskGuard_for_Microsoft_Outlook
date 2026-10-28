namespace DeskGuard.Interop.Outlook.Services;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DeskGuard.Core.Interfaces;
using DeskGuard.Core.Models;
using DeskGuard.Interop.Outlook.Com;
using Microsoft.Office.Interop.Outlook;
using Exception = System.Exception;

public sealed class OutlookActionsService
{
    private const string QuarantineFolderName = "DeskGuard Quarantine";

    public static MailItem? RobustMove(MailItem? item, MAPIFolder? targetFolder, string stableKey = "", string reason = "")
    {
        if (item == null || targetFolder == null) return null;

        bool? origUnread = null;
        try { origUnread = item.UnRead; } catch { }

        try
        {
            MailItem? moved = item.Move(targetFolder) as MailItem;
            if (moved != null)
            {
                if (origUnread.HasValue)
                {
                    try
                    {
                        if (moved.UnRead != origUnread.Value)
                        {
                            moved.UnRead = origUnread.Value;
                            moved.Save();
                        }
                    }
                    catch { }
                }
                return moved;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OutlookActionsService] RobustMove error: {ex.Message}");
        }
        return null;
    }

    public static MAPIFolder? GetTargetFolderSafe(MailItem? item, OlDefaultFolders folderType)
    {
        if (item == null) return null;
        MAPIFolder? parent = null;
        Store? store = null;
        try
        {
            parent = item.Parent as MAPIFolder;
            if (parent != null)
            {
                store = parent.Store;
                if (store != null)
                {
                    return store.GetDefaultFolder(folderType);
                }
            }
            return null;
        }
        catch
        {
            return null;
        }
        finally
        {
            ComUtils.Release(store);
            ComUtils.Release(parent);
        }
    }

    public static MAPIFolder? GetQuarantineFolder(NameSpace ns, MailItem? item)
    {
        if (ns == null) return null;
        Store? store = null;
        MAPIFolder? root = null;
        Folders? folders = null;
        try
        {
            if (item != null)
            {
                MAPIFolder? parent = item.Parent as MAPIFolder;
                if (parent != null)
                {
                    store = parent.Store;
                    ComUtils.Release(parent);
                }
            }

            store ??= ns.DefaultStore;
            if (store == null) return null;

            root = store.GetRootFolder();
            if (root == null) return null;

            folders = root.Folders;
            int fCount = folders.Count;
            for (int i = 1; i <= fCount; i++)
            {
                MAPIFolder f = folders[i];
                if (f.Name == QuarantineFolderName)
                {
                    return f;
                }
                ComUtils.Release(f);
            }

            // Create quarantine folder if it doesn't exist
            return folders.Add(QuarantineFolderName, Type.Missing);
        }
        catch
        {
            return null;
        }
        finally
        {
            ComUtils.Release(folders);
            ComUtils.Release(root);
            ComUtils.Release(store);
        }
    }

    public static MAPIFolder? GetQuarantineSubfolder(NameSpace ns, MailItem? item, string subfolderName)
    {
        MAPIFolder? parent = GetQuarantineFolder(ns, item);
        if (parent == null) return null;

        Folders? folders = null;
        bool returningParent = false;
        try
        {
            folders = parent.Folders;
            int fCount = folders.Count;
            for (int i = 1; i <= fCount; i++)
            {
                MAPIFolder f = folders[i];
                if (string.Equals(f.Name, subfolderName, StringComparison.OrdinalIgnoreCase))
                {
                    return f;
                }
                ComUtils.Release(f);
            }

            return folders.Add(subfolderName, Type.Missing);
        }
        catch
        {
            returningParent = true;
            return parent;
        }
        finally
        {
            ComUtils.Release(folders);
            if (!returningParent)
            {
                ComUtils.Release(parent);
            }
        }
    }

    public static bool TestIsQuarantineFolder(MAPIFolder? folder)
    {
        if (folder == null) return false;
        Store? store = null;
        try
        {
            string fid = folder.EntryID ?? string.Empty;
            store = folder.Store;
            if (store != null)
            {
                // Junk (23) or Deleted (3)
                foreach (OlDefaultFolders dfType in new[] { OlDefaultFolders.olFolderJunk, OlDefaultFolders.olFolderDeletedItems })
                {
                    MAPIFolder? df = null;
                    try
                    {
                        df = store.GetDefaultFolder(dfType);
                        if (df != null && df.EntryID == fid) return true;
                    }
                    catch { }
                    finally { ComUtils.Release(df); }
                }
            }

            MAPIFolder? cur = folder;
            for (int depth = 0; depth < 4 && cur != null; depth++)
            {
                if (cur.Name == QuarantineFolderName)
                {
                    if (!ReferenceEquals(cur, folder)) ComUtils.Release(cur);
                    return true;
                }
                MAPIFolder? next = null;
                try { next = cur.Parent as MAPIFolder; } catch { }
                if (!ReferenceEquals(cur, folder)) ComUtils.Release(cur);
                cur = next;
            }
            if (cur != null && !ReferenceEquals(cur, folder)) ComUtils.Release(cur);
        }
        catch { }
        finally
        {
            ComUtils.Release(store);
        }
        return false;
    }

    public static MailItem? FindItemByMessageId(NameSpace ns, string messageId)
    {
        if (ns == null || string.IsNullOrWhiteSpace(messageId)) return null;

        Stores? stores = null;
        try
        {
            stores = ns.Stores;
            int count = stores.Count;
            for (int storeIdx = 1; storeIdx <= count; storeIdx++)
            {
                Store? store = null;
                MAPIFolder? root = null;
                try
                {
                    store = stores[storeIdx];
                    root = store.GetRootFolder();
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
                                if (folder.DefaultItemType == OlItemType.olMailItem)
                                {
                                    string filter = $"@SQL=\"http://schemas.microsoft.com/mapi/proptag/0x1035001F\" = '{messageId.Replace("'", "''")}'";
                                    Table? table = null;
                                    try
                                    {
                                        table = folder.GetTable(filter, OlTableContents.olUserItems);
                                        if (table != null && !table.EndOfTable)
                                        {
                                            Row? row = null;
                                            try
                                            {
                                                row = table.GetNextRow();
                                                if (row != null)
                                                {
                                                    object? rawEid = null;
                                                    try { rawEid = row[MapiPropertyTags.ToPropTagUrl(MapiPropertyTags.PrEntryId)]; } catch { }
                                                    if (rawEid == null) { try { rawEid = row["EntryID"]; } catch { } }

                                                    string entryId = rawEid switch
                                                    {
                                                        string s => s,
                                                        byte[] b => Convert.ToHexString(b),
                                                        _ => string.Empty
                                                    };
                                                    if (!string.IsNullOrEmpty(entryId))
                                                    {
                                                        return ns.GetItemFromID(entryId, store.StoreID) as MailItem;
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                ComUtils.Release(row);
                                            }
                                        }
                                    }
                                    catch { }
                                    finally { ComUtils.Release(table); }
                                }

                                subFolders = folder.Folders;
                                int subCount = subFolders.Count;
                                for (int i = 1; i <= subCount; i++)
                                {
                                    queue.Enqueue(subFolders[i]);
                                }
                            }
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
                    ComUtils.Release(store);
                }
            }
        }
        catch { }
        finally
        {
            ComUtils.Release(stores);
        }
        return null;
    }
}
