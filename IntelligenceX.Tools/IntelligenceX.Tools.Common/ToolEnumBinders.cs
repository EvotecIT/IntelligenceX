using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared enum argument binders for tools.
/// </summary>
public static class ToolEnumBinders {
    /// <summary>
    /// Parses an optional enum argument where null/empty/"any" means no filter.
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    /// <param name="value">Raw argument value.</param>
    /// <param name="map">String-to-enum map.</param>
    /// <param name="argumentName">Argument name used in error text.</param>
    /// <param name="parsed">Parsed enum value or null for "any".</param>
    /// <param name="error">Validation error when parse fails.</param>
    /// <returns>True when parsed successfully; otherwise false.</returns>
    public static bool TryParseOptional<TEnum>(
        string? value,
        IReadOnlyDictionary<string, TEnum> map,
        string argumentName,
        out TEnum? parsed,
        out string? error)
        where TEnum : struct, Enum {
        if (map is null) {
            throw new ArgumentNullException(nameof(map));
        }
        if (string.IsNullOrWhiteSpace(argumentName)) {
            throw new ArgumentException("Argument name is required.", nameof(argumentName));
        }

        parsed = null;
        error = null;

        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length == 0 || string.Equals(normalized, "any", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (map.TryGetValue(normalized, out var parsedValue)) {
            parsed = parsedValue;
            return true;
        }

        var choices = string.Join(", ", map.Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
        error = $"{argumentName} must be one of: any, {choices}.";
        return false;
    }

    /// <summary>
    /// Parses an enum argument and falls back to a default value when unknown.
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    /// <param name="value">Raw argument value.</param>
    /// <param name="map">String-to-enum map.</param>
    /// <param name="defaultValue">Default value when unknown/missing.</param>
    /// <returns>Parsed enum value or <paramref name="defaultValue"/>.</returns>
    public static TEnum ParseOrDefault<TEnum>(
        string? value,
        IReadOnlyDictionary<string, TEnum> map,
        TEnum defaultValue)
        where TEnum : struct, Enum {
        if (map is null) {
            throw new ArgumentNullException(nameof(map));
        }

        var normalized = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (normalized.Length == 0 || string.Equals(normalized, "any", StringComparison.OrdinalIgnoreCase)) {
            return defaultValue;
        }

        return map.TryGetValue(normalized, out var parsedValue) ? parsedValue : defaultValue;
    }

    /// <summary>
    /// Converts an enum value to its canonical string key.
    /// </summary>
    /// <typeparam name="TEnum">Enum type.</typeparam>
    /// <param name="value">Enum value.</param>
    /// <param name="map">Enum-to-string map.</param>
    /// <returns>Mapped string key or numeric fallback.</returns>
    public static string ToName<TEnum>(TEnum value, IReadOnlyDictionary<TEnum, string> map)
        where TEnum : struct, Enum {
        if (map is null) {
            throw new ArgumentNullException(nameof(map));
        }

        if (map.TryGetValue(value, out var mapped) && !string.IsNullOrWhiteSpace(mapped)) {
            return mapped;
        }

        var underlying = Convert.ChangeType(value, Enum.GetUnderlyingType(typeof(TEnum)), CultureInfo.InvariantCulture);
        return Convert.ToString(underlying, CultureInfo.InvariantCulture) ?? value.ToString();
    }
}
