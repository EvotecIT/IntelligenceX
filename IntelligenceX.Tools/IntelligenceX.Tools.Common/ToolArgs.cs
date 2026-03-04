using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helpers for reading tool arguments from <see cref="JsonObject"/>.
/// </summary>
/// <remarks>
/// Kept for transport compatibility and legacy wrappers. New or refactored tool wrappers
/// should prefer <see cref="ToolRequestBinder"/> + <see cref="ToolArgumentReader"/> so
/// binding, validation, and error shaping stay centralized and typed.
/// </remarks>
public static class ToolArgs {
    /// <summary>
    /// Controls how non-positive numeric arguments are normalized in bounded integer helpers.
    /// </summary>
    public enum NonPositiveInt32Behavior {
        /// <summary>
        /// Non-positive values are clamped to <c>minInclusive</c>.
        /// </summary>
        ClampToMinimum,
        /// <summary>
        /// Non-positive values keep the bounded default value.
        /// </summary>
        UseDefault
    }

    /// <summary>
    /// Reads an optional string and trims it. Returns <c>null</c> when missing/empty.
    /// </summary>
    public static string? GetOptionalTrimmed(JsonObject? arguments, string key) {
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return null;
        }

        var v = arguments.GetString(key);
        if (string.IsNullOrWhiteSpace(v)) {
            return null;
        }
        return v.Trim();
    }

    /// <summary>
    /// Reads an optional string and trims it. Returns <paramref name="defaultValue"/> when missing/empty.
    /// </summary>
    public static string GetTrimmedOrDefault(JsonObject? arguments, string key, string defaultValue) {
        var v = GetOptionalTrimmed(arguments, key);
        return string.IsNullOrWhiteSpace(v) ? (defaultValue ?? string.Empty) : v;
    }

    /// <summary>
    /// Reads a boolean value, using <paramref name="defaultValue"/> when missing.
    /// </summary>
    public static bool GetBoolean(JsonObject? arguments, string key, bool defaultValue = false) {
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return defaultValue;
        }
        return arguments.GetBoolean(key, defaultValue);
    }

    /// <summary>
    /// Reads an optional positive integer argument (stored as JSON integer) and applies a safety cap.
    /// </summary>
    /// <remarks>
    /// JSON numbers come in as 64-bit from our JSON model, so we normalize here.
    /// </remarks>
    public static int GetCappedInt32(JsonObject? arguments, string key, int defaultValue, int minInclusive, int maxInclusive) {
        return GetCappedInt32(arguments, key, defaultValue, minInclusive, maxInclusive, NonPositiveInt32Behavior.ClampToMinimum);
    }

    /// <summary>
    /// Reads an optional positive integer argument (stored as JSON integer) and applies a safety cap.
    /// </summary>
    /// <remarks>
    /// JSON numbers come in as 64-bit from our JSON model, so we normalize here.
    /// </remarks>
    public static int GetCappedInt32(
        JsonObject? arguments,
        string key,
        int defaultValue,
        int minInclusive,
        int maxInclusive,
        NonPositiveInt32Behavior nonPositiveBehavior) {
        if (maxInclusive < minInclusive) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
        var boundedDefault = Clamp(defaultValue, minInclusive, maxInclusive);
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return boundedDefault;
        }

        var raw = arguments.GetInt64(key);
        if (!raw.HasValue) {
            return boundedDefault;
        }

        if (raw.Value <= 0) {
            return nonPositiveBehavior == NonPositiveInt32Behavior.UseDefault
                ? boundedDefault
                : minInclusive;
        }

        if (raw.Value < minInclusive) {
            return minInclusive;
        }
        if (raw.Value > maxInclusive) {
            return maxInclusive;
        }
        return (int)raw.Value;
    }

    /// <summary>
    /// Reads an optional 64-bit integer argument and applies a safety cap.
    /// </summary>
    public static long GetCappedInt64(JsonObject? arguments, string key, long defaultValue, long minInclusive, long maxInclusive) {
        if (maxInclusive < minInclusive) throw new ArgumentOutOfRangeException(nameof(maxInclusive));
        if (arguments is null || string.IsNullOrWhiteSpace(key)) {
            return Clamp(defaultValue, minInclusive, maxInclusive);
        }

        var raw = arguments.GetInt64(key);
        if (!raw.HasValue) {
            return Clamp(defaultValue, minInclusive, maxInclusive);
        }
        if (raw.Value < minInclusive) {
            return minInclusive;
        }
        if (raw.Value > maxInclusive) {
            return maxInclusive;
        }
        return raw.Value;
    }

    /// <summary>
    /// Normalizes optional values used as hints (trim + return null when empty).
    /// </summary>
    public static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>
    /// Reads an optional JSON array of strings as a trimmed list (skips empty items).
    /// </summary>
    public static List<string> ReadStringArray(JsonArray? array) {
        var list = new List<string>();
        if (array is null || array.Count == 0) {
            return list;
        }

        for (var i = 0; i < array.Count; i++) {
            var v = array[i].AsString();
            if (string.IsNullOrWhiteSpace(v)) {
                continue;
            }
            list.Add(v!.Trim());
        }
        return list;
    }

    /// <summary>
    /// Reads an optional JSON array of strings as a trimmed list capped to <paramref name="maxItems"/>.
    /// </summary>
    public static List<string> ReadStringArrayCapped(JsonArray? array, int maxItems) {
        if (maxItems < 1) {
            return new List<string>();
        }

        var list = new List<string>(Math.Min(array?.Count ?? 0, maxItems));
        if (array is null || array.Count == 0) {
            return list;
        }

        for (var i = 0; i < array.Count; i++) {
            if (list.Count >= maxItems) {
                break;
            }

            var v = array[i].AsString();
            if (string.IsNullOrWhiteSpace(v)) {
                continue;
            }
            list.Add(v!.Trim());
        }
        return list;
    }

    /// <summary>
    /// Reads an optional JSON array of strings and returns a distinct trimmed list.
    /// </summary>
    public static List<string> ReadDistinctStringArray(JsonArray? array, StringComparer? comparer = null) {
        var raw = ReadStringArray(array);
        if (raw.Count <= 1) {
            return raw;
        }

        var dedup = new List<string>(raw.Count);
        var seen = new HashSet<string>(comparer ?? StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < raw.Count; i++) {
            var value = raw[i];
            if (seen.Add(value)) {
                dedup.Add(value);
            }
        }

        return dedup;
    }

    /// <summary>
    /// Reads an optional JSON array of positive integers and caps each value to <paramref name="maxInclusive"/>.
    /// Invalid and non-positive values are ignored.
    /// </summary>
    public static List<int> ReadPositiveInt32ArrayCapped(JsonArray? array, int maxInclusive) {
        if (maxInclusive < 1) {
            maxInclusive = int.MaxValue;
        }

        var list = new List<int>(array?.Count ?? 0);
        if (array is null || array.Count == 0) {
            return list;
        }

        for (var i = 0; i < array.Count; i++) {
            var value = array[i].AsInt64();
            if (!value.HasValue || value.Value <= 0) {
                continue;
            }

            var normalized = value.Value > int.MaxValue ? int.MaxValue : (int)value.Value;
            list.Add(Clamp(normalized, 1, maxInclusive));
        }

        return list;
    }

    /// <summary>
    /// Reads an optional JSON array of strings and filters it against an allowlist (case-insensitive if the allowlist is).
    /// Returns a list deduplicated in the caller-provided order.
    /// </summary>
    public static List<string> ReadAllowedStrings(JsonArray? array, ISet<string> allowed) {
        if (allowed is null) {
            throw new ArgumentNullException(nameof(allowed));
        }

        var raw = ReadStringArray(array);
        if (raw.Count == 0) {
            return raw;
        }

        var list = new List<string>(raw.Count);
        foreach (var name in raw) {
            if (!allowed.Contains(name)) {
                continue;
            }
            list.Add(name);
        }

        // Deduplicate preserving caller order.
        var dedup = new List<string>(list.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list) {
            if (seen.Add(item)) {
                dedup.Add(item);
            }
        }
        return dedup;
    }

    /// <summary>
    /// Reads an optional JSON array of positive 32-bit integers.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> when <paramref name="array"/> is missing or empty.
    /// </remarks>
    public static List<int>? TryReadPositiveInt32Array(JsonArray? array, string argumentName, out string? error) {
        error = null;
        if (array is null || array.Count == 0) {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(argumentName) ? "value" : argumentName.Trim();
        var list = new List<int>(array.Count);
        for (var i = 0; i < array.Count; i++) {
            var v = array[i].AsInt64();
            if (!v.HasValue) {
                error = $"{name} must be an array of integers.";
                return null;
            }

            if (v.Value <= 0 || v.Value > int.MaxValue) {
                error = $"{name} values must be positive 32-bit integers.";
                return null;
            }

            list.Add((int)v.Value);
        }

        return list;
    }

    /// <summary>
    /// Reads an optional positive integer argument where non-positive values fall back to the default.
    /// </summary>
    /// <remarks>
    /// Useful for "top N" style knobs where zero/negative values should keep default behavior.
    /// </remarks>
    public static int GetPositiveCappedInt32OrDefault(JsonObject? arguments, string key, int defaultValue, int maxInclusive) {
        if (maxInclusive < 1) {
            maxInclusive = 1;
        }

        return GetCappedInt32(
            arguments,
            key,
            defaultValue,
            minInclusive: 1,
            maxInclusive: maxInclusive,
            nonPositiveBehavior: NonPositiveInt32Behavior.UseDefault);
    }

    /// <summary>
    /// Reads an optional integer argument bounded by a caller-provided option max value.
    /// </summary>
    /// <remarks>
    /// Useful for pack/base helpers where many tools share the same configured max cap.
    /// </remarks>
    public static int GetOptionBoundedInt32(
        JsonObject? arguments,
        string key,
        int optionMaxInclusive,
        int minInclusive = 1) {
        return GetOptionBoundedInt32(
            arguments,
            key,
            optionMaxInclusive,
            minInclusive,
            nonPositiveBehavior: NonPositiveInt32Behavior.ClampToMinimum,
            defaultValue: optionMaxInclusive);
    }

    /// <summary>
    /// Reads an optional integer argument bounded by a caller-provided option max value,
    /// with explicit handling for non-positive values.
    /// </summary>
    /// <remarks>
    /// Use this overload to keep max-results normalization semantics explicit:
    /// either clamp non-positive values to <paramref name="minInclusive"/> or keep a bounded default.
    /// </remarks>
    public static int GetOptionBoundedInt32(
        JsonObject? arguments,
        string key,
        int optionMaxInclusive,
        int minInclusive,
        NonPositiveInt32Behavior nonPositiveBehavior,
        int? defaultValue = null) {
        var resolvedDefault = defaultValue ?? optionMaxInclusive;
        return GetCappedInt32(arguments, key, resolvedDefault, minInclusive, optionMaxInclusive, nonPositiveBehavior);
    }

    /// <summary>
    /// Reads an optional positive integer argument where non-positive values keep a default,
    /// then caps to a caller-provided option max value.
    /// </summary>
    /// <remarks>
    /// Useful for knobs where zero/negative should preserve default behavior instead of forcing the minimum.
    /// </remarks>
    public static int GetPositiveOptionBoundedInt32OrDefault(
        JsonObject? arguments,
        string key,
        int defaultValue,
        int optionMaxInclusive) {
        return GetOptionBoundedInt32(
            arguments,
            key,
            optionMaxInclusive,
            minInclusive: 1,
            nonPositiveBehavior: NonPositiveInt32Behavior.UseDefault,
            defaultValue: defaultValue);
    }

    /// <summary>
    /// Normalizes a nullable 64-bit value to a positive nullable 32-bit value.
    /// Returns <c>null</c> when value is null or non-positive.
    /// </summary>
    public static int? ToPositiveInt32OrNull(long? value, int maxInclusive = int.MaxValue) {
        if (!value.HasValue || value.Value <= 0) {
            return null;
        }

        var normalized = value.Value > int.MaxValue ? int.MaxValue : (int)value.Value;
        if (maxInclusive < 1) {
            maxInclusive = int.MaxValue;
        }

        return Clamp(normalized, 1, maxInclusive);
    }

    private static int Clamp(int v, int minInclusive, int maxInclusive) {
        if (v < minInclusive) return minInclusive;
        if (v > maxInclusive) return maxInclusive;
        return v;
    }

    private static long Clamp(long v, long minInclusive, long maxInclusive) {
        if (v < minInclusive) return minInclusive;
        if (v > maxInclusive) return maxInclusive;
        return v;
    }
}
