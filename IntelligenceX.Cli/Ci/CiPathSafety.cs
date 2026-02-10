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
            var dirToCheck = Directory.Exists(fullPath)
                ? fullPath
                : (Path.GetDirectoryName(fullPath) ?? trimmedRoot);
            dirToCheck = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dirToCheck));
            while (!string.IsNullOrWhiteSpace(dirToCheck) &&
                   !Directory.Exists(dirToCheck) &&
                   !string.Equals(dirToCheck, trimmedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
                var parent = Path.GetDirectoryName(dirToCheck);
                if (string.IsNullOrWhiteSpace(parent)) {
                    break;
                }
                dirToCheck = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
            }

            var relative = Path.GetRelativePath(trimmedRoot, dirToCheck);
            if (relative.StartsWith("..", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
                return false;
            }

            var current = trimmedRoot;
            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
                current = Path.Combine(current, segment);
                if (File.Exists(current)) {
                    return false;
                }
                // Only validate reparse points for segments that exist. Non-existent leaf segments may be created later
                // via TryEnsureSafeDirectory or other safe creation flows.
                if (!Directory.Exists(current)) {
                    break;
                }
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0) {
                    return false;
                }
            }

            // If the leaf exists (file or directory), ensure it isn't itself a reparse point.
            if (File.Exists(fullPath) || Directory.Exists(fullPath)) {
                var leafAttrs = File.GetAttributes(fullPath);
                if ((leafAttrs & FileAttributes.ReparsePoint) != 0) {
                    return false;
                }
            }

            return true;
        } catch {
            return false;
        }
    }

    internal static bool TryEnsureSafeDirectory(string directoryPath, string root, out string error) {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(root)) {
            error = "Invalid directory path.";
            return false;
        }
        try {
            var fullDir = Path.GetFullPath(directoryPath);
            var fullRoot = Path.GetFullPath(root);
            if (!IsUnderRoot(fullDir, fullRoot)) {
                error = $"Directory must be within the workspace. dir={fullDir} workspace={fullRoot}";
                return false;
            }

            var trimmedRoot = Path.TrimEndingDirectorySeparator(fullRoot);
            if (Directory.Exists(trimmedRoot)) {
                var rootAttrs = File.GetAttributes(trimmedRoot);
                if ((rootAttrs & FileAttributes.ReparsePoint) != 0) {
                    error = "Workspace root is a symlink/junction (reparse point).";
                    return false;
                }
            }

            var relative = Path.GetRelativePath(trimmedRoot, fullDir);
            if (relative.StartsWith("..", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) {
                error = $"Directory must be within the workspace. dir={fullDir} workspace={trimmedRoot}";
                return false;
            }

            var current = trimmedRoot;
            foreach (var segment in relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)) {
                current = Path.Combine(current, segment);
                if (File.Exists(current)) {
                    error = $"Path segment is a file, not a directory: {current}";
                    return false;
                }
                if (!Directory.Exists(current)) {
                    Directory.CreateDirectory(current);
                }
                var attrs = File.GetAttributes(current);
                if ((attrs & FileAttributes.ReparsePoint) != 0) {
                    error = $"Path contains a symlink/junction component: {current}";
                    return false;
                }
            }

            return true;
        } catch (Exception ex) {
            error = ex.Message;
            return false;
        }
    }
}
