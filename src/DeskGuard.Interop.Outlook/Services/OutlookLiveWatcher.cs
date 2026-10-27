namespace DeskGuard.Interop.Outlook.Services;

using System;
using System.Collections.Generic;
using DeskGuard.Core.Concurrency;
using DeskGuard.Interop.Outlook.Com;
using DeskGuard.Interop.Outlook.Sta;
using Microsoft.Office.Interop.Outlook;

public sealed class OutlookLiveWatcher : IDisposable
{
    private readonly OutlookStaCoordinator _coordinator;
    private readonly Action<string, string> _onNewMailItem;
    private readonly List<Items> _watchedItems = [];
    private readonly List<MAPIFolder> _watchedFolders = [];
    private readonly List<ItemsEvents_ItemAddEventHandler> _handlers = [];
    private bool _isDisposed;

    public OutlookLiveWatcher(OutlookStaCoordinator coordinator, Action<string, string> onNewMailItem)
    {
        _coordinator = coordinator;
        _onNewMailItem = onNewMailItem;
    }

    public bool HasActiveWatchers => _watchedItems.Count > 0;

    public void RegisterWatchers(NameSpace ns)
    {
        if (ns == null) return;

        Stores? stores = null;
        try
        {
            stores = ns.Stores;
            int count = stores.Count;

            for (int i = 1; i <= count; i++)
            {
                Store? store = null;
                MAPIFolder? inbox = null;
                Items? items = null;

                try
                {
                    store = stores[i];
                    string storeId = store.StoreID;

                    inbox = store.GetDefaultFolder(OlDefaultFolders.olFolderInbox);
                    if (inbox != null)
                    {
                        items = inbox.Items;
                        if (items != null)
                        {
                            ItemsEvents_ItemAddEventHandler handler = item =>
                            {
                                try
                                {
                                    if (item is MailItem mailItem)
                                    {
                                        string entryId = mailItem.EntryID ?? string.Empty;
                                        if (!string.IsNullOrEmpty(entryId))
                                        {
                                            _onNewMailItem(entryId, storeId);
                                        }
                                    }
                                }
                                catch { }
                            };

                            items.ItemAdd += handler;
                            _watchedItems.Add(items);
                            _handlers.Add(handler);
                            _watchedFolders.Add(inbox);
                            inbox = null;
                        }
                    }
                }
                catch { }
                finally
                {
                    ComUtils.Release(inbox);
                    ComUtils.Release(store);
                }
            }
        }
        catch { }
        finally
        {
            ComUtils.Release(stores);
        }
    }

    public void UnregisterWatchers()
    {
        for (int i = 0; i < _watchedItems.Count; i++)
        {
            try
            {
                _watchedItems[i].ItemAdd -= _handlers[i];
            }
            catch { }
            finally
            {
                ComUtils.Release(_watchedItems[i]);
            }
        }
        _watchedItems.Clear();
        _handlers.Clear();

        foreach (MAPIFolder folder in _watchedFolders)
        {
            ComUtils.Release(folder);
        }
        _watchedFolders.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        UnregisterWatchers();
    }
}
