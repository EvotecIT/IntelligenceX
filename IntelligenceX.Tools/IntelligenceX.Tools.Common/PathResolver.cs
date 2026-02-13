using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared safe-by-default path resolution used by file-based tools.
/// </summary>
public static class PathResolver {
    /// <summary>
    /// Resolves an input path to a full path and enforces an allowed-roots allowlist.
    /// </summary>
    public static bool TryResolvePath(string inputPath, IReadOnlyList<string> allowedRoots, out string fullPath, out string error) {
        fullPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(inputPath)) {
            error = "Path is required.";
            return false;
        }

        string candidate;
        try {
            candidate = Path.GetFullPath(inputPath);
        } catch (Exception ex) {
            error = $"Invalid path: {ex.Message}";
            return false;
        }

        if (allowedRoots is null || allowedRoots.Count == 0) {
            error = "Access denied: no AllowedRoots configured.";
            return false;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var root in allowedRoots) {
            if (string.IsNullOrWhiteSpace(root)) {
                continue;
            }

            string rootFull;
            try {
                rootFull = Path.GetFullPath(root);
            } catch {
                continue;
            }

            if (!rootFull.EndsWith(Path.DirectorySeparatorChar)) {
                rootFull += Path.DirectorySeparatorChar;
            }

            if (candidate.StartsWith(rootFull, comparison) ||
                string.Equals(candidate, rootFull.TrimEnd(Path.DirectorySeparatorChar), comparison)) {
                fullPath = candidate;
                return true;
            }
        }

        error = "Access denied: path is outside AllowedRoots.";
        return false;
    }
}

