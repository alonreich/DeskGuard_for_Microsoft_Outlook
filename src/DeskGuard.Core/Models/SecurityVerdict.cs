namespace DeskGuard.Core.Models;

public sealed class SecurityVerdict
{
    public double Score { get; set; } = 100.0;
    public string Verdict { get; set; } = "Safe";
    public List<SecurityPenalty> Penalties { get; set; } = [];
    public double TotalDeduction { get; set; }
}
