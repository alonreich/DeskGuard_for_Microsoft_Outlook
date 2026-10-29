namespace DeskGuard.Core.Models;

public sealed class EmailRecord
{
    public string Id { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string Fingerprint { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Cc { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Headers { get; set; } = string.Empty;
    public string Verdict { get; set; } = "Safe";
    public double Score { get; set; } = 100.0;
    public string Tier { get; set; } = string.Empty;
    public string AttachmentsJson { get; set; } = "[]";
    public string Attachments { get; set; } = string.Empty;
    public bool Unread { get; set; }
    public bool UserMoved { get; set; }
    public string CurrentFolder { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public bool Hidden { get; set; }
    public string FolderKind { get; set; } = string.Empty;
    public long Size { get; set; }
    public string PenaltiesJson { get; set; } = "[]";
    public string UserMovedStory { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public bool AvScanned { get; set; }
    public string AvVerdict { get; set; } = string.Empty;
    public string AvThreatName { get; set; } = string.Empty;
}
