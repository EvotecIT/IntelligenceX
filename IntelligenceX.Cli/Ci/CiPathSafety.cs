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

    internal static bool IsUnderRootPhysical(string path, string root) {
        if (!IsUnderRoot(path, root)) {
            return false;
        }
        try {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root);
            var trimmedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
            if (Directory.Exists(trimmedRoot)) {
                var rootAttrs = File.GetAttributes(trimmedRoot);
                if ((rootAttrs & FileAttributes.ReparsePoint) != 0) {
                    return false;
                }
            }

            // Check each existing directory segment under the root for symlinks/junctions.
            // This is best-effort and reduces the risk of writing outside the workspace via reparse points.
            var dirToCheck = Directory.Exists(fullPath) ? fullPath : (Path.GetDirectoryName(fullPath) ?? trimmedRoot);
            dirToCheck = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirToCheck));

            var relative = Path.GetRelativePath(trimmedRoot, dirToCheck);
            if (relative.StartsWith("..", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
                return false;
            }

            var current = trimmedRoot;
            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current)) {
                    // Non-existent segments will be created under root; existing segments are what can be symlink attacks.
                    continue;
                }
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0) {
                    return false;
                }
            }

            return true;
        } catch {
            return false;
        }
    }
}
