using System;
using System.IO;

namespace IntelligenceX.Chat.Tests;

internal static class TempPathTestHelper {
    internal static string CreateTempFilePath(string prefix, string extension) {
        var normalizedExtension = extension.StartsWith(".", StringComparison.Ordinal)
            ? extension
            : "." + extension;
        return Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N") + normalizedExtension);
    }

    internal static string CreateTempDirectoryPath(string prefix) {
        return Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
    }

    internal static void TryDeleteFile(string path) {
        try {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        } catch {
            // Best-effort cleanup.
        }
    }
}
