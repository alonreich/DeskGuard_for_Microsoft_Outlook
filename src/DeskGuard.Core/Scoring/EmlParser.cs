namespace DeskGuard.Core.Scoring;

using System;
using System.Collections.Generic;

public sealed class ParsedEml
{
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string RawHeaders { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

public static class EmlParser
{
    public static ParsedEml Parse(string? rawEml)
    {
        if (string.IsNullOrWhiteSpace(rawEml))
        {
            return new ParsedEml();
        }

        int splitIdx = rawEml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int headerEndLen = 4;
        if (splitIdx == -1)
        {
            splitIdx = rawEml.IndexOf("\n\n", StringComparison.Ordinal);
            headerEndLen = 2;
        }

        string rawHeaders = splitIdx != -1 ? rawEml[..splitIdx] : rawEml;
        string body = splitIdx != -1 ? rawEml[(splitIdx + headerEndLen)..].Trim() : string.Empty;

        Dictionary<string, string> headers = new(StringComparer.OrdinalIgnoreCase);
        string[] lines = rawHeaders.Split(["\r\n", "\n"], StringSplitOptions.None);
        string? currentKey = null;

        foreach (string line in lines)
        {
            if (line.Length > 0 && char.IsWhiteSpace(line[0]) && currentKey != null)
            {
                headers[currentKey] += " " + line.Trim();
            }
            else
            {
                int colonIdx = line.IndexOf(':');
                if (colonIdx > 0)
                {
                    currentKey = line[..colonIdx].Trim();
                    string val = line[(colonIdx + 1)..].Trim();
                    if (headers.TryGetValue(currentKey, out string? existing))
                    {
                        headers[currentKey] = existing + "\n" + val;
                    }
                    else
                    {
                        headers[currentKey] = val;
                    }
                }
            }
        }

        return new ParsedEml
        {
            Headers = headers,
            RawHeaders = rawHeaders,
            Body = body
        };
    }
}
