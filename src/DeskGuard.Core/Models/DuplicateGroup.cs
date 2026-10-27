namespace DeskGuard.Core.Models;

public sealed class DuplicateGroup
{
    public string GroupKey { get; set; } = string.Empty;
    public string Dna { get; set; } = string.Empty;
    public DuplicateItem Survivor { get; set; } = new();
    public List<DuplicateItem> Duplicates { get; set; } = [];
}
