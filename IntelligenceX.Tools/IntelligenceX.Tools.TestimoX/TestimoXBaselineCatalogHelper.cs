using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ComputerX.Controls;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXBaselineCatalogHelper {
    internal static readonly string[] VendorNames = Enum.GetNames(typeof(CxVendor))
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    internal static readonly string[] ProductNames = {
        "Windows-10-1507",
        "Windows-10-1607",
        "Windows-10-1809",
        "Windows-11-22H2",
        "Windows-11-23H2",
        "Windows-11-24H2",
        "Windows-Server-2016",
        "Windows-Server-2019",
        "Windows-Server-2022",
        "Windows-Server-2025"
    };

    internal static bool TryParseSet(
        IReadOnlyList<string> requestedValues,
        IReadOnlyList<string> supportedValues,
        string argumentName,
        out HashSet<string>? parsedValues,
        out string? error) {
        parsedValues = null;
        error = null;

        if (requestedValues.Count == 0) {
            return true;
        }

        var supported = new HashSet<string>(supportedValues, StringComparer.OrdinalIgnoreCase);
        var parsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in requestedValues) {
            if (!supported.Contains(value)) {
                error = $"{argumentName} contains unsupported value '{value}'. Supported values: {string.Join(", ", supportedValues)}.";
                return false;
            }

            parsed.Add(value);
        }

        parsedValues = parsed;
        return true;
    }

    internal static ParsedBaselineId ParseBaselineId(string baselineId) {
        var parts = (baselineId ?? string.Empty).Split('/');
        return new ParsedBaselineId(
            BaselineId: baselineId ?? string.Empty,
            VendorId: parts.Length >= 1 ? parts[0] : string.Empty,
            ProductId: parts.Length >= 2 ? parts[1] : string.Empty,
            Version: parts.Length >= 3 ? parts[2] : string.Empty);
    }

    internal static bool WildcardMatch(string? value, string pattern) {
        if (string.IsNullOrWhiteSpace(pattern)) {
            return false;
        }

        var candidate = value ?? string.Empty;
        if (string.Equals(pattern, "*", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(candidate, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    internal static bool ContainsOrdinalIgnoreCase(string? value, string search) {
        if (string.IsNullOrWhiteSpace(search)) {
            return true;
        }

        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    internal static string FormatDesiredValue(object? value) {
        if (value is null) {
            return string.Empty;
        }

        if (value is Array array) {
            return string.Join(
                ", ",
                array.Cast<object?>()
                    .Select(static item => item?.ToString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item)));
        }

        return value.ToString() ?? string.Empty;
    }

    internal readonly record struct ParsedBaselineId(
        string BaselineId,
        string VendorId,
        string ProductId,
        string Version);
}
