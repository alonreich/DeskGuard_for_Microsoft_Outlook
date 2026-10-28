namespace DeskGuard.Data;

using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using DeskGuard.Core.Models;
using Microsoft.Data.Sqlite;

public sealed partial class VaultRepository
{
    private static readonly Dictionary<string, string[]> SearchFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["all"] = ["e.subject", "e.body", "e.sender", "e.recipient", "e.attachments"],
        ["subject"] = ["e.subject"],
        ["body"] = ["e.body"],
        ["attachments"] = ["e.attachments"]
    };

    [GeneratedRegex(@"^[\p{L}\p{N}_*?]+$", RegexOptions.Compiled)]
    private static partial Regex SimpleWordWildcardRegex();

    [GeneratedRegex(@"(?i)\bfrom:(?:""(?<val>[^""]+)""|(?<val>[^\s]+))", RegexOptions.Compiled)]
    private static partial Regex FromOperatorRegex();

    [GeneratedRegex(@"(?i)\bto:(?:""(?<val>[^""]+)""|(?<val>[^\s]+))", RegexOptions.Compiled)]
    private static partial Regex ToOperatorRegex();

    [GeneratedRegex(@"(?i)\bsubject:(?:""(?<val>[^""]+)""|(?<val>[^\s]+))", RegexOptions.Compiled)]
    private static partial Regex SubjectOperatorRegex();

    [GeneratedRegex(@"(?i)\bfolder:(?:""(?<val>[^""]+)""|(?<val>[^\s]+))", RegexOptions.Compiled)]
    private static partial Regex FolderOperatorRegex();

    [GeneratedRegex(@"(?i)\bhas:(?:noattach|noattachment)\b", RegexOptions.Compiled)]
    private static partial Regex HasNoAttachRegex();

    [GeneratedRegex(@"(?i)\bhas:(?:attach|attachment)\b", RegexOptions.Compiled)]
    private static partial Regex HasAttachRegex();

    [GeneratedRegex(@"(?i)\bscore:(?<op><=|>=|<|>|=)?(?<val>\d+)\b", RegexOptions.Compiled)]
    private static partial Regex ScoreOperatorRegex();

    [GeneratedRegex(@"(?i)\bis:(?<val>unread|read|malicious|spam|suspicious|safe|clean)\b", RegexOptions.Compiled)]
    private static partial Regex IsOperatorRegex();

    [GeneratedRegex(@"(?i)\bverdict:(?<val>[a-zA-Z]+)\b", RegexOptions.Compiled)]
    private static partial Regex VerdictOperatorRegex();

    [GeneratedRegex(@"(?i)\b(?:after|since|date:>|date:>=):(?<val>\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex DateAfterRegex();

    [GeneratedRegex(@"(?i)\b(?:before|until|date:<|date:<=):(?<val>\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled)]
    private static partial Regex DateBeforeRegex();

    [GeneratedRegex(@"""(?<phrase>[^""]+)""|(?<word>[^\s""]+)", RegexOptions.Compiled)]
    private static partial Regex TokenizeTermsRegex();

    public static (string CleanedQuery, List<string> Conditions, List<object> Bindings) ParseSearchOperators(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return (string.Empty, [], []);
        }

        List<string> conds = [];
        List<object> binds = [];
        string current = query;

        current = FromOperatorRegex().Replace(current, m =>
        {
            string val = m.Groups["val"].Value;
            conds.Add("e.sender LIKE ? ESCAPE '\\'");
            binds.Add($"%{LikeEscape(val)}%");
            return " ";
        });

        current = ToOperatorRegex().Replace(current, m =>
        {
            string val = m.Groups["val"].Value;
            conds.Add("e.recipient LIKE ? ESCAPE '\\'");
            binds.Add($"%{LikeEscape(val)}%");
            return " ";
        });

        current = SubjectOperatorRegex().Replace(current, m =>
        {
            string val = m.Groups["val"].Value;
            conds.Add("e.subject LIKE ? ESCAPE '\\'");
            binds.Add($"%{LikeEscape(val)}%");
            return " ";
        });

        current = FolderOperatorRegex().Replace(current, m =>
        {
            string val = m.Groups["val"].Value;
            conds.Add("e.current_folder LIKE ? ESCAPE '\\'");
            binds.Add($"%{LikeEscape(val)}%");
            return " ";
        });

        current = HasNoAttachRegex().Replace(current, _ =>
        {
            conds.Add("(e.attachments_json IS NULL OR e.attachments_json = '' OR e.attachments_json = '[]')");
            return " ";
        });

        current = HasAttachRegex().Replace(current, _ =>
        {
            conds.Add("(e.attachments_json IS NOT NULL AND e.attachments_json != '' AND e.attachments_json != '[]')");
            return " ";
        });

        current = ScoreOperatorRegex().Replace(current, m =>
        {
            string op = m.Groups["op"].Success && !string.IsNullOrEmpty(m.Groups["op"].Value) ? m.Groups["op"].Value : "=";
            if (double.TryParse(m.Groups["val"].Value, out double scoreVal))
            {
                conds.Add($"e.score {op} ?");
                binds.Add(scoreVal);
            }
            return " ";
        });

        current = IsOperatorRegex().Replace(current, m =>
        {
            string val = m.Groups["val"].Value.ToLowerInvariant();
            switch (val)
            {
                case "unread":
                    conds.Add("e.unread = 1");
                    break;
                case "read":
                    conds.Add("e.unread = 0");
                    break;
                case "malicious":
                    conds.Add("LOWER(e.verdict) LIKE '%malicious%'");
                    break;
                case "spam":
                    conds.Add("LOWER(e.verdict) LIKE '%spam%'");
                    break;
                case "suspicious":
                    conds.Add("LOWER(e.verdict) LIKE '%suspicious%'");
                    break;
                case "clean":
                case "safe":
                    conds.Add("(LOWER(e.verdict) LIKE '%clean%' OR LOWER(e.verdict) LIKE '%safe%')");
                    break;
            }
            return " ";
        });

        current = VerdictOperatorRegex().Replace(current, m =>
        {
            string val = m.Groups["val"].Value.ToLowerInvariant();
            conds.Add("LOWER(e.verdict) = ?");
            binds.Add(val);
            return " ";
        });

        current = DateAfterRegex().Replace(current, m =>
        {
            conds.Add("e.date >= ?");
            binds.Add(m.Groups["val"].Value);
            return " ";
        });

        current = DateBeforeRegex().Replace(current, m =>
        {
            conds.Add("e.date <= ?");
            binds.Add(m.Groups["val"].Value);
            return " ";
        });

        return (current.Trim(), conds, binds);
    }

    private static string[] TokenizeQueryTerms(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        List<string> terms = [];
        foreach (Match m in TokenizeTermsRegex().Matches(query))
        {
            if (m.Groups["phrase"].Success && !string.IsNullOrWhiteSpace(m.Groups["phrase"].Value))
            {
                terms.Add(m.Groups["phrase"].Value.Trim());
            }
            else if (m.Groups["word"].Success && !string.IsNullOrWhiteSpace(m.Groups["word"].Value))
            {
                terms.Add(m.Groups["word"].Value.Trim());
            }
        }
        return terms.ToArray();
    }

    private sealed class FtsQueryResult
    {
        public List<string> Parts { get; } = [];
        public bool NeedsLike { get; set; }
        public bool NoMatch { get; set; }
    }

    private FtsQueryResult BuildFtsQuery(string[] terms)
    {
        FtsQueryResult outResult = new();
        foreach (string t in terms)
        {
            string core = t.TrimEnd('*');
            bool infix = core.Contains('*') || core.Contains('?');

            if (string.IsNullOrEmpty(core)) continue;

            if (core.Contains(' '))
            {
                outResult.Parts.Add($"\"{core.Replace("\"", "\"\"")}\"");
                continue;
            }

            if (!infix)
            {
                outResult.Parts.Add($"\"{core.Replace("\"", "\"\"")}\"*");
                continue;
            }

            if (!SimpleWordWildcardRegex().IsMatch(t))
            {
                outResult.NeedsLike = true;
                continue;
            }

            List<string> words = [];
            try
            {
                using SqliteCommand cmd = _readConnection!.CreateCommand();
                cmd.CommandText = "SELECT term FROM emails_vocab WHERE term GLOB $glob ORDER BY doc DESC LIMIT 300";
                cmd.Parameters.AddWithValue("$glob", t.ToLowerInvariant());
                using SqliteDataReader r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!r.IsDBNull(0)) words.Add(r.GetString(0));
                }
            }
            catch
            {
                outResult.NeedsLike = true;
                continue;
            }

            if (words.Count == 0)
            {
                outResult.NoMatch = true;
                return outResult;
            }

            StringBuilder partBuilder = new();
            partBuilder.Append('(');
            for (int i = 0; i < words.Count; i++)
            {
                if (i > 0) partBuilder.Append(" OR ");
                partBuilder.Append($"\"{words[i].Replace("\"", "\"\"")}\"");
            }
            partBuilder.Append(')');
            outResult.Parts.Add(partBuilder.ToString());
        }
        return outResult;
    }

    private static string ReplacePlaceholdersWithNamed(string sql, out List<string> paramNames)
    {
        StringBuilder sb = new();
        paramNames = [];
        int count = 0;
        for (int i = 0; i < sql.Length; i++)
        {
            if (sql[i] == '?')
            {
                string pName = $"$p{count++}";
                paramNames.Add(pName);
                sb.Append(pName);
            }
            else
            {
                sb.Append(sql[i]);
            }
        }
        return sb.ToString();
    }

    private static (List<string> Conds, List<string> Binds) BuildLikeConditions(string[] terms, string scope)
    {
        string[] fields = SearchFields.GetValueOrDefault(scope, SearchFields["all"]);
        List<string> conds = [];
        List<string> binds = [];

        foreach (string term in terms)
        {
            string pattern = $"%{LikeEscape(term)}%";
            StringBuilder fieldCond = new();
            fieldCond.Append('(');
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) fieldCond.Append(" OR ");
                fieldCond.Append($"{fields[i]} LIKE ? ESCAPE '\\'");
            }
            fieldCond.Append(')');
            conds.Add(fieldCond.ToString());

            for (int i = 0; i < fields.Length; i++)
            {
                binds.Add(pattern);
            }
        }

        return (conds, binds);
    }

    public SearchResult SearchEmails(SearchParameters parameters)
    {
        EnsureReady();
        if (parameters == null) return new SearchResult();

        lock (_readSyncRoot)
        {
            try
            {
                List<string> conditions = ["e.hidden = 0"];
                List<object> bindings = [];

                if (!string.IsNullOrWhiteSpace(parameters.Category) && !parameters.Category.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("LOWER(e.verdict) LIKE ?");
                    bindings.Add($"%{parameters.Category.ToLowerInvariant()}%");
                }

                if (!string.IsNullOrWhiteSpace(parameters.Verdict) && !parameters.Verdict.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("LOWER(e.verdict) = ?");
                    bindings.Add(parameters.Verdict.ToLowerInvariant());
                }

                if (!string.IsNullOrWhiteSpace(parameters.DateFrom))
                {
                    conditions.Add("e.date >= ?");
                    bindings.Add(parameters.DateFrom);
                }

                if (!string.IsNullOrWhiteSpace(parameters.DateTo))
                {
                    conditions.Add("e.date <= ?");
                    bindings.Add(parameters.DateTo);
                }

                if (!string.IsNullOrWhiteSpace(parameters.Folder) && !parameters.Folder.Equals("all", StringComparison.OrdinalIgnoreCase))
                {
                    conditions.Add("e.current_folder LIKE ? ESCAPE '\\'");
                    bindings.Add($"%{LikeEscape(parameters.Folder)}%");
                }

                if (parameters.HasAttachment == true)
                {
                    conditions.Add("(e.attachments_json IS NOT NULL AND e.attachments_json != '' AND e.attachments_json != '[]')");
                }

                if (!string.IsNullOrWhiteSpace(parameters.FromFilter))
                {
                    conditions.Add("e.sender LIKE ? ESCAPE '\\'");
                    bindings.Add($"%{LikeEscape(parameters.FromFilter.Trim())}%");
                }

                if (!string.IsNullOrWhiteSpace(parameters.ToFilter))
                {
                    conditions.Add("e.recipient LIKE ? ESCAPE '\\'");
                    bindings.Add($"%{LikeEscape(parameters.ToFilter.Trim())}%");
                }

                var (cleanQuery, opConds, opBinds) = ParseSearchOperators(parameters.Query);
                conditions.AddRange(opConds);
                bindings.AddRange(opBinds);

                string[] terms = TokenizeQueryTerms(cleanQuery);
                FtsQueryResult fts = BuildFtsQuery(terms);
                if (fts.NoMatch)
                {
                    return new SearchResult { Total = 0, Rows = [] };
                }

                string orderDir = parameters.SortOrder.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
                string orderClause = parameters.SortBy.ToLowerInvariant() switch
                {
                    "date" => $"e.date {orderDir}, e.time {orderDir}",
                    "time" => $"e.time {orderDir}",
                    "subject" => $"e.subject {orderDir}",
                    "sender" => $"e.sender {orderDir}",
                    "recipient" => $"e.recipient {orderDir}",
                    "verdict" => $"e.verdict {orderDir}",
                    "score" => $"e.score {orderDir}",
                    _ => $"e.date {orderDir}, e.time {orderDir}"
                };

                // Attempt FTS if parts present and not requiring full LIKE scan
                if (fts.Parts.Count > 0 && !fts.NeedsLike)
                {
                    string phrase = string.Join(" AND ", fts.Parts);
                    string scope = parameters.Scope.ToLowerInvariant();
                    string ftsQuery = (scope is "subject" or "body" or "attachments") ? $"{{{scope}}} : ({phrase})" : phrase;

                    List<string> ftsWhereParts = ["emails_fts MATCH ?", .. conditions];
                    string rawFtsWhere = string.Join(" AND ", ftsWhereParts);
                    string ftsWhere = ReplacePlaceholdersWithNamed(rawFtsWhere, out List<string> ftsParamNames);
                    List<object> ftsBindings = [ftsQuery, .. bindings];

                    try
                    {
                        using SqliteCommand countCmd = _readConnection!.CreateCommand();
                        countCmd.CommandText = $"SELECT COUNT(*) FROM emails_fts f JOIN emails e ON f.rowid = e.rowid WHERE {ftsWhere}";
                        for (int i = 0; i < ftsBindings.Count; i++)
                        {
                            countCmd.Parameters.AddWithValue(ftsParamNames[i], ftsBindings[i]);
                        }
                        int total = Convert.ToInt32(countCmd.ExecuteScalar());

                        using SqliteCommand selectCmd = _readConnection.CreateCommand();
                        selectCmd.CommandText = $"""
                            SELECT e.id, e.entry_id, e.store_id, e.fingerprint, e.subject, e.sender, e.recipient, e.cc,
                                   e.date, e.time, e.timestamp, e.body, e.headers, e.verdict, e.score, e.tier,
                                   e.attachments_json, e.attachments, e.unread, e.user_moved, e.current_folder,
                                   e.message_id, e.hidden, e.folder_kind, e.size, e.penalties_json,
                                   snippet(emails_fts, 1, '<mark>', '</mark>', '...', 24) AS snippet
                            FROM emails_fts f JOIN emails e ON f.rowid = e.rowid
                            WHERE {ftsWhere}
                            ORDER BY {orderClause}
                            LIMIT $limit OFFSET $offset;
                        """;
                        for (int i = 0; i < ftsBindings.Count; i++)
                        {
                            selectCmd.Parameters.AddWithValue(ftsParamNames[i], ftsBindings[i]);
                        }
                        selectCmd.Parameters.AddWithValue("$limit", parameters.Limit);
                        selectCmd.Parameters.AddWithValue("$offset", parameters.Offset);

                        List<EmailRecord> rows = [];
                        using (SqliteDataReader r = selectCmd.ExecuteReader())
                        {
                            while (r.Read())
                            {
                                EmailRecord rec = MapEmailRecord(r, light: true);
                                rec.Snippet = r.IsDBNull(26) ? string.Empty : r.GetString(26);
                                rows.Add(rec);
                            }
                        }

                        return new SearchResult { Total = total, Rows = rows };
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VaultRepository] FTS match failed, falling back to LIKE: {ex.Message}");
                    }
                }

                // Fallback to LIKE condition
                var (likeConds, likeBinds) = BuildLikeConditions(terms, parameters.Scope);
                List<string> allConds = [.. conditions, .. likeConds];
                List<object> allBinds = [.. bindings, .. likeBinds];
                string rawWhere = $"WHERE {string.Join(" AND ", allConds)}";
                string whereClause = ReplacePlaceholdersWithNamed(rawWhere, out List<string> likeParamNames);

                using (SqliteCommand countCmd = _readConnection!.CreateCommand())
                {
                    countCmd.CommandText = $"SELECT COUNT(*) FROM emails e {whereClause}";
                    for (int i = 0; i < allBinds.Count; i++)
                    {
                        countCmd.Parameters.AddWithValue(likeParamNames[i], allBinds[i]);
                    }
                    int total = Convert.ToInt32(countCmd.ExecuteScalar());

                    using SqliteCommand selectCmd = _readConnection.CreateCommand();
                    selectCmd.CommandText = $"""
                        SELECT id, entry_id, store_id, fingerprint, subject, sender, recipient, cc,
                               date, time, timestamp, body, headers, verdict, score, tier,
                               attachments_json, attachments, unread, user_moved, current_folder,
                               message_id, hidden, folder_kind, size, penalties_json
                        FROM emails e
                        {whereClause}
                        ORDER BY {orderClause}
                        LIMIT $limit OFFSET $offset;
                    """;
                    for (int i = 0; i < allBinds.Count; i++)
                    {
                        selectCmd.Parameters.AddWithValue(likeParamNames[i], allBinds[i]);
                    }
                    selectCmd.Parameters.AddWithValue("$limit", parameters.Limit);
                    selectCmd.Parameters.AddWithValue("$offset", parameters.Offset);

                    List<EmailRecord> rows = [];
                    using (SqliteDataReader r = selectCmd.ExecuteReader())
                    {
                        while (r.Read())
                        {
                            rows.Add(MapEmailRecord(r, light: true));
                        }
                    }

                    return new SearchResult { Total = total, Rows = rows };
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VaultRepository] SearchEmails error: {ex.Message}");
                return new SearchResult { Total = 0, Rows = [] };
            }
        }
    }
}
