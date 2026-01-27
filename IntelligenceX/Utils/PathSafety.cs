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
}
