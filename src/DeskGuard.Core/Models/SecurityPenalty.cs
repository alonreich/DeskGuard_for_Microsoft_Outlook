namespace DeskGuard.Core.Models;

public sealed class SecurityPenalty
{
    public string Rule { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public double Points { get; set; }
    public string Evidence { get; set; } = string.Empty;
}
