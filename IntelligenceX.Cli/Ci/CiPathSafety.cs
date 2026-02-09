using System;
using System.IO;

namespace IntelligenceX.Cli.Ci;

internal static class CiPathSafety {
    internal static bool IsUnderRoot(string path, string root) {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) {
            return false;
        }
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        var trimmedPath = Path.TrimEndingDirectorySeparator(fullPath);
        var trimmedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Consider the root directory itself as "under root" for containment checks.
        if (string.Equals(trimmedPath, trimmedRoot, comparison)) {
            return true;
        }

        var normalizedRoot = trimmedRoot + Path.DirectorySeparatorChar;
        return trimmedPath.StartsWith(normalizedRoot, comparison);
    }
}

