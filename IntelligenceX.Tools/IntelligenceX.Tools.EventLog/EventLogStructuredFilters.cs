using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EventViewerX;
using IntelligenceX.Json;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogStructuredFilters {
    internal const int MaxEventIds = EventStructuredQueryFilterService.MaxEventIds;
    internal const int MaxRecordIds = EventStructuredQueryFilterService.MaxRecordIds;
    internal const int MaxNamedDataKeys = EventStructuredQueryFilterService.MaxNamedDataKeys;
    internal const int MaxNamedDataValuesPerKey = EventStructuredQueryFilterService.MaxNamedDataValuesPerKey;

    internal static readonly string[] LevelNames = EventStructuredQueryFilterService.LevelNames.ToArray();
    internal static readonly string[] KeywordNames = EventStructuredQueryFilterService.KeywordNames.ToArray();

    internal static JsonObject ObjectMapSchema(string description) {
        return new JsonObject()
            .Add("type", "object")
            .Add("description", description);
    }

    internal static bool TryNormalize(
        JsonObject? arguments,
        DateTime? startUtc,
        DateTime? endUtc,
        out EventStructuredQueryFilter? filter,
        out string? error) {
        filter = null;
        error = null;

        if (!TryReadOptionalPositiveInt32Array(arguments, "event_ids", out var eventIds, out error)) {
            return false;
        }

        if (!TryReadOptionalString(arguments, "provider_name", allowNumber: false, out var providerName, out error)) {
            return false;
        }

        if (!TryReadOptionalString(arguments, "level", allowNumber: true, out var level, out error)) {
            return false;
        }

        if (!TryReadOptionalString(arguments, "keywords", allowNumber: true, out var keywords, out error)) {
            return false;
        }

        if (!TryReadOptionalString(arguments, "user_id", allowNumber: false, out var userId, out error)) {
            return false;
        }

        if (!TryReadOptionalPositiveInt64Array(arguments, "event_record_ids", out var eventRecordIds, out error)) {
            return false;
        }

        if (!TryReadOptionalNamedDataMap(arguments, "named_data_filter", out var namedDataFilter, out error)) {
            return false;
        }

        if (!TryReadOptionalNamedDataMap(arguments, "named_data_exclude_filter", out var namedDataExcludeFilter, out error)) {
            return false;
        }

        return EventStructuredQueryFilterService.TryNormalize(
            new EventStructuredQueryFilterInput {
                EventIds = eventIds,
                ProviderName = providerName,
                StartTimeUtc = startUtc,
                EndTimeUtc = endUtc,
                Level = level,
                Keywords = keywords,
                UserId = userId,
                RecordIds = eventRecordIds,
                NamedDataFilter = namedDataFilter,
                NamedDataExcludeFilter = namedDataExcludeFilter
            },
            out filter,
            out error);
    }

    internal static bool HasAnyStructuredFilter(EventStructuredQueryFilter? filter) {
        return EventStructuredQueryFilterService.HasAny(filter);
    }

    internal static string BuildStructuredXPath(EventStructuredQueryFilter? filter) {
        return EventStructuredQueryFilterService.BuildXPath(filter);
    }

    private static bool TryReadOptionalString(
        JsonObject? arguments,
        string argumentName,
        bool allowNumber,
        out string? value,
        out string? error) {
        value = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind == JsonValueKind.String) {
            value = raw.AsString();
            return true;
        }

        if (allowNumber && raw.Kind == JsonValueKind.Number) {
            var asInt64 = raw.AsInt64();
            if (asInt64.HasValue) {
                value = asInt64.Value.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            var asDouble = raw.AsDouble();
            if (asDouble.HasValue && double.IsFinite(asDouble.Value)) {
                value = asDouble.Value.ToString(CultureInfo.InvariantCulture);
                return true;
            }
        }

        error = $"{argumentName} must be a string.";
        return false;
    }

    private static bool TryReadOptionalPositiveInt32Array(
        JsonObject? arguments,
        string argumentName,
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
        if (array is { Count: > MaxEventIds }) {
            error = $"{argumentName} supports at most {MaxEventIds} values.";
            return false;
        }

        values = ToolArgs.TryReadPositiveInt32Array(array, argumentName, out error);
        return error is null;
    }

    private static bool TryReadOptionalPositiveInt64Array(
        JsonObject? arguments,
        string argumentName,
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

        if (array.Count > MaxRecordIds) {
            error = $"{argumentName} supports at most {MaxRecordIds} values.";
            return false;
        }

        var list = new List<long>(array.Count);
        var seen = new HashSet<long>();
        for (var i = 0; i < array.Count; i++) {
            var item = array[i].AsInt64();
            if (!item.HasValue || item.Value <= 0) {
                error = $"{argumentName} values must be positive integers.";
                return false;
            }

            if (seen.Add(item.Value)) {
                list.Add(item.Value);
            }
        }

        values = list;
        return true;
    }

    private static bool TryReadOptionalNamedDataMap(
        JsonObject? arguments,
        string argumentName,
        out IReadOnlyDictionary<string, IReadOnlyList<string>>? values,
        out string? error) {
        values = null;
        error = null;

        if (arguments is null || !arguments.TryGetValue(argumentName, out var raw) || raw is null || raw.Kind == JsonValueKind.Null) {
            return true;
        }

        if (raw.Kind != JsonValueKind.Object) {
            error = $"{argumentName} must be an object.";
            return false;
        }

        var map = raw.AsObject();
        if (map is null) {
            values = new Dictionary<string, IReadOnlyList<string>>();
            return true;
        }

        if (map.Count > MaxNamedDataKeys) {
            error = $"{argumentName} supports at most {MaxNamedDataKeys} keys.";
            return false;
        }

        var normalized = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in map) {
            if (!TryReadNamedDataValue(value, argumentName, key, out var parsed, out error)) {
                return false;
            }

            normalized[key] = parsed;
        }

        values = normalized;
        return true;
    }

    private static bool TryReadNamedDataValue(
        JsonValue value,
        string argumentName,
        string key,
        out IReadOnlyList<string> parsed,
        out string? error) {
        parsed = Array.Empty<string>();
        error = null;

        if (value is null || value.Kind == JsonValueKind.Null) {
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

            var items = new List<string>(array.Count);
            for (var i = 0; i < array.Count; i++) {
                if (!TryReadNamedDataScalar(array[i], argumentName, key, out var scalar, out error)) {
                    return false;
                }

                items.Add(scalar);
            }

            parsed = items;
            return true;
        }

        if (!TryReadNamedDataScalar(value, argumentName, key, out var normalized, out error)) {
            return false;
        }

        parsed = new[] { normalized };
        return true;
    }

    private static bool TryReadNamedDataScalar(
        JsonValue value,
        string argumentName,
        string key,
        out string normalized,
        out string? error) {
        normalized = string.Empty;
        error = null;

        switch (value.Kind) {
            case JsonValueKind.String:
                normalized = (value.AsString() ?? string.Empty).Trim();
                return true;
            case JsonValueKind.Number: {
                var asInt64 = value.AsInt64();
                if (asInt64.HasValue) {
                    normalized = asInt64.Value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                var asDouble = value.AsDouble();
                if (asDouble.HasValue && double.IsFinite(asDouble.Value)) {
                    normalized = asDouble.Value.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                error = $"{argumentName}.{key} contains an invalid numeric value.";
                return false;
            }
            case JsonValueKind.Boolean:
                normalized = value.AsBoolean().ToString();
                return true;
            default:
                error = $"{argumentName}.{key} must be a scalar or array of scalar values.";
                return false;
        }
    }
}
