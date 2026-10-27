namespace DeskGuard.Core.Models;

public sealed class EngineStats
{
    public int Total { get; set; }
    public int Malicious { get; set; }
    public int Suspicious { get; set; }
    public int Spam { get; set; }
    public int Safe { get; set; }
}
