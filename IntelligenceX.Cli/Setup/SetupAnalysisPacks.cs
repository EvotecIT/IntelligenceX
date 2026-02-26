using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace IntelligenceX.Cli.Setup;

internal static class SetupAnalysisPacks {
    private static readonly Regex PackIdRegex = new("^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,126}[A-Za-z0-9])?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Keep limits conservative; these are user-controlled inputs (wizard/web payloads).
    private const int MaxIds = 100;
    private const int MaxNormalizedLength = 2048;

    /// <summary>
    /// Normalize a comma-separated pack id list.
    /// Returns <c>true</c> with <paramref name="normalizedCsv"/> = <c>null</c> to represent "use defaults".
    /// </summary>
    public static bool TryNormalizeCsv(string? raw, out string? normalizedCsv, out string? error) {
        normalizedCsv = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw)) {
            return true;
        }

        var parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) {
            return true;
        }

        var ids = new List<string>(parts.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts) {
            if (string.IsNullOrWhiteSpace(part)) {
                continue;
            }
            if (!PackIdRegex.IsMatch(part)) {
                error = $"Invalid pack id '{part}'. Use comma-separated ids like all-50, all-multilang-50, all-security-50, all-security-default, powershell-50, javascript-50, python-50.";
                return false;
            }
            if (seen.Add(part)) {
                ids.Add(part);
            }
        }

        if (ids.Count == 0) {
            return true;
        }
        if (ids.Count > MaxIds) {
            error = $"Too many pack ids (max {MaxIds}).";
            return false;
        }

        var joined = string.Join(",", ids);
        if (joined.Length > MaxNormalizedLength) {
            error = "Pack list is too long.";
            return false;
        }

        normalizedCsv = joined;
        return true;
    }
}
