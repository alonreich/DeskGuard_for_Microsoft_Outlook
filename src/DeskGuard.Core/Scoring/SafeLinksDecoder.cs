namespace DeskGuard.Core.Scoring;

using System;
using System.Net;

public static class SafeLinksDecoder
{
    public static string UnwrapUrl(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return string.Empty;

        string current = rawUrl.Trim();
        int maxDepth = 5;

        while (maxDepth-- > 0)
        {
            if (!Uri.TryCreate(current, UriKind.Absolute, out Uri? uri))
            {
                break;
            }

            string host = uri.Host.ToLowerInvariant();
            if (host.EndsWith("safelinks.protection.outlook.com", StringComparison.OrdinalIgnoreCase) ||
                (host.Contains("google.") && uri.AbsolutePath.Equals("/url", StringComparison.OrdinalIgnoreCase)))
            {
                string query = uri.Query.TrimStart('?');
                string? unwrapped = null;

                foreach (string param in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eqIndex = param.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string key = param[..eqIndex].ToLowerInvariant();
                        if (key is "url" or "q")
                        {
                            string val = param[(eqIndex + 1)..];
                            try
                            {
                                unwrapped = Uri.UnescapeDataString(val);
                                break;
                            }
                            catch
                            {
                                unwrapped = WebUtility.UrlDecode(val);
                                break;
                            }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(unwrapped) && !string.Equals(unwrapped, current, StringComparison.OrdinalIgnoreCase))
                {
                    current = unwrapped;
                    continue;
                }
            }

            break;
        }

        return current;
    }

    public static string GetLinkHost(string href)
    {
        string unwrapped = UnwrapUrl(href);
        if (Uri.TryCreate(unwrapped, UriKind.Absolute, out Uri? uri))
        {
            return uri.Host.ToLowerInvariant();
        }
        return string.Empty;
    }
}
