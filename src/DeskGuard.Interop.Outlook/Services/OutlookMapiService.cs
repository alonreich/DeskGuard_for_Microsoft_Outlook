namespace DeskGuard.Interop.Outlook.Services;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Concurrency;
using DeskGuard.Core.Interfaces;
using DeskGuard.Core.Models;
using DeskGuard.Core.Scoring;
using DeskGuard.Core.Serialization;
using DeskGuard.Interop.Outlook.Com;
using DeskGuard.Interop.Outlook.Sta;
using Microsoft.Office.Interop.Outlook;
using Exception = System.Exception;

public sealed class OutlookMapiService : IOutlookMapiService
{
    private readonly OutlookStaCoordinator _coordinator;
    private readonly IVaultRepository _vault;
    private readonly ScoringEngine _scoringEngine;
    private ScanConfiguration _config;
    private readonly OutlookLiveWatcher _liveWatcher;

    private Application? _outlookApp;
    private NameSpace? _mapiNamespace;

    private int _scanDelayMs = 200;
    private readonly ConcurrentDictionary<string, byte> _releasedFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _userMovedFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _processedFingerprints = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _moveFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<(string EntryId, string StoreId)> _retryQueue = new();

    public event Action<EmailRecord>? EmailScanned;
    public event Action<string>? LogProgress;
    public event Action<string, string>? EngineStatusChanged;
    public event Action<int, int>? SweepProgress;
    public event Action<int, string, string>? DuplicateProgress;

