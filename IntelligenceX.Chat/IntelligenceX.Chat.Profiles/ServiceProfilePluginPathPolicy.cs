using System;
using System.Collections.Generic;
using System.IO;

namespace IntelligenceX.Chat.Profiles;

internal static class ServiceProfilePluginPathPolicy {
    internal static List<string> NormalizeStoredPluginPaths(IReadOnlyList<string>? values) {
        var normalized = new List<string>();
        if (values is null || values.Count == 0) {
            return normalized;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < values.Count; i++) {
            var candidate = NormalizePath(values[i]);
            if (candidate.Length == 0 || IsAppManagedPluginPath(candidate) || !seen.Add(candidate)) {
                continue;
            }

            normalized.Add(candidate);
        }

        return normalized;
    }

    private static bool IsAppManagedPluginPath(string path) {
        var fullPath = TryGetFullPath(path);
        if (fullPath is null) {
            return false;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData)) {
            var pluginCacheRoot = TryGetFullPath(Path.Combine(localAppData, "IntelligenceX.Chat", "plugin-cache"));
            if (IsSameOrNestedPath(fullPath, pluginCacheRoot)) {
                return true;
            }
        }

        var serviceRuntimeRoot = TryGetFullPath(Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "service-runtime"));
        if (IsSameOrNestedPath(fullPath, serviceRuntimeRoot)) {
            return true;
        }

        return IsPortableReleasePluginPath(fullPath);
    }

    private static bool IsPortableReleasePluginPath(string fullPath) {
        if (!string.Equals(Path.GetFileName(fullPath), "plugins", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var appRoot = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(appRoot)) {
            return false;
        }

        var appFolderName = Path.GetFileName(appRoot);
        if (!appFolderName.StartsWith("IntelligenceX.Chat", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var portableRoot = Path.GetDirectoryName(appRoot);
        if (!string.Equals(Path.GetFileName(portableRoot), "portable", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var releaseVersionRoot = Path.GetDirectoryName(portableRoot);
        if (string.IsNullOrWhiteSpace(releaseVersionRoot)) {
            return false;
        }

        var releasesRoot = Path.GetDirectoryName(releaseVersionRoot);
        if (!string.Equals(Path.GetFileName(releasesRoot), "Releases", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var artifactsRoot = Path.GetDirectoryName(releasesRoot);
        return string.Equals(Path.GetFileName(artifactsRoot), "artifacts", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrNestedPath(string fullPath, string? basePath) {
        if (string.IsNullOrWhiteSpace(basePath)) {
            return false;
        }

        if (string.Equals(fullPath, basePath, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var normalizedBase = basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path) {
        var trimmed = (path ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }

        return TryGetFullPath(trimmed) ?? trimmed;
    }

    private static string? TryGetFullPath(string path) {
        try {
            return Path.GetFullPath(path);
        } catch {
            return null;
        }
    }
}
