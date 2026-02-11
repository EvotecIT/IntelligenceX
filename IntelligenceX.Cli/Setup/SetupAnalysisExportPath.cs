using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Cli.Setup;

internal static class SetupAnalysisExportPath {
    public static bool TryNormalize(string? raw, out string? normalized, out string? error) {
        normalized = null;
        error = null;

        if (string.IsNullOrWhiteSpace(raw)) {
            return true;
        }

        var candidate = raw.Trim().Replace('\\', '/');
        if (candidate.StartsWith("/", StringComparison.Ordinal)) {
            error = "analysisExportPath must be repository-relative (no leading '/').";
            return false;
        }
        if (candidate.Contains(':', StringComparison.Ordinal)) {
            error = "analysisExportPath must be repository-relative (no drive prefixes).";
            return false;
        }

        var segments = new List<string>();
        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (segment.Equals(".", StringComparison.Ordinal) || segment.Equals("..", StringComparison.Ordinal)) {
                error = "analysisExportPath cannot contain '.' or '..' path segments.";
                return false;
            }
            if (!IsValidPathSegment(segment)) {
                error = $"analysisExportPath segment '{segment}' contains unsupported characters.";
                return false;
            }
            segments.Add(segment);
        }

        if (segments.Count == 0) {
            error = "analysisExportPath must not be empty.";
            return false;
        }

        normalized = string.Join("/", segments);
        return true;
    }

    public static string Combine(string normalizedBasePath, string fileName) {
        if (string.IsNullOrWhiteSpace(normalizedBasePath)) {
            throw new ArgumentException("Normalized export path is required.", nameof(normalizedBasePath));
        }
        if (string.IsNullOrWhiteSpace(fileName)) {
            throw new ArgumentException("File name is required.", nameof(fileName));
        }
        return normalizedBasePath.TrimEnd('/') + "/" + fileName.TrimStart('/');
    }

    private static bool IsValidPathSegment(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        foreach (var ch in value) {
            if (ch < ' ') {
                return false;
            }
            if (ch == '/' || ch == '\\' || ch == ':' || ch == '*' || ch == '?' ||
                ch == '"' || ch == '<' || ch == '>' || ch == '|') {
                return false;
            }
        }
        return true;
    }
}
