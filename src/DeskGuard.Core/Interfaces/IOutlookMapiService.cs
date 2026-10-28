namespace DeskGuard.Core.Interfaces;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DeskGuard.Core.Models;

public sealed class DuplicateScanResult
{
    public List<DuplicateItem> Items { get; set; } = [];
    public int ScannedCount { get; set; }
    public bool Paused { get; set; }
}

public sealed class DeleteResult
{
    public int Count { get; set; }
    public List<string> DeletedIds { get; set; } = [];
    public bool Success { get; set; }
}

public sealed class ExistenceCheckResult
{
    public List<string> RemovedEntryIds { get; set; } = [];
    public List<(string EntryId, string FolderName, string FolderPath)> UserMovedEmails { get; set; } = [];
}

public sealed class DuplicateCleanupResult
{
    public int SuccessCount { get; set; }
    public int SkippedCount { get; set; }
    public string Mode { get; set; } = "safe";
    public List<(string EntryId, string Reason)> SkippedDetails { get; set; } = [];
}

public interface IOutlookMapiService : IDisposable
{
    event Action<EmailRecord>? EmailScanned;
    event Action<string>? LogProgress;
    event Action<string, string>? EngineStatusChanged;
    event Action<int, int>? SweepProgress;
    event Action<int, string, string>? DuplicateProgress;

    bool IsConnected { get; }
    bool IsWatching { get; }

    void SetScanSpeed(int speed);
    void UpdateConfiguration(ScanConfiguration config);

    Task<bool> StartLiveWatcherAsync(CancellationToken cancellationToken);
    Task StopLiveWatcherAsync();
    Task ResetConnectionAsync(CancellationToken cancellationToken = default);

    Task RunSweepAsync(DateTime sinceUtc, CancellationToken cancellationToken);
    Task RunCrawlerAsync(CancellationToken cancellationToken);

    Task<DuplicateScanResult> ScanDuplicatesAsync(CancellationToken cancellationToken);
    Task<DuplicateScanResult> ScanDuplicatesAsync(bool includeJunk, CancellationToken cancellationToken = default);
    Task<DuplicateCleanupResult> CleanDuplicatesAsync(IEnumerable<DuplicateItem> targetItems, string mode = "safe", CancellationToken cancellationToken = default);

    Task<bool> ReleaseEmailAsync(string entryId, string storeId, string fingerprint, int targetFolderType = 6, CancellationToken cancellationToken = default);
    Task<bool> QuarantineEmailAsync(string entryId, string storeId, int targetFolderType = 23, CancellationToken cancellationToken = default);
    Task<DeleteResult> DeleteEmailsAsync(IEnumerable<string> entryIds, Dictionary<string, string>? storeIds = null, CancellationToken cancellationToken = default);
    Task<bool> OpenInOutlookAsync(string entryId, string storeId, string? altEntryId = null, string? messageId = null, CancellationToken cancellationToken = default);
    Task<ExistenceCheckResult> CheckExistenceAsync(IEnumerable<EmailRecord> items, CancellationToken cancellationToken = default);
    Task<List<AttachmentInfo>> GetAttachmentsWithHashesAsync(string entryId, string storeId, CancellationToken cancellationToken = default);
    void DrainRetryQueue();
    void ClearProcessedCache(bool clearProtected = false);
}
