using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Common JSON helpers shared by tool implementations.
/// </summary>
public static class ToolJson {
    private const int MaxFallbackDepth = 8;

    private static readonly JsonSerializerOptions SnakeCaseSerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // Keep dictionary keys as-is. Many tool payloads carry provider-defined keys (event data names, LDAP attribute names, headers)
        // and callers expect exact casing/spelling. Prefer modeling standardized outputs as lists/records instead of dictionaries.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Maps an enumerable to a <see cref="JsonArray"/> using <see cref="JsonMapper"/>.
     /// </summary>
    public static JsonArray ToJsonArray(IEnumerable values) => JsonMapper.FromEnumerable(values);

    /// <summary>
    /// Maps an enumerable to a <see cref="JsonArray"/> using <see cref="JsonMapper"/>.
    /// </summary>
    public static JsonArray ToJsonArray<T>(IEnumerable<T> values) => JsonMapper.FromEnumerable(values);

    /// <summary>
    /// Adds optional UTC start/end keys to a JSON object using the tool contract keys.
    /// </summary>
    public static void AddUtcRange(JsonObject obj, DateTime? startUtc, DateTime? endUtc) {
        if (obj is null) throw new ArgumentNullException(nameof(obj));

        if (startUtc.HasValue) {
            obj.Add("start_time_utc", startUtc.Value.ToString("O"));
        }
        if (endUtc.HasValue) {
            obj.Add("end_time_utc", endUtc.Value.ToString("O"));
        }
    }

    /// <summary>
    /// Converts a string dictionary into an <see cref="JsonObject"/> without renaming keys.
    /// </summary>
    /// <remarks>
    /// This is intentionally not snake_case: many tool payloads contain provider-defined keys (event data, headers, etc.)
    /// where the original key names should be preserved.
    /// </remarks>
    public static JsonObject ToJsonObject(IReadOnlyDictionary<string, string>? dict) {
        var obj = new JsonObject(StringComparer.Ordinal);
        if (dict is null || dict.Count == 0) {
            return obj;
        }
        foreach (var kvp in dict) {
            obj.Add(kvp.Key, kvp.Value ?? string.Empty);
        }
        return obj;
    }

    /// <summary>
    /// Converts a typed object graph into an <see cref="JsonObject"/> using snake_case keys.
    /// </summary>
    /// <remarks>
    /// Intended to reduce per-tool manual <c>new JsonObject().Add(...)</c> boilerplate while keeping tool outputs
    /// consistent with the UI/tool contract naming convention.
    /// </remarks>
    public static JsonObject ToJsonObjectSnakeCase(object? value) {
        if (value is null) {
            return new JsonObject(StringComparer.Ordinal);
        }
        if (value is JsonObject obj) {
            return obj;
        }

        System.Text.Json.Nodes.JsonNode? node;
        try {
            node = JsonSerializer.SerializeToNode(value, SnakeCaseSerializerOptions);
        } catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or JsonException or ArgumentException) {
            var sanitized = SanitizeForJsonFallback(
                value,
                seen: new HashSet<object>(ReferenceEqualityComparer.Instance),
                depth: 0);
            node = JsonSerializer.SerializeToNode(sanitized, SnakeCaseSerializerOptions);
        }

        var converted = ConvertNode(node);

        if (converted is JsonObject root) {
            return root;
        }