    public OutlookMapiService(
        OutlookStaCoordinator coordinator,
        IVaultRepository vault,
        ScanConfiguration? initialConfig = null)
    {
        _coordinator = coordinator;
        _vault = vault;
        _scoringEngine = new ScoringEngine();
        _config = initialConfig ?? new ScanConfiguration();

        SetScanSpeed(_config.ScanningSpeed);

        // Load protected fingerprints from vault into memory
        foreach (string fp in _vault.GetProtected("released")) _releasedFingerprints.TryAdd(fp, 0);
        foreach (string fp in _vault.GetProtected("user_moved")) _userMovedFingerprints.TryAdd(fp, 0);
        foreach (string fp in _vault.GetProcessedKeys()) _processedFingerprints.TryAdd(fp, 0);

        _liveWatcher = new OutlookLiveWatcher(_coordinator, (entryId, storeId) =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _coordinator.EnqueueAsync(ct =>
                    {
                        ProcessSingleEmail(entryId, storeId, isLiveArrival: true);
                    }, JobPriority.P0, $"LiveMail:{entryId}");
                }
                catch (Exception ex)
                {
                    LogProgress?.Invoke($"[OutlookMapiService] Failed to process live mail {entryId}: {ex.Message}. Queued for retry.");
                    _retryQueue.Enqueue((entryId, storeId));
                }
            });
        });
    }

    public void SetScanSpeed(int speed)
    {
        int v = Math.Clamp(speed, 10, 100);
        _scanDelayMs = (100 - v) * 4;
    }

    public void UpdateConfiguration(ScanConfiguration config)
    {
        _config = config ?? new ScanConfiguration();
        SetScanSpeed(_config.ScanningSpeed);
    }

    private void EnsureOutlookConnected()
    {
        if (_outlookApp != null && _mapiNamespace != null) return;

        try
        {
            DeskGuard.Core.Logging.AppLogger.Info("[OutlookMapiService] Connecting to Outlook COM Application...");
            object? rawApp = ComUtils.CreateOrGetOutlook();
            if (rawApp == null)
            {
                _outlookApp = null;
                _mapiNamespace = null;
                DeskGuard.Core.Logging.AppLogger.Warn("[OutlookMapiService] CreateOrGetOutlook returned null.");
                return;
            }

            DeskGuard.Core.Logging.AppLogger.Info($"[OutlookMapiService] CreateOrGetOutlook returned object of type: {rawApp.GetType().FullName}. Casting to Application...");
            _outlookApp = rawApp as Application;
            if (_outlookApp != null)
            {
                _mapiNamespace = _outlookApp.GetNamespace("MAPI");
                int storeCount = 0;
                try { storeCount = _mapiNamespace?.Stores?.Count ?? 0; } catch { }
                DeskGuard.Core.Logging.AppLogger.Info($"[OutlookMapiService] Connected to Outlook successfully. Active stores: {storeCount}");
            }
            else
            {
                _mapiNamespace = null;
                DeskGuard.Core.Logging.AppLogger.Warn($"[OutlookMapiService] Failed to cast {rawApp.GetType().FullName} to Microsoft.Office.Interop.Outlook.Application.");
            }
        }
        catch (Exception ex)
        {
            ComUtils.Release(_mapiNamespace);
            ComUtils.Release(_outlookApp);
            _outlookApp = null;
            _mapiNamespace = null;
            string err = $"[OutlookMapiService] Outlook connection attempt failed: {ex.Message}";
            LogProgress?.Invoke(err);
            DeskGuard.Core.Logging.AppLogger.Error(err, ex);
        }
    }

    public bool IsConnected => _mapiNamespace != null;
    public bool IsWatching => _liveWatcher.HasActiveWatchers;

    public Task<bool> StartLiveWatcherAsync(CancellationToken cancellationToken)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace != null)
            {
                _liveWatcher.RegisterWatchers(_mapiNamespace);
                bool watching = _liveWatcher.HasActiveWatchers;
                if (watching)
                {
                    LogProgress?.Invoke("Real-time live inbox monitoring registered successfully across all stores.");
                    EngineStatusChanged?.Invoke("Running", "Active");
                    DrainRetryQueue();
                }
                return watching;
            }
            return false;
        }, JobPriority.P1, "StartLiveWatcher", cancellationToken);
    }

    public void DrainRetryQueue()
    {
        while (_retryQueue.TryDequeue(out var item))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _coordinator.EnqueueAsync(ct =>
                    {
                        ProcessSingleEmail(item.EntryId, item.StoreId, isLiveArrival: true);
                    }, JobPriority.P0, $"RetryLiveMail:{item.EntryId}");
                }
                catch (Exception ex)
                {
                    LogProgress?.Invoke($"[OutlookMapiService] Retry failed for {item.EntryId}: {ex.Message}");
                    _retryQueue.Enqueue(item);
                }
            });
        }
    }

    public void ClearProcessedCache(bool clearProtected = false)
    {
        _processedFingerprints.Clear();
        _moveFailures.Clear();
        while (_retryQueue.TryDequeue(out _)) { }
        if (clearProtected)
        {
            _releasedFingerprints.Clear();
            _userMovedFingerprints.Clear();
        }
    }

    public Task StopLiveWatcherAsync()
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            _liveWatcher.UnregisterWatchers();
            LogProgress?.Invoke("Real-time live inbox monitoring stopped.");
            EngineStatusChanged?.Invoke("Stopped", "Idle");
        }, JobPriority.P1, "StopLiveWatcher");
    }

    public Task ResetConnectionAsync() => ResetConnectionAsync(default);

    public Task ResetConnectionAsync(CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            _liveWatcher.UnregisterWatchers();
            ComUtils.Release(_mapiNamespace);
            ComUtils.Release(_outlookApp);
            _mapiNamespace = null;
            _outlookApp = null;
            EngineStatusChanged?.Invoke("Standby", "Waiting for Outlook");
        }, JobPriority.P1, "ResetConnection", cancellationToken);
    }

    public Task RunSweepAsync(DateTime sinceUtc, CancellationToken cancellationToken)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null)
            {
                string warnMsg = "[RunSweepAsync] Sweep aborted: Outlook MAPI session is not available.";
                LogProgress?.Invoke(warnMsg);
                DeskGuard.Core.Logging.AppLogger.Warn(warnMsg);
                return;
            }

            string? filter = sinceUtc == DateTime.MinValue
                ? null
                : OutlookMailboxCrawler.GetReceivedSinceFilter(sinceUtc);

            string startMsg = sinceUtc == DateTime.MinValue
                ? "Starting full mailbox sweep across all historical folders..."
                : $"Starting sweep for items received since {sinceUtc:u}...";
            LogProgress?.Invoke(startMsg);
            DeskGuard.Core.Logging.AppLogger.Info($"[RunSweepAsync] {startMsg} (Filter: {filter ?? "none"})");

            List<CrawledFolderEntry> folders = OutlookMailboxCrawler.DiscoverMailFolders(_mapiNamespace);
            DeskGuard.Core.Logging.AppLogger.Info($"[RunSweepAsync] Discovered {folders.Count} mail folders across Outlook stores.");

            int totalSwept = 0;
            foreach (CrawledFolderEntry folder in folders)
            {
                if (ct.IsCancellationRequested) break;
                List<CrawledEmailSummary> folderItems = OutlookMailboxCrawler.ReadFolderItems(_mapiNamespace, folder, filter);
                if (folderItems.Count == 0) continue;

                DeskGuard.Core.Logging.AppLogger.Info($"[RunSweepAsync] Inspecting folder '{folder.FolderName}' ({folder.StoreName}): {folderItems.Count} items.");

                foreach (CrawledEmailSummary item in folderItems)
                {
                    if (ct.IsCancellationRequested) break;
                    if (_processedFingerprints.ContainsKey(item.EntryId)) continue;
                    if (!string.IsNullOrEmpty(item.MessageId) && _processedFingerprints.ContainsKey($"mid:{item.MessageId}")) continue;

                    ProcessSingleEmail(item.EntryId, item.StoreId, isLiveArrival: false);
                    totalSwept++;

                    if (totalSwept % 5 == 0 || totalSwept == 1)
                    {
                        SweepProgress?.Invoke(totalSwept, totalSwept + folderItems.Count);
                    }

                    _coordinator.YieldIfHighPriority(ct);

                    if (_scanDelayMs > 0)
                    {
                        OutlookStaCoordinator.PumpDelay(_scanDelayMs, ct);
                    }
                }
            }

            SweepProgress?.Invoke(totalSwept, totalSwept);
            string finishedMsg = $"Sweep completed: {totalSwept} items evaluated.";
            LogProgress?.Invoke(finishedMsg);
            DeskGuard.Core.Logging.AppLogger.Info($"[RunSweepAsync] {finishedMsg}");
        }, JobPriority.P2, "SweepMailbox", cancellationToken);
    }

    public Task RunCrawlerAsync(CancellationToken cancellationToken)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return;

            LogProgress?.Invoke("Starting full background mailbox crawl and search indexing...");
            List<CrawledFolderEntry> folders = OutlookMailboxCrawler.DiscoverMailFolders(_mapiNamespace);
            HashSet<string> knownIds = new(_vault.GetAllEntryIds(), StringComparer.OrdinalIgnoreCase);

            int indexedCount = 0;
            foreach (CrawledFolderEntry folder in folders)
            {
                if (ct.IsCancellationRequested) break;

                List<CrawledEmailSummary> items = OutlookMailboxCrawler.ReadFolderItems(_mapiNamespace, folder);
                HashSet<string> folderPresentKeys = new(StringComparer.OrdinalIgnoreCase);

                foreach (CrawledEmailSummary item in items)
                {
                    folderPresentKeys.Add(item.EntryId);
                    if (!string.IsNullOrEmpty(item.MessageId))
                    {
                        folderPresentKeys.Add($"mid:{item.MessageId}");
                    }

                    if (!knownIds.Contains(item.EntryId))
                    {
                        IndexSingleEmail(item.EntryId, item.StoreId, folder.FolderName);
                        knownIds.Add(item.EntryId);
                        indexedCount++;

                        _coordinator.YieldIfHighPriority(ct);

                        if (_scanDelayMs > 0)
                        {
                            OutlookStaCoordinator.PumpDelay(_scanDelayMs, ct);
                        }
                    }
                    else
                    {
                        _coordinator.YieldIfHighPriority(ct);
                    }
                }

                // Reconcile folder state
                var (hidden, skipped) = _vault.ReconcileFolder(folder.StoreId, folder.FolderName, folderPresentKeys);
                if (hidden > 0)
                {
                    LogProgress?.Invoke($"Reconciliation: Marked {hidden} deleted/moved items as hidden in '{folder.FolderName}'.");
                }
                else if (skipped)
                {
                    LogProgress?.Invoke($"Reconciliation: Skipped '{folder.FolderName}' (>50% missing safety check).");
                }
            }

            LogProgress?.Invoke($"Crawl complete: {indexedCount} new items indexed into vault.");
        }, JobPriority.P2, "CrawlMailbox", cancellationToken);
    }

    public Task<DuplicateScanResult> ScanDuplicatesAsync(CancellationToken cancellationToken) =>
        ScanDuplicatesAsync(false, cancellationToken);

    public Task<DuplicateScanResult> ScanDuplicatesAsync(bool includeJunk, CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null)
            {
                string errMsg = "Cannot perform duplicate scan: Unable to connect to Microsoft Outlook MAPI session. Verify Outlook is running.";
                DeskGuard.Core.Logging.AppLogger.Warn($"[ScanDuplicatesAsync] {errMsg}");
                throw new InvalidOperationException(errMsg);
            }

            LogProgress?.Invoke("Duplicate Scan: Discovering duplicates across all mailboxes...");
            DeskGuard.Core.Logging.AppLogger.Info("[ScanDuplicatesAsync] Duplicate scan started. Traversing folder tree...");
            List<CrawledFolderEntry> folders = OutlookMailboxCrawler.DiscoverMailFolders(_mapiNamespace, includeJunk);
            DeskGuard.Core.Logging.AppLogger.Info($"[ScanDuplicatesAsync] Folders discovered: {folders.Count} (includeJunk={includeJunk})");

            Dictionary<string, List<CrawledEmailSummary>> groupsByDna = new(StringComparer.OrdinalIgnoreCase);
            int scanned = 0;

            List<DuplicateItem> ElectDuplicates(Dictionary<string, List<CrawledEmailSummary>> groups)
            {
                List<DuplicateItem> result = [];
                foreach (var (dna, list) in groups)
                {
                    if (list.Count < 2) continue;

                    CrawledEmailSummary survivor = list[0];
                    for (int i = 1; i < list.Count; i++)
                    {
                        var candidate = list[i];
                        bool survivorIsJunk = survivor.FolderPath.Contains("Junk", StringComparison.OrdinalIgnoreCase);
                        bool candidateIsJunk = candidate.FolderPath.Contains("Junk", StringComparison.OrdinalIgnoreCase);

                        if (survivorIsJunk && !candidateIsJunk)
                        {
                            survivor = candidate;
                        }
                        else if (!survivorIsJunk && candidateIsJunk)
                        {
                            // keep current survivor
                        }
                        else if (candidate.Size > survivor.Size)
                        {
                            survivor = candidate;
                        }
                    }

                    foreach (CrawledEmailSummary dup in list)
                    {
                        if (dup.EntryId == survivor.EntryId) continue;

                        result.Add(new DuplicateItem
                        {
                            EntryId = dup.EntryId,
                            StoreId = dup.StoreId,
                            Subject = !string.IsNullOrEmpty(dup.Subject) ? dup.Subject : (!string.IsNullOrEmpty(survivor.Subject) ? survivor.Subject : "(No Subject)"),
                            Sender = !string.IsNullOrEmpty(dup.Sender) ? dup.Sender : (!string.IsNullOrEmpty(survivor.Sender) ? survivor.Sender : "Unknown"),
                            Timestamp = !string.IsNullOrEmpty(dup.Timestamp) ? dup.Timestamp : survivor.Timestamp,
                            Folder = dup.FolderPath,
                            Size = dup.Size,
                            Dna = dna,
                            SurvivorId = survivor.EntryId,
                            SurvivorStoreId = survivor.StoreId,
                            SurvivorFolder = survivor.FolderPath
                        });
                    }
                }
                return result;
            }

            foreach (CrawledFolderEntry folder in folders)
            {
                if (ct.IsCancellationRequested)
                {
                    List<DuplicateItem> partialDups = ElectDuplicates(groupsByDna);
                    return new DuplicateScanResult
                    {
                        Items = partialDups,
                        ScannedCount = scanned,
                        Paused = true
                    };
                }

                DuplicateProgress?.Invoke(scanned, folder.StoreName, folder.FolderName);
                List<CrawledEmailSummary> items = OutlookMailboxCrawler.ReadFolderItems(_mapiNamespace, folder);
                foreach (CrawledEmailSummary item in items)
                {
                    if (ct.IsCancellationRequested)
                    {
                        List<DuplicateItem> partialDups = ElectDuplicates(groupsByDna);
                        return new DuplicateScanResult
                        {
                            Items = partialDups,
                            ScannedCount = scanned,
                            Paused = true
                        };
                    }

                    scanned++;
                    if (scanned % 25 == 0)
                    {
                        DuplicateProgress?.Invoke(scanned, folder.StoreName, folder.FolderName);
                    }
                    string dna = string.Empty;
                    if (!string.IsNullOrEmpty(item.MessageId))
                    {
                        dna = OutlookItemAccessor.ComputeSha256($"mid|{item.MessageId}|{folder.IsSent}");
                    }
                    else
                    {
                        MailItem? mi = null;
                        try
                        {
                            mi = _mapiNamespace.GetItemFromID(item.EntryId, item.StoreId) as MailItem;
                            if (mi != null) dna = OutlookItemAccessor.GetItemDna(mi);
                        }
                        catch { }
                        finally { ComUtils.Release(mi); }
                    }

                    _coordinator.YieldIfHighPriority(ct);

                    if (string.IsNullOrEmpty(dna)) continue;

                    if (!groupsByDna.TryGetValue(dna, out var list))
                    {
                        list = [];
                        groupsByDna[dna] = list;
                    }
                    list.Add(item);
                }
            }

            List<DuplicateItem> duplicates = ElectDuplicates(groupsByDna);
            _vault.SaveDuplicates(duplicates, "Finished");
            LogProgress?.Invoke($"Duplicate Scan finished: {duplicates.Count} duplicate items found across {scanned} scanned.");

            return new DuplicateScanResult
            {
                Items = duplicates,
                ScannedCount = scanned,
                Paused = false
            };
        }, JobPriority.P3, "ScanDuplicates", cancellationToken);
    }

    public Task<DuplicateCleanupResult> CleanDuplicatesAsync(
        IEnumerable<DuplicateItem> targetItems,
        string mode = "safe",
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return new DuplicateCleanupResult();

            int successCount = 0;
            int skippedCount = 0;
            List<(string EntryId, string Reason)> skipped = [];
            bool isPurge = string.Equals(mode, "purge", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(mode, "permanent", StringComparison.OrdinalIgnoreCase);

            foreach (DuplicateItem entry in targetItems)
            {
                if (ct.IsCancellationRequested) break;
                string dupId = entry.EntryId;
                string survivorId = entry.SurvivorId;

                if (string.IsNullOrEmpty(dupId) || string.IsNullOrEmpty(survivorId))
                {
                    skippedCount++;
                    skipped.Add((dupId, "Survivor missing or modified"));
                    continue;
                }

                if (dupId == survivorId)
                {
                    skippedCount++;
                    skipped.Add((dupId, "Target item is the survivor itself"));
                    continue;
                }

                MailItem? survivorItem = null;
                MailItem? targetItem = null;
                MAPIFolder? deletedFolder = null;

                try
                {
                    try { survivorItem = _mapiNamespace.GetItemFromID(survivorId, entry.SurvivorStoreId) as MailItem; } catch { }
                    survivorItem ??= _mapiNamespace.GetItemFromID(survivorId) as MailItem;

                    if (survivorItem == null)
                    {
                        skippedCount++;
                        skipped.Add((dupId, "Survivor item does not exist in Outlook"));
                        continue;
                    }

                    // Check if survivor is in Deleted Items
                    MAPIFolder? sParent = survivorItem.Parent as MAPIFolder;
                    if (sParent != null)
                    {
                        Store? sStore = sParent.Store;
                        if (sStore != null)
                        {
                            deletedFolder = sStore.GetDefaultFolder(OlDefaultFolders.olFolderDeletedItems);
                            if (deletedFolder != null && sParent.EntryID == deletedFolder.EntryID)
                            {
                                skippedCount++;
                                skipped.Add((dupId, "Survivor is in Deleted Items folder"));
                                ComUtils.Release(deletedFolder);
                                deletedFolder = null;
                                ComUtils.Release(sStore);
                                ComUtils.Release(sParent);
                                continue;
                            }
                            ComUtils.Release(deletedFolder);
                            deletedFolder = null;
                            ComUtils.Release(sStore);
                        }
                        ComUtils.Release(sParent);
                    }

                    string survivorDna = OutlookItemAccessor.GetItemDna(survivorItem);
                    string survivorFp = OutlookItemAccessor.GetFingerprint(survivorItem);
                    string survivorLegacyFp = OutlookItemAccessor.GetLegacyFingerprint(survivorItem);
                    string survivorMid = OutlookItemAccessor.GetPropertySafe(survivorItem, MapiPropertyTags.PrInternetMessageId, string.Empty);
                    bool survivorMatch = string.IsNullOrEmpty(entry.Dna) ||
                                         string.Equals(survivorDna, entry.Dna, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(survivorFp, entry.Dna, StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(survivorLegacyFp, entry.Dna, StringComparison.OrdinalIgnoreCase) ||
                                         (!string.IsNullOrEmpty(survivorMid) && (string.Equals(entry.Dna, survivorMid, StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Dna, $"mid:{survivorMid}", StringComparison.OrdinalIgnoreCase)));
                    if (!survivorMatch)
                    {
                        skippedCount++;
                        skipped.Add((dupId, "Survivor DNA mismatch"));
                        continue;
                    }

                    try { targetItem = _mapiNamespace.GetItemFromID(dupId, entry.StoreId) as MailItem; } catch { }
                    targetItem ??= _mapiNamespace.GetItemFromID(dupId) as MailItem;

                    if (targetItem == null)
                    {
                        skippedCount++;
                        skipped.Add((dupId, "Target duplicate item not found"));
                        continue;
                    }

                    if (targetItem.EntryID == survivorItem.EntryID)
                    {
                        skippedCount++;
                        skipped.Add((dupId, "Target resolved to survivor"));
                        continue;
                    }

                    // Pre-flight check: Verify target duplicate DNA matches expected DNA before deletion/move
                    string targetDna = OutlookItemAccessor.GetItemDna(targetItem);
                    string targetFp = OutlookItemAccessor.GetFingerprint(targetItem);
                    string targetLegacyFp = OutlookItemAccessor.GetLegacyFingerprint(targetItem);
                    string targetMid = OutlookItemAccessor.GetPropertySafe(targetItem, MapiPropertyTags.PrInternetMessageId, string.Empty);
                    bool targetMatch = string.IsNullOrEmpty(entry.Dna) ||
                                      string.Equals(targetDna, entry.Dna, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(targetFp, entry.Dna, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(targetLegacyFp, entry.Dna, StringComparison.OrdinalIgnoreCase) ||
                                      (!string.IsNullOrEmpty(targetMid) && (string.Equals(entry.Dna, targetMid, StringComparison.OrdinalIgnoreCase) || string.Equals(entry.Dna, $"mid:{targetMid}", StringComparison.OrdinalIgnoreCase))) ||
                                      string.Equals(targetDna, survivorDna, StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(targetFp, survivorFp, StringComparison.OrdinalIgnoreCase);
                    if (!targetMatch)
                    {
                        skippedCount++;
                        skipped.Add((dupId, "Target duplicate DNA mismatch"));
                        continue;
                    }

                    if (isPurge)
                    {
                        targetItem.Delete();
                        successCount++;
                        _vault.DeleteEmails([dupId]);
                        LogProgress?.Invoke($"[PURGE] Permanently deleted duplicate: {entry.Subject} ({dupId})");
                    }
                    else
                    {
                        MAPIFolder? tParent = targetItem.Parent as MAPIFolder;
                        if (tParent != null)
                        {
                            Store? tStore = tParent.Store;
                            if (tStore != null)
                            {
                                deletedFolder = tStore.GetDefaultFolder(OlDefaultFolders.olFolderDeletedItems);
                                if (deletedFolder != null)
                                {
                                    MailItem? moved = OutlookActionsService.RobustMove(targetItem, deletedFolder);
                                    if (moved != null)
                                    {
                                        successCount++;
                                        _vault.HideEmails([dupId]);
                                        LogProgress?.Invoke($"[SAFE] Moved duplicate to Deleted Items: {entry.Subject} ({dupId})");
                                        ComUtils.Release(moved);
                                    }
                                    else
                                    {
                                        skippedCount++;
                                        skipped.Add((dupId, "Failed to move duplicate to Deleted Items"));
                                    }
                                }
                                else
                                {
                                    skippedCount++;
                                    skipped.Add((dupId, "Deleted Items folder not found"));
                                }
                                ComUtils.Release(tStore);
                            }
                            else
                            {
                                skippedCount++;
                                skipped.Add((dupId, "Mailbox store not found"));
                            }
                            ComUtils.Release(tParent);
                        }
                        else
                        {
                            skippedCount++;
                            skipped.Add((dupId, "Parent folder not found"));
                        }
                    }
                }
                catch (Exception ex)
                {
                    skippedCount++;
                    skipped.Add((dupId, ex.Message));
                }
                finally
                {
                    ComUtils.Release(deletedFolder);
                    ComUtils.Release(targetItem);
                    ComUtils.Release(survivorItem);
                }
            }

            return new DuplicateCleanupResult
            {
                SuccessCount = successCount,
                SkippedCount = skippedCount,
                Mode = mode,
                SkippedDetails = skipped
            };
        }, JobPriority.P1, "CleanDuplicates", cancellationToken);
    }

    public Task<bool> ReleaseEmailAsync(
        string entryId,
        string storeId,
        string fingerprint,
        int targetFolderType = 6,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return false;

            MailItem? item = null;
            MAPIFolder? targetFolder = null;
            MailItem? moved = null;

            try
            {
                if (!string.IsNullOrEmpty(storeId))
                {
                    try { item = _mapiNamespace.GetItemFromID(entryId, storeId) as MailItem; } catch { }
                }
                item ??= _mapiNamespace.GetItemFromID(entryId) as MailItem;
                if (item == null) return false;

                string stableFp = OutlookItemAccessor.GetFingerprint(item);
                string legacyFp = OutlookItemAccessor.GetLegacyFingerprint(item);

                foreach (string fp in new[] { fingerprint, stableFp, legacyFp })
                {
                    if (!string.IsNullOrEmpty(fp))
                    {
                        _releasedFingerprints.TryAdd(fp, 0);
                        _vault.AddProtected([fp], "released");
                    }
                }

                targetFolder = OutlookActionsService.GetTargetFolderSafe(item, (OlDefaultFolders)targetFolderType)
                               ?? _mapiNamespace.GetDefaultFolder((OlDefaultFolders)targetFolderType);
                if (targetFolder == null) return false;

                // Unmark Junk category
                try
                {
                    string cats = item.Categories ?? string.Empty;
                    if (cats.Contains("DeskGuard", StringComparison.OrdinalIgnoreCase))
                    {
                        item.Categories = string.Join(", ", cats.Split(',')
                            .Select(c => c.Trim())
                            .Where(c => !c.StartsWith("DeskGuard", StringComparison.OrdinalIgnoreCase)));
                        item.Save();
                    }
                }
                catch { }

                moved = OutlookActionsService.RobustMove(item, targetFolder, stableFp, "Release");
                if (moved != null)
                {
                    string newId = moved.EntryID;
                    _vault.UpdateVerdict(entryId, stableFp, "Safe", "User Released", 100.0, newId, targetFolder.Name);
                    _vault.RecordEvent(new SecurityEvent
                    {
                        Kind = "Release",
                        MessageKey = stableFp,
                        EntryId = newId,
                        Subject = OutlookItemAccessor.GetItemSubject(moved),
                        Sender = OutlookItemAccessor.GetItemSender(moved),
                        ToFolder = targetFolder.Name,
                        Score = 100.0
                    });

                    LogProgress?.Invoke($"Released email '{OutlookItemAccessor.GetItemSubject(moved)}' to {targetFolder.Name}.");
                    return true;
                }
                return false;
            }
            finally
            {
                ComUtils.Release(moved);
                ComUtils.Release(targetFolder);
                ComUtils.Release(item);
            }
        }, JobPriority.P1, $"Release:{entryId}", cancellationToken);
    }

    public Task<bool> QuarantineEmailAsync(
        string entryId,
        string storeId,
        int targetFolderType = 23,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return false;

            MailItem? item = null;
            MAPIFolder? targetFolder = null;
            MailItem? moved = null;

            try
            {
                if (!string.IsNullOrEmpty(storeId))
                {
                    try { item = _mapiNamespace.GetItemFromID(entryId, storeId) as MailItem; } catch { }
                }
                item ??= _mapiNamespace.GetItemFromID(entryId) as MailItem;
                if (item == null) return false;

                targetFolder = OutlookActionsService.GetQuarantineSubfolder(_mapiNamespace, item, "Threats");
                if (targetFolder == null) return false;

                string fp = OutlookItemAccessor.GetFingerprint(item);
                moved = OutlookActionsService.RobustMove(item, targetFolder, fp, "Quarantine");
                if (moved != null)
                {
                    string newId = moved.EntryID;
                    _vault.UpdateVerdict(entryId, fp, "Malicious", "High Risk", 0.0, newId, targetFolder.Name);
                    _vault.RecordEvent(new SecurityEvent
                    {
                        Kind = "Quarantine",
                        MessageKey = fp,
                        EntryId = newId,
                        Subject = OutlookItemAccessor.GetItemSubject(moved),
                        Sender = OutlookItemAccessor.GetItemSender(moved),
                        ToFolder = targetFolder.Name,
                        Score = 0.0
                    });
                    LogProgress?.Invoke($"Quarantined email '{OutlookItemAccessor.GetItemSubject(moved)}' to {targetFolder.Name}.");
                    return true;
                }
                return false;
            }
            finally
            {
                ComUtils.Release(moved);
                ComUtils.Release(targetFolder);
                ComUtils.Release(item);
            }
        }, JobPriority.P1, $"Quarantine:{entryId}", cancellationToken);
    }

    public Task<DeleteResult> DeleteEmailsAsync(
        IEnumerable<string> entryIds,
        Dictionary<string, string>? storeIds = null,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return new DeleteResult();

            int count = 0;
            List<string> deleted = [];

            foreach (string id in entryIds)
            {
                MailItem? item = null;
                try
                {
                    string? sid = storeIds?.GetValueOrDefault(id);
                    if (!string.IsNullOrEmpty(sid))
                    {
                        try { item = _mapiNamespace.GetItemFromID(id, sid) as MailItem; } catch { }
                    }
                    item ??= _mapiNamespace.GetItemFromID(id) as MailItem;
                    if (item != null)
                    {
                        item.Delete();
                        count++;
                        deleted.Add(id);
                    }
                }
                catch { }
                finally
                {
                    ComUtils.Release(item);
                }
            }

            if (deleted.Count > 0)
            {
                _vault.DeleteEmails(deleted);
            }

            return new DeleteResult
            {
                Count = count,
                DeletedIds = deleted,
                Success = true
            };
        }, JobPriority.P1, "DeleteEmails", cancellationToken);
    }

    public Task<bool> OpenInOutlookAsync(
        string entryId,
        string storeId,
        string? altEntryId = null,
        string? messageId = null,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return false;

            MailItem? item = null;
            try
            {
                if (!string.IsNullOrEmpty(storeId))
                {
                    try { item = _mapiNamespace.GetItemFromID(entryId, storeId) as MailItem; } catch { }
                }
                item ??= _mapiNamespace.GetItemFromID(entryId) as MailItem;

                if (item == null && !string.IsNullOrEmpty(altEntryId))
                {
                    try { item = _mapiNamespace.GetItemFromID(altEntryId) as MailItem; } catch { }
                }

                if (item == null && !string.IsNullOrEmpty(messageId))
                {
                    item = OutlookActionsService.FindItemByMessageId(_mapiNamespace, messageId);
                }

                if (item != null)
                {
                    item.Display(false);

                    // Update relocation if entryId changed
                    string newEid = item.EntryID ?? string.Empty;
                    if (!string.IsNullOrEmpty(newEid) && newEid != entryId)
                    {
                        MAPIFolder? p = item.Parent as MAPIFolder;
                        string sid = p?.StoreID ?? storeId;
                        _vault.RelocateEntry(entryId, newEid, sid);
                        ComUtils.Release(p);
                    }
                    return true;
                }
                return false;
            }
            finally
            {
                ComUtils.Release(item);
            }
        }, JobPriority.P1, $"OpenInOutlook:{entryId}", cancellationToken);
    }

    public Task<ExistenceCheckResult> CheckExistenceAsync(
        IEnumerable<EmailRecord> items,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return new ExistenceCheckResult();

            List<string> removed = [];
            List<(string EntryId, string FolderName, string FolderPath)> userMoved = [];

            foreach (EmailRecord entry in items)
            {
                if (ct.IsCancellationRequested) break;
                string id = entry.EntryId;
                if (string.IsNullOrEmpty(id)) continue;

                MailItem? item = null;
                MAPIFolder? parent = null;
                bool exists = false;
                bool isRpcError = false;

                try
                {
                    if (!string.IsNullOrEmpty(entry.StoreId))
                    {
                        try { item = _mapiNamespace.GetItemFromID(id, entry.StoreId) as MailItem; } catch { }
                    }
                    item ??= _mapiNamespace.GetItemFromID(id) as MailItem;

                    if (item != null)
                    {
                        exists = true;
                        parent = item.Parent as MAPIFolder;
                        string parentFolder = parent?.Name ?? "Unknown";
                        string parentPath = parent?.FolderPath ?? string.Empty;

                        bool wasQuarantined = entry.Verdict is "Malicious" or "Spam";
                        bool isInQuarantine = OutlookActionsService.TestIsQuarantineFolder(parent);

                        if (wasQuarantined && !isInQuarantine)
                        {
                            userMoved.Add((id, parentFolder, parentPath));
                            if (!string.IsNullOrEmpty(entry.Fingerprint))
                            {
                                _userMovedFingerprints.TryAdd(entry.Fingerprint, 0);
                                _vault.AddProtected([entry.Fingerprint], "user_moved");
                                _vault.UpdateUserMoved(id, parentFolder);
                            }
                        }
                    }
                }
                catch (COMException ex) when (ex.ErrorCode is -2147418111 or -2147417846 or -2147220948)
                {
                    isRpcError = true;
                }
                catch
                {
                    exists = false;
                }
                finally
                {
                    ComUtils.Release(parent);
                    ComUtils.Release(item);
                }

                if (!exists && !isRpcError)
                {
                    removed.Add(id);
                }
            }

            if (removed.Count > 0)
            {
                _vault.HideEmails(removed);
            }

            return new ExistenceCheckResult
            {
                RemovedEntryIds = removed,
                UserMovedEmails = userMoved
            };
        }, JobPriority.P1, "CheckExistence", cancellationToken);
    }

    public Task<List<AttachmentInfo>> GetAttachmentsWithHashesAsync(
        string entryId,
        string storeId,
        CancellationToken cancellationToken = default)
    {
        return _coordinator.EnqueueAsync(ct =>
        {
            EnsureOutlookConnected();
            if (_mapiNamespace == null) return [];

            MailItem? item = null;
            try
            {
                if (!string.IsNullOrEmpty(storeId))
                {
                    try { item = _mapiNamespace.GetItemFromID(entryId, storeId) as MailItem; } catch { }
                }
                item ??= _mapiNamespace.GetItemFromID(entryId) as MailItem;
                if (item != null)
                {
                    return OutlookItemAccessor.GetAttachmentDetails(item, withHashes: true);
                }
            }
            catch { }
            finally
            {
                ComUtils.Release(item);
            }
            return [];
        }, JobPriority.P1, $"GetAttachments:{entryId}", cancellationToken);
    }

    private void ProcessSingleEmail(
        string entryId,
        string storeId,
        bool isLiveArrival)
    {
        EnsureOutlookConnected();
        if (_mapiNamespace == null) return;

        MailItem? mailItem = null;
        try
        {
            if (!string.IsNullOrEmpty(storeId))
            {
                try { mailItem = _mapiNamespace.GetItemFromID(entryId, storeId) as MailItem; } catch { }
            }
            mailItem ??= _mapiNamespace.GetItemFromID(entryId) as MailItem;
            if (mailItem == null) return;

            string fp = OutlookItemAccessor.GetFingerprint(mailItem);
            string legacyFp = OutlookItemAccessor.GetLegacyFingerprint(mailItem);

            // Invariant: User relocation & release protection
            if (_releasedFingerprints.ContainsKey(fp) || _releasedFingerprints.ContainsKey(legacyFp))
            {
                return;
            }

            bool isUserMoved = _userMovedFingerprints.ContainsKey(fp) || _userMovedFingerprints.ContainsKey(legacyFp);

            string headers = OutlookItemAccessor.GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersA, string.Empty);
            if (string.IsNullOrEmpty(headers)) headers = OutlookItemAccessor.GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersW, string.Empty);

            string body = string.Empty;
            try { body = mailItem.Body ?? string.Empty; } catch { }

            string subject = OutlookItemAccessor.GetItemSubject(mailItem);
            string sender = OutlookItemAccessor.GetItemSender(mailItem);
            string to = string.Empty;
            try { to = mailItem.To ?? string.Empty; } catch { }
            string cc = string.Empty;
            try { cc = mailItem.CC ?? string.Empty; } catch { }

            bool unread = false;
            try { unread = mailItem.UnRead; } catch { }

            string messageId = OutlookItemAccessor.GetPropertySafe(mailItem, MapiPropertyTags.PrInternetMessageId, string.Empty);
            long size = 0;
            try { size = mailItem.Size; } catch { }

            List<AttachmentInfo> attachments = OutlookItemAccessor.GetAttachmentDetails(mailItem, withHashes: false);
            string attsJson = JsonSerializer.Serialize(attachments, DeskGuardJsonContext.Default.ListAttachmentInfo);

            DateTime receivedTime = DateTime.Now;
            try
            {
                DateTime rt = mailItem.ReceivedTime;
                if (rt != default && rt != DateTime.MinValue) receivedTime = rt;
            }
            catch { }

            EmailRecord record = new()
            {
                EntryId = entryId,
                StoreId = storeId,
                Fingerprint = fp,
                Subject = subject,
                Sender = sender,
                Recipient = to,
                Cc = cc,
                Date = receivedTime.ToString("yyyy-MM-dd"),
                Time = receivedTime.ToString("HH:mm:ss"),
                Timestamp = receivedTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Body = body,
                Headers = headers,
                AttachmentsJson = attsJson,
                Unread = unread,
                UserMoved = isUserMoved,
                MessageId = messageId,
                Size = size
            };

            // Check whitelist / blacklist
            SecurityVerdict verdict;
            if (_config.Whitelist.Any(w => sender.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                verdict = new SecurityVerdict { Score = 100.0, Verdict = "Safe", TotalDeduction = 0.0 };
            }
            else if (_config.Blacklist.Any(b => sender.Contains(b, StringComparison.OrdinalIgnoreCase)))
            {
                verdict = new SecurityVerdict { Score = 0.0, Verdict = "Malicious", TotalDeduction = 100.0 };
                verdict.Penalties.Add(new SecurityPenalty { Rule = "BLACKLIST", Key = "heuristics", Points = 100, Evidence = "Sender on Blacklist" });
            }
            else
            {
                verdict = _scoringEngine.ScoreEmail(record, _config, explicitAttachments: attachments);
            }

            record.Verdict = verdict.Verdict;
            record.Score = verdict.Score;
            record.PenaltiesJson = JsonSerializer.Serialize(verdict.Penalties, DeskGuardJsonContext.Default.ListSecurityPenalty);

            // Handle quarantine/move only for live arrivals when not user-moved
            if (isLiveArrival && !isUserMoved && verdict.Verdict is "Malicious" or "Spam")
            {
                MAPIFolder? dest = null;
                if (verdict.Verdict == "Malicious")
                {
                    dest = OutlookActionsService.GetQuarantineSubfolder(_mapiNamespace, mailItem, "Threats");
                }
                else if (string.Equals(_config.SpamDestination, "junk", StringComparison.OrdinalIgnoreCase))
                {
                    dest = OutlookActionsService.GetTargetFolderSafe(mailItem, OlDefaultFolders.olFolderJunk)
                           ?? OutlookActionsService.GetQuarantineSubfolder(_mapiNamespace, mailItem, "Spam");
                }
                else
                {
                    dest = OutlookActionsService.GetQuarantineSubfolder(_mapiNamespace, mailItem, "Spam");
                }

                if (dest != null)
                {
                    try
                    {
                        MailItem? moved = OutlookActionsService.RobustMove(mailItem, dest, fp, verdict.Verdict);
                        if (moved != null)
                        {
                            record.EntryId = moved.EntryID;
                            record.CurrentFolder = dest.Name;

                            if (_config.LabelInOutlook)
                            {
                                SetVerdictCategory(moved, verdict.Verdict);
                            }

                            _vault.RecordEvent(new SecurityEvent
                            {
                                Kind = verdict.Verdict,
                                MessageKey = fp,
                                EntryId = moved.EntryID,
                                Subject = subject,
                                Sender = sender,
                                ToFolder = dest.Name,
                                Score = verdict.Score,
                                ReasonsJson = record.PenaltiesJson
                            });

                            ComUtils.Release(moved);
                        }
                    }
                    finally
                    {
                        ComUtils.Release(dest);
                    }
                }
            }
            else
            {
                if (_config.LabelInOutlook && verdict.Verdict != "Safe")
                {
                    SetVerdictCategory(mailItem, verdict.Verdict);
                }
            }

            _vault.InsertEmail(record);
            _processedFingerprints.TryAdd(fp, 0);
            _processedFingerprints.TryAdd(entryId, 0);
            if (!string.IsNullOrEmpty(record.MessageId)) _processedFingerprints.TryAdd($"mid:{record.MessageId}", 0);
            _vault.AddProcessed([fp]);

            EmailScanned?.Invoke(record);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OutlookMapiService] ProcessSingleEmailAsync error: {ex.Message}");
        }
        finally
        {
            ComUtils.Release(mailItem);
        }
    }

    private void IndexSingleEmail(
        string entryId,
        string storeId,
        string folderPath)
    {
        EnsureOutlookConnected();
        if (_mapiNamespace == null) return;

        MailItem? mailItem = null;
        try
        {
            if (!string.IsNullOrEmpty(storeId))
            {
                try { mailItem = _mapiNamespace.GetItemFromID(entryId, storeId) as MailItem; } catch { }
            }
            mailItem ??= _mapiNamespace.GetItemFromID(entryId) as MailItem;
            if (mailItem == null) return;

            string fp = OutlookItemAccessor.GetFingerprint(mailItem);
            string subject = OutlookItemAccessor.GetItemSubject(mailItem);
            string sender = OutlookItemAccessor.GetItemSender(mailItem);
            string to = string.Empty; try { to = mailItem.To ?? string.Empty; } catch { }
            string cc = string.Empty; try { cc = mailItem.CC ?? string.Empty; } catch { }
            string body = string.Empty; try { body = mailItem.Body ?? string.Empty; } catch { }
            string headers = OutlookItemAccessor.GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersA, string.Empty);
            if (string.IsNullOrEmpty(headers)) headers = OutlookItemAccessor.GetPropertySafe(mailItem, MapiPropertyTags.PrTransportMessageHeadersW, string.Empty);

            string msgId = OutlookItemAccessor.GetPropertySafe(mailItem, MapiPropertyTags.PrInternetMessageId, string.Empty);
            bool unread = false; try { unread = mailItem.UnRead; } catch { }
            long size = 0; try { size = mailItem.Size; } catch { }

            List<AttachmentInfo> atts = OutlookItemAccessor.GetAttachmentDetails(mailItem, withHashes: false);
            string attsJson = JsonSerializer.Serialize(atts, DeskGuardJsonContext.Default.ListAttachmentInfo);

            DateTime receivedTime = DateTime.Now;
            try
            {
                DateTime rt = mailItem.ReceivedTime;
                if (rt != default && rt != DateTime.MinValue) receivedTime = rt;
            }
            catch { }

            EmailRecord record = new()
            {
                EntryId = entryId,
                StoreId = storeId,
                Fingerprint = fp,
                Subject = subject,
                Sender = sender,
                Recipient = to,
                Cc = cc,
                Date = receivedTime.ToString("yyyy-MM-dd"),
                Time = receivedTime.ToString("HH:mm:ss"),
                Timestamp = receivedTime.ToString("yyyy-MM-dd HH:mm:ss"),
                Body = body,
                Headers = headers,
                AttachmentsJson = attsJson,
                Unread = unread,
                CurrentFolder = folderPath,
                MessageId = msgId,
                Size = size
            };

            _vault.InsertIndexed(record);
        }
        catch { }
        finally
        {
            ComUtils.Release(mailItem);
        }
    }

    private static void SetVerdictCategory(MailItem item, string verdict)
    {
        try
        {
            string catName = $"DeskGuard: {verdict}";
            string current = item.Categories ?? string.Empty;
            if (!current.Contains(catName, StringComparison.OrdinalIgnoreCase))
            {
                item.Categories = string.IsNullOrWhiteSpace(current) ? catName : $"{current}, {catName}";
                item.Save();
            }
        }
        catch { }
    }

    public void Dispose()
    {
        try
        {
            _coordinator.EnqueueAsync(ct =>
            {
                _liveWatcher.Dispose();
                ComUtils.Release(_mapiNamespace);
                ComUtils.Release(_outlookApp);
                _mapiNamespace = null;
                _outlookApp = null;
            }, JobPriority.P0, "DisposeOutlook").Wait(TimeSpan.FromSeconds(3));
        }
        catch { }
    }
}
