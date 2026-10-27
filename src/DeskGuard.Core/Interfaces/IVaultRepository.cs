namespace DeskGuard.Core.Interfaces;

using System;
using System.Collections.Generic;
using DeskGuard.Core.Models;

public interface IVaultRepository : IDisposable
{
    void Initialize(string dbDir, bool readOnly = false);
    void InsertEmail(EmailRecord item);
    void InsertIndexed(EmailRecord item);
    EmailRecord? GetEmailById(string id);
    List<string> GetAllEntryIds();
    (int Hidden, bool Skipped) ReconcileFolder(string storeId, string folderPath, IReadOnlySet<string> presentKeys);
    void RelocateEntry(string oldEntryId, string newEntryId, string storeId);
    int GetIndexCount();
    bool UpdateVerdict(string entryId, string fingerprint, string verdict, string tier, double score, string? newEntryId = null);
    void HideEmails(IEnumerable<string> entryIds);
    void DeleteEmails(IEnumerable<string> entryIds);
    void AddProcessed(IEnumerable<string> keys);
    List<string> GetProcessedKeys();
    void ClearProcessed();
    void AddProtected(IEnumerable<string> fingerprints, string kind);
    List<string> GetProtected(string kind);
    void ClearProtected();
    void SaveDuplicates(IEnumerable<DuplicateItem> items, string status);
    (List<DuplicateItem> Items, string Status, string SavedAt) GetSavedDuplicates();
    (List<DuplicateItem> Items, int Indexed) FindDuplicates(bool includeJunk = false);
    long? RecordEvent(SecurityEvent ev);
    List<SecurityEvent> GetEvents(int limit = 100, int offset = 0, string? kind = null);
    SearchResult SearchEmails(SearchParameters parameters);
    Dictionary<string, int> GetKnownDomains(int limit = 20000);
    List<string> GetSenderSuggestions(string prefix, int limit = 15);
    EngineStats GetStats();
    bool UpdateUserMoved(string id, string currentFolder);
    Dictionary<string, List<EmailRecord>> GetAllCategorizedEmails(int limitPerCat = 2000);
    void ImportLegacyData(string jsonContent);
}
