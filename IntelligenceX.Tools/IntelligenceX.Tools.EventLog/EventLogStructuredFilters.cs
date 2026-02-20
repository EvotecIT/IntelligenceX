using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogStructuredFilters {
    internal const int MaxEventIds = 256;
    internal const int MaxRecordIds = 256;
    internal const int MaxProviderNameLength = 260;
    internal const int MaxUserIdLength = 260;
    internal const int MaxNamedDataKeys = 32;
    internal const int MaxNamedDataValuesPerKey = 16;
    internal const int MaxNamedDataKeyLength = 128;
    internal const int MaxNamedDataValueLength = 256;

    internal static readonly string[] LevelNames = Enum.GetValues<Level>()
        .Select(static value => ToSnakeCase(value.ToString()))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static readonly string[] KeywordNames = Enum.GetValues<Keywords>()
        .Select(static value => ToSnakeCase(value.ToString()))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static readonly IReadOnlyDictionary<string, Level> LevelsByName = BuildLevelMap();
    private static readonly IReadOnlyDictionary<string, Keywords> KeywordsByName = BuildKeywordMap();

    internal static bool TryReadOptionalBoundedString(
        JsonObject? arguments,
        string argumentName,
        int maxLength,
        out string? value,
        out string? error) {
        value = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind != JsonValueKind.String) {
            error = $"{argumentName} must be a string.";
            return false;
        }

        var text = raw.AsString();
        if (string.IsNullOrWhiteSpace(text)) {
            return true;
        }

        var normalized = text.Trim();
        if (normalized.Length > maxLength) {
            error = $"{argumentName} must be <= {maxLength} characters.";
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsControl(normalized[i])) {
                error = $"{argumentName} must not contain control characters.";
                return false;
            }
        }

        value = normalized;
        return true;
    }

    internal static bool TryParseOptionalEventIds(
        JsonObject? arguments,
        string argumentName,
        int maxItems,
        out List<int>? values,
        out string? error) {
        values = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind != JsonValueKind.Array) {
            error = $"{argumentName} must be an array of positive 32-bit integers.";
            return false;
        }

        var array = raw.AsArray();
        if (array is null || array.Count == 0) {
            return true;
        }

        if (array.Count > maxItems) {
            error = $"{argumentName} supports at most {maxItems} values.";
            return false;
        }

        var list = new List<int>(array.Count);
        var dedup = new HashSet<int>();
        for (var i = 0; i < array.Count; i++) {
            var item = array[i].AsInt64();
            if (!item.HasValue || item.Value <= 0 || item.Value > int.MaxValue) {
                error = $"{argumentName} values must be positive 32-bit integers.";
                return false;
            }

            var value = (int)item.Value;
            if (dedup.Add(value)) {
                list.Add(value);
            }
        }

        values = list.Count == 0 ? null : list;
        return true;
    }

    internal static bool TryParseOptionalRecordIds(
        JsonObject? arguments,
        string argumentName,
        int maxItems,
        out List<long>? values,
        out string? error) {
        values = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind != JsonValueKind.Array) {
            error = $"{argumentName} must be an array of positive integers.";
            return false;
        }

        var array = raw.AsArray();
        if (array is null || array.Count == 0) {
            return true;
        }

        if (array.Count > maxItems) {
            error = $"{argumentName} supports at most {maxItems} values.";
            return false;
        }

        var list = new List<long>(array.Count);
        var dedup = new HashSet<long>();
        for (var i = 0; i < array.Count; i++) {
            var item = array[i].AsInt64();
            if (!item.HasValue || item.Value <= 0) {
                error = $"{argumentName} values must be positive integers.";
                return false;
            }

            if (dedup.Add(item.Value)) {
                list.Add(item.Value);
            }
        }

        values = list.Count == 0 ? null : list;
        return true;
    }

    internal static bool TryParseOptionalLevel(
        JsonObject? arguments,
        string argumentName,
        out Level? level,
        out string? error) {
        level = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind == JsonValueKind.Number) {
            var numeric = raw.AsInt64();
            if (!numeric.HasValue || numeric.Value < int.MinValue || numeric.Value > int.MaxValue) {
                error = $"{argumentName} must be one of: {string.Join(", ", LevelNames)}.";
                return false;
            }

            var value = (Level)(int)numeric.Value;
            if (!Enum.IsDefined(value)) {
                error = $"{argumentName} must be one of: {string.Join(", ", LevelNames)}.";
                return false;
            }

            level = value;
            return true;
        }

        if (raw.Kind != JsonValueKind.String) {
            error = $"{argumentName} must be a string.";
            return false;
        }

        var text = raw.AsString();
        if (string.IsNullOrWhiteSpace(text)) {
            return true;
        }

        var normalized = ToSnakeCase(text.Trim());
        if (normalized.Length == 0 || string.Equals(normalized, "any", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (LevelsByName.TryGetValue(normalized, out var parsed)) {
            level = parsed;
            return true;
        }

        error = $"{argumentName} must be one of: any, {string.Join(", ", LevelNames)}.";
        return false;
    }

    internal static bool TryParseOptionalKeywords(
        JsonObject? arguments,
        string argumentName,
        out Keywords? keywords,
        out string? error) {
        keywords = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind == JsonValueKind.Number) {
            var numeric = raw.AsInt64();
            if (!numeric.HasValue || numeric.Value < 0) {
                error = $"{argumentName} must be one of: {string.Join(", ", KeywordNames)}.";
                return false;
            }

            keywords = (Keywords)numeric.Value;
            return true;
        }

        if (raw.Kind != JsonValueKind.String) {
            error = $"{argumentName} must be a string.";
            return false;
        }

        var text = raw.AsString();
        if (string.IsNullOrWhiteSpace(text)) {
            return true;
        }

        var normalized = ToSnakeCase(text.Trim());
        if (normalized.Length == 0 || string.Equals(normalized, "any", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (KeywordsByName.TryGetValue(normalized, out var parsed)) {
            keywords = parsed;
            return true;
        }

        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericMask) && numericMask >= 0) {
            keywords = (Keywords)numericMask;
            return true;
        }

        error = $"{argumentName} must be one of: any, {string.Join(", ", KeywordNames)}.";
        return false;
    }

    internal static bool TryParseOptionalNamedDataFilter(
        JsonObject? arguments,
        string argumentName,
        out Hashtable? filter,
        out string? error) {
        filter = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind != JsonValueKind.Object) {
            error = $"{argumentName} must be an object.";
            return false;
        }

        var map = raw.AsObject();
        if (map is null || map.Count == 0) {
            error = $"{argumentName} must include at least one key.";
            return false;
        }

        if (map.Count > MaxNamedDataKeys) {
            error = $"{argumentName} supports at most {MaxNamedDataKeys} keys.";
            return false;
        }

        var table = new Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (var (keyRaw, value) in map) {
            var key = (keyRaw ?? string.Empty).Trim();
            if (key.Length == 0) {
                error = $"{argumentName} keys must be non-empty strings.";
                return false;
            }

            if (key.Length > MaxNamedDataKeyLength) {
                error = $"{argumentName} keys must be <= {MaxNamedDataKeyLength} characters.";
                return false;
            }

            if (!TryParseNamedDataValue(value, argumentName, key, out var parsed, out error)) {
                return false;
            }

            table[key] = parsed;
        }

        filter = table.Count == 0 ? null : table;
        return true;
    }

    internal static bool HasAnyStructuredFilter(
        IReadOnlyList<int>? eventIds,
        string? providerName,
        DateTime? startTimeUtc,
        DateTime? endTimeUtc,
        Level? level,
        Keywords? keywords,
        string? userId,
        IReadOnlyList<long>? eventRecordIds,
        Hashtable? namedDataFilter,
        Hashtable? namedDataExcludeFilter) {
        return (eventIds?.Count ?? 0) > 0
               || !string.IsNullOrWhiteSpace(providerName)
               || startTimeUtc.HasValue
               || endTimeUtc.HasValue
               || level.HasValue
               || keywords.HasValue
               || !string.IsNullOrWhiteSpace(userId)
               || (eventRecordIds?.Count ?? 0) > 0
               || (namedDataFilter?.Count ?? 0) > 0
               || (namedDataExcludeFilter?.Count ?? 0) > 0;
    }

    internal static string BuildStructuredXPath(
        IReadOnlyList<int>? eventIds,
        string? providerName,
        Keywords? keywords,
        Level? level,
        DateTime? startTimeUtc,
        DateTime? endTimeUtc,
        string? userId,
        IReadOnlyList<long>? eventRecordIds,
        Hashtable? namedDataFilter,
        Hashtable? namedDataExcludeFilter) {
        var xpath = SearchEvents.BuildWinEventFilter(
            id: eventIds?.Select(static value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
            eventRecordId: eventRecordIds?.Select(static value => value.ToString(CultureInfo.InvariantCulture)).ToArray(),
            startTime: startTimeUtc,
            endTime: endTimeUtc,
            providerName: string.IsNullOrWhiteSpace(providerName) ? null : new[] { providerName.Trim() },
            keywords: keywords.HasValue ? new[] { (long)keywords.Value } : null,
            level: level.HasValue ? new[] { level.Value.ToString() } : null,
            userId: string.IsNullOrWhiteSpace(userId) ? null : new[] { userId.Trim() },
            namedDataFilter: namedDataFilter is null ? null : new[] { namedDataFilter },
            namedDataExcludeFilter: namedDataExcludeFilter is null ? null : new[] { namedDataExcludeFilter },
            xpathOnly: true);

        return string.IsNullOrWhiteSpace(xpath) ? "*" : xpath;
    }

    internal static JsonObject ObjectMapSchema(string description) {
        return new JsonObject()
            .Add("type", "object")
            .Add("description", description);
    }

    private static bool TryParseNamedDataValue(
        JsonValue value,
        string argumentName,
        string key,
        out object parsed,
        out string? error) {
        parsed = string.Empty;
        error = null;

        if (value is null || value.Kind == JsonValueKind.Null) {
            parsed = Array.Empty<string>();
            return true;
        }

        if (value.Kind == JsonValueKind.Array) {
            var array = value.AsArray();
            if (array is null) {
                error = $"{argumentName}.{key} must be a scalar or array of scalar values.";
                return false;
            }

            if (array.Count > MaxNamedDataValuesPerKey) {
                error = $"{argumentName}.{key} supports at most {MaxNamedDataValuesPerKey} values.";
                return false;
            }

            if (array.Count == 0) {
                parsed = Array.Empty<string>();
                return true;
            }

            var values = new List<string>(array.Count);
            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < array.Count; i++) {
                if (!TryParseNamedDataScalar(array[i], argumentName, key, out var scalar, out error)) {
                    return false;
                }

                if (dedup.Add(scalar)) {
                    values.Add(scalar);
                }
            }

            parsed = values.ToArray();
            return true;
        }

        if (!TryParseNamedDataScalar(value, argumentName, key, out var normalized, out error)) {
            return false;
        }

        parsed = normalized;
        return true;
    }

    private static bool TryParseNamedDataScalar(
        JsonValue value,
        string argumentName,
        string key,
        out string normalized,
        out string? error) {
        normalized = string.Empty;
        error = null;

        switch (value.Kind) {
            case JsonValueKind.String: {
                var text = value.AsString() ?? string.Empty;
                normalized = text.Trim();
                break;
            }
            case JsonValueKind.Number: {
                var asInt64 = value.AsInt64();
                if (asInt64.HasValue) {
                    normalized = asInt64.Value.ToString(CultureInfo.InvariantCulture);
                    break;
                }

                var asDouble = value.AsDouble();
                if (!asDouble.HasValue || !double.IsFinite(asDouble.Value)) {
                    error = $"{argumentName}.{key} contains an invalid numeric value.";
                    return false;
                }

                normalized = asDouble.Value.ToString(CultureInfo.InvariantCulture);
                break;
            }
            case JsonValueKind.Boolean:
                normalized = value.AsBoolean().ToString();
                break;
            default:
                error = $"{argumentName}.{key} must be a scalar or array of scalar values.";
                return false;
        }

        if (normalized.Length > MaxNamedDataValueLength) {
            error = $"{argumentName}.{key} values must be <= {MaxNamedDataValueLength} characters.";
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsControl(normalized[i])) {
                error = $"{argumentName}.{key} values must not contain control characters.";
                return false;
            }
        }

        return true;
    }

    private static string ToSnakeCase(string name) {
        return EventLogNamedEventsQueryShared.ToSnakeCase(name);
    }

    private static IReadOnlyDictionary<string, Level> BuildLevelMap() {
        var map = new Dictionary<string, Level>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in Enum.GetValues<Level>()) {
            map[ToSnakeCase(value.ToString())] = value;
        }

        map["info"] = Level.Informational;
        map["information"] = Level.Informational;
        map["warn"] = Level.Warning;
        map["crit"] = Level.Critical;
        return map;
    }

    private static IReadOnlyDictionary<string, Keywords> BuildKeywordMap() {
        var map = new Dictionary<string, Keywords>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in Enum.GetValues<Keywords>()) {
            map[ToSnakeCase(value.ToString())] = value;
        }
        return map;
    }
}
