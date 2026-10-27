namespace DeskGuard.Core.Models;

public sealed class SearchParameters
{
    public string Query { get; set; } = string.Empty;
    public string Scope { get; set; } = "all";
    public string Category { get; set; } = "all";
    public string FromFilter { get; set; } = string.Empty;
    public string ToFilter { get; set; } = string.Empty;
    public string DateFrom { get; set; } = string.Empty;
    public string DateTo { get; set; } = string.Empty;
    public bool? HasAttachment { get; set; }
    public string Folder { get; set; } = string.Empty;
    public string Verdict { get; set; } = string.Empty;
    public int Limit { get; set; } = 100;
    public int Offset { get; set; }
    public string SortBy { get; set; } = "date";
    public string SortOrder { get; set; } = "desc";
}

public sealed class SearchResult
{
    public int Total { get; set; }
    public List<EmailRecord> Rows { get; set; } = [];
}

public sealed class AttachmentInfo
{
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string Md5 { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
