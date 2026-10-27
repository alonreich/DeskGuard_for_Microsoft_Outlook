namespace DeskGuard.Core.Concurrency;

public enum JobPriority
{
    P0 = 0, // Real-time new mail arrival (On-Access ItemAdd)
    P1 = 1, // User commands (Release, Delete, Quarantine, OpenInOutlook)
    P2 = 2, // Search Indexer crawl
    P3 = 3  // Duplicate Finder discovery crawl
}
