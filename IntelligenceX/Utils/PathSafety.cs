using System;
using System.IO;
using System.Runtime.InteropServices;

namespace IntelligenceX.Utils;

internal static class PathSafety {
    public static void EnsureFileExists(string path) {
        if (!File.Exists(path)) {
            throw new FileNotFoundException($"File not found: {path}", path);
        }
    }

    public static void EnsureMaxFileSize(string path, long maxBytes) {
        if (maxBytes <= 0) {
            return;
        }
        var info = new FileInfo(path);
        if (info.Length > maxBytes) {
            throw new InvalidOperationException($"File '{path}' exceeds max size {maxBytes} bytes.");
        }
    }

    public static void EnsureUnderRoot(string path, string root) {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        if (!IsUnderRoot(fullPath, fullRoot)) {
            throw new InvalidOperationException($"Path '{fullPath}' is outside workspace '{fullRoot}'.");
        }
        EnsureNoReparsePointTraversal(fullRoot, fullPath);
    }

    private static bool IsUnderRoot(string path, string root) {
        var comparison = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), comparison)) {
            root += Path.DirectorySeparatorChar;
        }
        return path.StartsWith(root, comparison);
    }

    private static void EnsureNoReparsePointTraversal(string root, string path) {
        // Path.GetFullPath already normalized separators for the current platform.
        // This check is best-effort: it only inspects path segments that exist on disk.
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path)) {
            return;
        }

        // If the root doesn't exist we can't reliably inspect segments. Keep the legacy behavior.
        if (!Directory.Exists(root)) {
            return;
        }

        var relative = Path.GetRelativePath(root, path);
        if (string.IsNullOrWhiteSpace(relative) ||
            relative.StartsWith("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(relative)) {
            return;
        }

        var current = root;
        var parts = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts) {
            current = Path.Combine(current, part);
            if (!File.Exists(current) && !Directory.Exists(current)) {
                // Stop at the first non-existing segment; we can't evaluate symlink traversal beyond this point.
                break;
            }
            var attr = File.GetAttributes(current);
            if ((attr & FileAttributes.ReparsePoint) != 0) {
                throw new InvalidOperationException($"Path '{path}' traverses a symlink/junction at '{current}'.");
            }
        }
    }
}
