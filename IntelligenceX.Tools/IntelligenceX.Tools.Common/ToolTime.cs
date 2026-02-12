using System;
using System.Globalization;
using IntelligenceX.Json;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helpers for parsing tool time arguments.
/// </summary>
public static class ToolTime {
    /// <summary>
    /// Parses an optional ISO-8601 datetime string and normalizes it to UTC.
    /// </summary>
    /// <remarks>
    /// Accepts values without an explicit timezone offset by assuming UTC.
    /// </remarks>
    public static bool TryParseUtcOptional(string? value, out DateTime? utc, out string? error) {
        utc = null;
        error = null;

        if (string.IsNullOrWhiteSpace(value)) {
            return true;
        }

        if (!DateTime.TryParse(
                value,
                provider: null,
                styles: DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                result: out var parsed)) {
            error = "Invalid timestamp; expected an ISO-8601 datetime (UTC preferred).";
            return false;
        }

        utc = parsed.ToUniversalTime();
        return true;
    }

    /// <summary>
    /// Parses two optional UTC datetimes from <paramref name="arguments"/> and validates ordering.
    /// </summary>
    public static bool TryParseUtcRange(JsonObject? arguments, string startKey, string endKey, out DateTime? startUtc, out DateTime? endUtc, out string? error) {
        startUtc = null;
        endUtc = null;
        error = null;

        var startRaw = arguments?.GetString(startKey);
        var endRaw = arguments?.GetString(endKey);

        if (!TryParseUtcOptional(startRaw, out startUtc, out var startErr)) {
            error = $"{startKey}: {startErr}";
            return false;
        }
        if (!TryParseUtcOptional(endRaw, out endUtc, out var endErr)) {
            error = $"{endKey}: {endErr}";
            return false;
        }

        if (startUtc.HasValue && endUtc.HasValue && startUtc.Value > endUtc.Value) {
            error = $"{startKey} must be <= {endKey}.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Formats an optional UTC time as ISO-8601 (<c>O</c>) or empty string when null.
    /// </summary>
    public static string FormatUtc(DateTime? value) {
        return value.HasValue ? value.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : string.Empty;
    }
}