        // ToolResponse expects object-shaped root payload fields. Wrap non-object values.
        return new JsonObject(StringComparer.Ordinal).Add("value", JsonMapper.FromObject(converted ?? string.Empty));
    }

    private static object? ConvertNode(System.Text.Json.Nodes.JsonNode? node) {
        if (node is null) {
            return null;
        }

        if (node is System.Text.Json.Nodes.JsonObject obj) {
            var root = new JsonObject(StringComparer.Ordinal);
            foreach (var kvp in obj) {
                root.Add(kvp.Key, JsonMapper.FromObject(ConvertNode(kvp.Value)));
            }
            return root;
        }

        if (node is System.Text.Json.Nodes.JsonArray arr) {
            var root = new JsonArray();
            foreach (var v in arr) {
                root.Add(JsonMapper.FromObject(ConvertNode(v)));
            }
            return root;
        }

        if (node is System.Text.Json.Nodes.JsonValue val) {
            if (val.TryGetValue<string>(out var s)) return s;
            if (val.TryGetValue<bool>(out var b)) return b;
            if (val.TryGetValue<int>(out var i)) return i;
            if (val.TryGetValue<long>(out var l)) return l;
            if (val.TryGetValue<double>(out var d)) return d;
            if (val.TryGetValue<decimal>(out var dec)) return (double)dec;
            if (val.TryGetValue<DateTimeOffset>(out var dto)) return dto.ToUniversalTime().ToString("O");
            if (val.TryGetValue<DateTime>(out var dt)) return dt.ToUniversalTime().ToString("O");

            return val.ToJsonString();
        }

        return node.ToJsonString();
    }

    private static object? SanitizeForJsonFallback(object? value, HashSet<object> seen, int depth) {
        if (value is null) {
            return null;
        }

        if (depth > MaxFallbackDepth) {
            return ConvertToInvariantString(value);
        }

        switch (value) {
            case string text:
                return text;
            case bool b:
                return b;
            case byte n:
                return n;
            case sbyte n:
                return n;
            case short n:
                return n;
            case ushort n:
                return n;
            case int n:
                return n;
            case uint n:
                return n;
            case long n:
                return n;
            case ulong n:
                return n;
            case float n:
                return n;
            case double n:
                return n;
            case decimal n:
                return n;
            case DateTime dt:
                return dt.ToUniversalTime().ToString("O");
            case DateTimeOffset dto:
                return dto.ToUniversalTime().ToString("O");
            case TimeSpan ts:
                return ts.ToString();
            case Guid guid:
                return guid.ToString();
            case Uri uri:
                return uri.ToString();
            case Type type:
                return type.FullName ?? type.Name;
            case JsonObject or JsonArray:
                return value;
            case System.Text.Json.Nodes.JsonNode jsonNode:
                return ConvertNode(jsonNode);
        }

        var typeInfo = value.GetType();
        if (typeInfo.IsEnum) {
            return value.ToString();
        }

        var trackReference = !typeInfo.IsValueType;
        if (trackReference && !seen.Add(value)) {
            return "[cycle]";
        }

        try {
            if (value is Array array && array.Rank > 1) {
                return SanitizeMultidimensionalArrayFallback(array, seen, depth + 1);
            }

            if (value is IDictionary dictionary) {
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in dictionary) {
                    var key = ConvertToInvariantString(entry.Key);
                    if (string.IsNullOrWhiteSpace(key)) {
                        continue;
                    }

                    normalized[key] = SanitizeForJsonFallback(entry.Value, seen, depth + 1);
                }
                return normalized;
            }

            if (value is IEnumerable enumerable) {
                var list = new List<object?>();
                foreach (var item in enumerable) {
                    list.Add(SanitizeForJsonFallback(item, seen, depth + 1));
                }
                return list;
            }

            var props = typeInfo.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            if (props.Length > 0) {
                var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
                var wroteAny = false;
                foreach (var prop in props) {
                    if (!prop.CanRead || prop.GetIndexParameters().Length != 0) {
                        continue;
                    }

                    object? propValue;
                    try {
                        propValue = prop.GetValue(value);
                    } catch {
                        continue;
                    }

                    var key = SnakeCaseSerializerOptions.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;
                    normalized[key] = SanitizeForJsonFallback(propValue, seen, depth + 1);
                    wroteAny = true;
                }

                if (wroteAny) {
                    return normalized;
                }
            }

            return ConvertToInvariantString(value);
        } finally {
            if (trackReference) {
                seen.Remove(value);
            }
        }
    }

    /// <summary>
    /// Shapes rank-2+ arrays into a stable fallback envelope consumed by chat/tool clients.
    /// </summary>
    /// <remarks>
    /// Contract fields are:
    /// <c>rank</c>, <c>lengths</c>, <c>lower_bounds</c>, and nested <c>values</c>.
    /// Values are emitted as zero-based nested lists while preserving source lower bounds in metadata.
    /// </remarks>
    private static Dictionary<string, object?> SanitizeMultidimensionalArrayFallback(
        Array array,
        HashSet<object> seen,
        int depth) {
        var rank = array.Rank;
        var lengths = new int[rank];
        var lowerBounds = new int[rank];
        for (var dimension = 0; dimension < rank; dimension++) {
            lengths[dimension] = array.GetLength(dimension);
            lowerBounds[dimension] = array.GetLowerBound(dimension);
        }

        var indexes = new int[rank];
        var values = SanitizeMultidimensionalArrayLevel(
            array: array,
            dimension: 0,
            indexes: indexes,
            lowerBounds: lowerBounds,
            seen: seen,
            depth: depth);

        return new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["rank"] = rank,
            ["lengths"] = lengths,
            ["lower_bounds"] = lowerBounds,
            ["values"] = values
        };
    }

    private static List<object?> SanitizeMultidimensionalArrayLevel(
        Array array,
        int dimension,
        int[] indexes,
        int[] lowerBounds,
        HashSet<object> seen,
        int depth) {
        var length = array.GetLength(dimension);
        var values = new List<object?>(length);

        if (dimension == array.Rank - 1) {
            for (var index = 0; index < length; index++) {
                indexes[dimension] = lowerBounds[dimension] + index;
                values.Add(SanitizeForJsonFallback(array.GetValue(indexes), seen, depth + 1));
            }

            return values;
        }

        for (var index = 0; index < length; index++) {
            indexes[dimension] = lowerBounds[dimension] + index;
            values.Add(SanitizeMultidimensionalArrayLevel(array, dimension + 1, indexes, lowerBounds, seen, depth + 1));
        }

        return values;
    }

    private static string ConvertToInvariantString(object? value) {
        if (value is null) {
            return string.Empty;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
