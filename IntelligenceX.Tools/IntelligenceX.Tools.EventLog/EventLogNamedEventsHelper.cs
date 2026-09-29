using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EventViewerX;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogNamedEventsHelper {
    private static readonly Lazy<IReadOnlyList<EventLogNamedEventCatalogRow>> CatalogRows = new(BuildCatalogRows);
    private static readonly IReadOnlyDictionary<string, EventType> ParseMap = BuildParseMap();
    private static readonly IReadOnlyList<string> KnownCategories = BuildKnownCategories();

    internal static IReadOnlyList<EventLogNamedEventCatalogRow> GetCatalogRows() {
        return CatalogRows.Value;
    }

    internal static bool TryParseMany(
        IReadOnlyList<string> values,
        int maxItems,
        out List<EventType> parsed,
        out string? error) {
        parsed = new List<EventType>();
        error = null;

        if (values is null || values.Count == 0) {
            error = "named_events must contain at least one value.";
            return false;
        }

        if (maxItems > 0 && values.Count > maxItems) {
            error = $"named_events supports at most {maxItems} values per call.";
            return false;
        }

        var seen = new HashSet<EventType>();
        for (var i = 0; i < values.Count; i++) {
            var value = values[i];
            if (!TryParseOne(value, out var parsedValue)) {
                error = $"named_events[{i}] ('{value}') is not recognized. Call eventlog_named_events_catalog to list valid names.";
                return false;
            }

            if (seen.Add(parsedValue)) {
                parsed.Add(parsedValue);
            }
        }

        if (parsed.Count == 0) {
            error = "named_events must contain at least one valid value.";
            return false;
        }

        return true;
    }

    internal static bool TryParseOne(string? value, out EventType parsed) {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = NormalizeKey(value);
        return ParseMap.TryGetValue(normalized, out parsed);
    }

    internal static string GetEnumName(EventType value) {
        return value.ToString();
    }

    internal static string GetQueryName(EventType value) {
        return ToSnakeCase(value.ToString());
    }

    internal static string GetCategory(EventType value) {
        return ResolveCategory(value.ToString());
    }

    internal static IReadOnlyList<string> GetKnownCategories() {
        return KnownCategories;
    }

    internal static bool TryNormalizeCategory(string? value, out string normalized) {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var candidate = ToSnakeCase(value.Trim());
        if (string.IsNullOrWhiteSpace(candidate)) {
            return false;
        }

        for (var i = 0; i < KnownCategories.Count; i++) {
            var known = KnownCategories[i];
            if (string.Equals(known, candidate, StringComparison.OrdinalIgnoreCase)) {
                normalized = known;
                return true;
            }
        }

        return false;
    }

    internal static bool TryParseCategories(
        IReadOnlyList<string> values,
        int maxItems,
        out List<string> categories,
        out string? error) {
        categories = new List<string>();
        error = null;

        if (values is null || values.Count == 0) {
            error = "categories must contain at least one value.";
            return false;
        }

        if (maxItems > 0 && values.Count > maxItems) {
            error = $"categories supports at most {maxItems} values per call.";
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var value = values[i];
            if (!TryNormalizeCategory(value, out var normalized)) {
                error = $"categories[{i}] ('{value}') is not recognized. Call eventlog_named_events_catalog to list valid categories.";
                return false;
            }

            if (seen.Add(normalized)) {
                categories.Add(normalized);
            }
        }

        if (categories.Count == 0) {
            error = "categories must contain at least one valid value.";
            return false;
        }

        return true;
    }

    private static IReadOnlyList<EventLogNamedEventCatalogRow> BuildCatalogRows() {
        var rows = new List<EventLogNamedEventCatalogRow>();
        foreach (var value in Enum.GetValues<EventType>().OrderBy(static x => x.ToString(), StringComparer.OrdinalIgnoreCase)) {
            var enumName = value.ToString();
            var queryName = ToSnakeCase(enumName);
            var category = GetCategory(value);

            var sources = EventTypeCatalog.GetDefinition(value).Sources;
            var logNames = sources.Select(static source => source.LogName)
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var eventIds = sources
                .SelectMany(static source => source.EventIds)
                .Distinct()
                .OrderBy(static x => x)
                .ToArray();

            rows.Add(new EventLogNamedEventCatalogRow(
                EnumName: enumName,
                QueryName: queryName,
                Category: category,
                LogNames: logNames,
                EventIds: eventIds,
                EventIdCount: eventIds.Length,
                Available: logNames.Length > 0 && eventIds.Length > 0));
        }

        return rows;
    }

    private static IReadOnlyDictionary<string, EventType> BuildParseMap() {
        var map = new Dictionary<string, EventType>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in Enum.GetValues<EventType>()) {
            var enumName = value.ToString();
            map[NormalizeKey(enumName)] = value;
            map[NormalizeKey(ToSnakeCase(enumName))] = value;
        }

        // Common alias variants produced by LLMs for Kerberos/authentication named events.
        AddAlias(map, "ad_kerberos_authentication_ticket_requested", EventType.KerberosTGTRequest);
        AddAlias(map, "ad_kerberos_service_ticket_requested", EventType.KerberosServiceTicket);
        AddAlias(map, "ad_kerberos_pre_authentication_failed", EventType.KerberosTicketFailure);
        AddAlias(map, "ad_successful_account_logon", EventType.ADUserLogon);
        AddAlias(map, "ad_failed_logon", EventType.ADUserLogonFailed);

        return map;
    }

    private static void AddAlias(IDictionary<string, EventType> map, string alias, EventType target) {
        var normalized = NormalizeKey(alias);
        if (normalized.Length == 0) {
            return;
        }

        map[normalized] = target;
    }

    private static IReadOnlyList<string> BuildKnownCategories() {
        var categories = Enum.GetValues<EventType>()
            .Select(GetCategory)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return categories;
    }

    private static string ResolveCategory(string enumName) {
        if (enumName.StartsWith("AD", StringComparison.Ordinal)) {
            return "active_directory";
        }
        if (enumName.StartsWith("Gpo", StringComparison.OrdinalIgnoreCase)) {
            return "group_policy";
        }
        if (enumName.StartsWith("Kerberos", StringComparison.OrdinalIgnoreCase)) {
            return "kerberos";
        }
        if (enumName.StartsWith("IIS", StringComparison.OrdinalIgnoreCase)) {
            return "iis";
        }
        if (enumName.StartsWith("HyperV", StringComparison.OrdinalIgnoreCase)) {
            return "hyperv";
        }
        if (enumName.StartsWith("AAD", StringComparison.OrdinalIgnoreCase)) {
            return "azure_ad";
        }
        if (enumName.StartsWith("Sql", StringComparison.OrdinalIgnoreCase)) {
            return "sql";
        }
        if (enumName.StartsWith("Dhcp", StringComparison.OrdinalIgnoreCase)) {
            return "dhcp";
        }
        if (enumName.StartsWith("Network", StringComparison.OrdinalIgnoreCase)) {
            return "network";
        }
        if (enumName.StartsWith("BitLocker", StringComparison.OrdinalIgnoreCase)) {
            return "bitlocker";
        }
        if (enumName.StartsWith("Logs", StringComparison.OrdinalIgnoreCase)) {
            return "logging";
        }
        if (enumName.StartsWith("OS", StringComparison.OrdinalIgnoreCase)) {
            return "os";
        }
        if (enumName.StartsWith("ClientGroupPolicies", StringComparison.OrdinalIgnoreCase)) {
            return "group_policy_client";
        }

        return "other";
    }

    private static string NormalizeKey(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            if (char.IsLetterOrDigit(c)) {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static string ToSnakeCase(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            if (!char.IsLetterOrDigit(c)) {
                if (sb.Length > 0 && sb[^1] != '_') {
                    sb.Append('_');
                }
                continue;
            }

            if (i > 0) {
                var prev = value[i - 1];
                var next = i + 1 < value.Length ? value[i + 1] : '\0';

                var shouldSplitUpper =
                    char.IsUpper(c) &&
                    (char.IsLower(prev) || char.IsDigit(prev) || (char.IsUpper(prev) && next != '\0' && char.IsLower(next)));
                var shouldSplitDigit = char.IsDigit(c) && !char.IsDigit(prev);
                var shouldSplitLetter = char.IsLetter(c) && char.IsDigit(prev);

                if ((shouldSplitUpper || shouldSplitDigit || shouldSplitLetter) && sb.Length > 0 && sb[^1] != '_') {
                    sb.Append('_');
                }
            }

            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString().Trim('_');
    }
}

internal sealed record EventLogNamedEventCatalogRow(
    string EnumName,
    string QueryName,
    string Category,
    IReadOnlyList<string> LogNames,
    IReadOnlyList<int> EventIds,
    int EventIdCount,
    bool Available);
