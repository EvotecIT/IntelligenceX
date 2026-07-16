using System.Text;

namespace IntelligenceX.Chat.Abstractions.Storage;

/// <summary>
/// Provides bounded reads and crash-safe atomic replacement for small JSON state files.
/// </summary>
public static class ChatJsonFileStore {
    /// <summary>
    /// Reads a bounded JSON snapshot and distinguishes missing from malformed storage.
    /// </summary>
    public static ChatJsonFileReadResult Read(string path, long maximumBytes) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return ChatJsonFileReadResult.Empty();
        }

        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length > maximumBytes) {
            return ChatJsonFileReadResult.Invalid();
        }

        var json = File.ReadAllText(path, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(json)
            ? ChatJsonFileReadResult.Invalid()
            : ChatJsonFileReadResult.Loaded(json);
    }

    /// <summary>
    /// Atomically replaces a JSON snapshot and applies owner-only Unix permissions.
    /// </summary>
    public static void Write(string path, string json) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);

        string? temporaryPath = null;
        try {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            if (!string.IsNullOrWhiteSpace(directory)) {
                TryHardenUnixDirectory(directory);
            }

            var fileName = Path.GetFileName(path);
            var temporaryName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            temporaryPath = string.IsNullOrWhiteSpace(directory)
                ? temporaryName
                : Path.Combine(directory, temporaryName);

            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                TryHardenUnixFile(temporaryPath);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path)) {
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            } else {
                File.Move(temporaryPath, path);
            }

            temporaryPath = null;
            TryHardenUnixFile(path);
        } finally {
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath)) {
                try {
                    File.Delete(temporaryPath);
                } catch {
                    // Preserve the original write failure.
                }
            }
        }
    }

    /// <summary>
    /// Deletes a JSON snapshot when it exists.
    /// </summary>
    public static void Delete(string path) {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
            File.Delete(path);
        }
    }

    private static void TryHardenUnixDirectory(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void TryHardenUnixFile(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

/// <summary>
/// Describes the outcome of a bounded JSON file read.
/// </summary>
public enum ChatJsonFileReadState {
    /// <summary>The snapshot does not exist.</summary>
    Empty,
    /// <summary>The snapshot exists but is empty, oversized, or whitespace-only.</summary>
    Invalid,
    /// <summary>The snapshot contains bounded non-whitespace JSON text.</summary>
    Loaded
}

/// <summary>
/// Contains a bounded JSON file read result.
/// </summary>
public readonly record struct ChatJsonFileReadResult(ChatJsonFileReadState State, string? Json) {
    internal static ChatJsonFileReadResult Empty() => new(ChatJsonFileReadState.Empty, null);
    internal static ChatJsonFileReadResult Invalid() => new(ChatJsonFileReadState.Invalid, null);
    internal static ChatJsonFileReadResult Loaded(string json) => new(ChatJsonFileReadState.Loaded, json);
}
