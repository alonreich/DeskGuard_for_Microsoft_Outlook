namespace DeskGuard.Core.Scoring;

using System;
using System.Text;

public static class RegisteredDomainParser
{
    private static readonly HashSet<string> SecondLevelTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "co", "com", "org", "net", "ac", "gov", "edu", "ne", "or"
    };

    public static string GetRegisteredDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return string.Empty;

        ReadOnlySpan<char> span = domain.AsSpan().Trim().Trim('.');
        if (span.IsEmpty) return string.Empty;

        // Split by '.'
        Span<Range> ranges = stackalloc Range[32];
        int count = span.Split(ranges, '.', StringSplitOptions.RemoveEmptyEntries);
        if (count == 0) return string.Empty;
        if (count == 1) return span[ranges[0]].ToString().ToLowerInvariant();

        ReadOnlySpan<char> last = span[ranges[count - 1]];
        if (count >= 3 && last.Length == 2)
        {
            string secondTld = span[ranges[count - 2]].ToString();
            if (SecondLevelTlds.Contains(secondTld))
            {
                int start = ranges[count - 3].Start.Value;
                int end = ranges[count - 1].End.Value;
                return span[start..end].ToString().ToLowerInvariant();
            }
        }

        if (count >= 2)
        {
            int start = ranges[count - 2].Start.Value;
            int end = ranges[count - 1].End.Value;
            return span[start..end].ToString().ToLowerInvariant();
        }

        return span[ranges[0]].ToString().ToLowerInvariant();
    }

    public static string FoldHomoglyphs(string? str)
    {
        if (string.IsNullOrEmpty(str)) return string.Empty;

        string lower = str.ToLowerInvariant();
        StringBuilder sb = new(lower.Length);

        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            if (c == '0')
            {
                sb.Append('o');
            }
            else if (c == '1' || c == '|' || c == 'l')
            {
                sb.Append('l');
            }
            else if (c == 'r' && i + 1 < lower.Length && lower[i + 1] == 'n')
            {
                sb.Append('m');
                i++;
            }
            else if (c == 'v' && i + 1 < lower.Length && lower[i + 1] == 'v')
            {
                sb.Append('w');
                i++;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
