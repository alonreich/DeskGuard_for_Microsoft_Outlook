namespace DeskGuard.Core.Models;

public sealed class DuplicateItem
{
    public string EntryId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Folder { get; set; } = string.Empty;
    public string Store { get; set; } = string.Empty;
    public string Dna { get; set; } = string.Empty;
    public string SurvivorId { get; set; } = string.Empty;
    public string SurvivorStoreId { get; set; } = string.Empty;
    public string SurvivorFolder { get; set; } = string.Empty;
    public string SurvivorStore { get; set; } = string.Empty;
}
