using System;
using System.Collections.Generic;
using System.Linq;
using EventViewerX.Reports;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogEvtxSecurityReportHelper {
    private const int MaxViewTop = 5000;
    private const int MaxAdCandidateUsers = 12;

    private static readonly HashSet<string> NonAdUsers = new(StringComparer.OrdinalIgnoreCase) {
        "anonymous logon",
        "system",
        "local service",
        "network service",
        "-",
        "?"
    };

    private static readonly HashSet<string> NonAdDomains = new(StringComparer.OrdinalIgnoreCase) {
        "nt authority",
        "window manager",
        "font driver host",
        "local",
        "-",
        "?"
    };

    public static string BuildTopUserResponse<TModel>(
        JsonObject? arguments,
        TModel model,
        string title,
        IReadOnlyList<ReportTopRow>? byTargetUser,
        IReadOnlyList<ReportTopRow>? byTargetDomain,
        int matchedEvents,
        int scannedEvents,
        int maxEventsScanned,
        bool truncated) {
        var adCorrelation = BuildAdCorrelationHints(byTargetUser, byTargetDomain);
        var output = model is JsonObject obj
            ? obj
            : ToolJson.ToJsonObjectSnakeCase(model);
        output.Add("ad_correlation", ToolJson.ToJsonObjectSnakeCase(adCorrelation));

        ToolTableViewEnvelope.TryBuildModelResponseAutoColumns(
            arguments: arguments,
            model: output,
            sourceRows: byTargetUser ?? Array.Empty<ReportTopRow>(),
            viewRowsPath: "by_target_user_view",
            title: title,
            maxTop: MaxViewTop,
            baseTruncated: truncated,
            response: out var response,
            scanned: scannedEvents,
            metaMutate: meta => meta
                .Add("matched_events", matchedEvents)
                .Add("max_events_scanned", maxEventsScanned));
        return response;
    }

    private static object BuildAdCorrelationHints(
        IReadOnlyList<ReportTopRow>? byTargetUser,
        IReadOnlyList<ReportTopRow>? byTargetDomain) {
        var users = ExtractTopValues(byTargetUser, "user")
            .Select(NormalizeUser)
            .Where(static user => !string.IsNullOrWhiteSpace(user))
            .Where(IsLikelyAdUser)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxAdCandidateUsers)
            .ToArray();

        var domains = ExtractTopValues(byTargetDomain, "domain")
            .Select(static domain => domain.Trim())
            .Where(static domain => !string.IsNullOrWhiteSpace(domain))
            .Where(IsLikelyAdDomain)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        var searchCandidates = users
            .Select(static user => new {
                tool = "ad_search",
                arguments = new {
                    query = user,
                    kind = "user"
                }
            })
            .ToArray();

        var hasCandidates = users.Length > 0;
        return new {
            has_candidates = hasCandidates,
            target_users_top = users,
            target_domains_top = domains,
            suggested_tools = hasCandidates
                ? new[] { "ad_environment_discover", "ad_search" }
                : Array.Empty<string>(),
            suggested_queries = searchCandidates,
            notes = hasCandidates
                ? new[] {
                    "Call ad_environment_discover first to resolve domain_controller/search_base_dn for this host.",
                    "Then run ad_search for candidate target users to correlate lockout/logon evidence with AD object state."
                }
                : new[] {
                    "No high-confidence AD user candidates were detected in top rows."
                }
        };
    }

    private static IEnumerable<string> ExtractTopValues(IReadOnlyList<ReportTopRow>? rows, string preferredKey) {
        if (rows is null || rows.Count == 0) {
            return Array.Empty<string>();
        }

        var values = new List<string>(rows.Count);
        for (var i = 0; i < rows.Count; i++) {
            var row = rows[i];
            if (row?.Key is null || row.Key.Count == 0) {
                continue;
            }

            if (!TryReadKeyValue(row.Key, preferredKey, out var value) || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            values.Add(value.Trim());
        }

        return values;
    }

    private static bool TryReadKeyValue(IReadOnlyDictionary<string, object?> key, string preferredKey, out string value) {
        value = string.Empty;
        if (key is null || key.Count == 0) {
            return false;
        }

        foreach (var pair in key) {
            if (!string.Equals(pair.Key, preferredKey, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            value = pair.Value?.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }

        foreach (var pair in key) {
            if (pair.Value is null) {
                continue;
            }

            var text = pair.Value.ToString();
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            value = text.Trim();
            return true;
        }

        return false;
    }

    private static string NormalizeUser(string user) {
        if (string.IsNullOrWhiteSpace(user)) {
            return string.Empty;
        }

        var value = user.Trim();
        var slashIndex = value.IndexOf('\\');
        if (slashIndex > -1 && slashIndex < value.Length - 1) {
            value = value.Substring(slashIndex + 1);
        }

        return value.Trim();
    }

    private static bool IsLikelyAdUser(string user) {
        if (string.IsNullOrWhiteSpace(user)) {
            return false;
        }

        var normalized = user.Trim();
        if (NonAdUsers.Contains(normalized)) {
            return false;
        }

        if (normalized.EndsWith("$", StringComparison.Ordinal)) {
            return false;
        }

        if (normalized.StartsWith("dwm-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("umfd-", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    private static bool IsLikelyAdDomain(string domain) {
        if (string.IsNullOrWhiteSpace(domain)) {
            return false;
        }

        var normalized = domain.Trim();
        return !NonAdDomains.Contains(normalized);
    }
}
