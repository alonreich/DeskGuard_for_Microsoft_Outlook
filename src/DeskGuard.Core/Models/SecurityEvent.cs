namespace DeskGuard.Core.Models;

public sealed class SecurityEvent
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string MessageKey { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string FromFolder { get; set; } = string.Empty;
    public string ToFolder { get; set; } = string.Empty;
    public double? Score { get; set; }
    public string ReasonsJson { get; set; } = "[]";
}
