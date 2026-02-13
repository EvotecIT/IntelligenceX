using System;
using System.IO;

namespace IntelligenceX.Chat.App;

internal static class StartupLog {
    private static readonly object LockObj = new();

    private static string LogPath {
        get {
            var dir = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat");
            return Path.Combine(dir, "app-startup.log");
        }
    }

    public static void Write(string message) {
        try {
            lock (LockObj) {
                var dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrWhiteSpace(dir)) {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(LogPath, $"[{DateTime.Now:O}] {message}{Environment.NewLine}");
            }
        } catch {
            // Ignore.
        }
    }
}

