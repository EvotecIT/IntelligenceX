using System.Buffers;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace IntelligenceX.Chat.Abstractions.Storage;

/// <summary>
/// Provides bounded reads and crash-safe atomic replacement for small JSON state files.
/// </summary>
public static class ChatJsonFileStore {
    private const UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Reads a bounded JSON snapshot and distinguishes missing from malformed storage.
    /// </summary>
    public static ChatJsonFileReadResult Read(string path, long maximumBytes) {
        if (maximumBytes <= 0 || maximumBytes > int.MaxValue) {
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), maximumBytes, "The JSON read limit must be between 1 byte and 2 GB.");
        }

        if (string.IsNullOrWhiteSpace(path)) {
            return ChatJsonFileReadResult.Empty();
        }

        FileStream stream;
        try {
            stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.SequentialScan);
        } catch (FileNotFoundException) {
            return ChatJsonFileReadResult.Empty();
        } catch (DirectoryNotFoundException) {
            return ChatJsonFileReadResult.Empty();
        }

        using (stream) {
            if (stream.Length <= 0 || stream.Length > maximumBytes) {
                return ChatJsonFileReadResult.Invalid();
            }

            using var content = new MemoryStream(capacity: (int)stream.Length);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try {
                long totalBytes = 0;
                while (true) {
                    var remainingWithOverflowProbe = maximumBytes - totalBytes + 1;
                    var requestedBytes = (int)Math.Min(buffer.Length, remainingWithOverflowProbe);
                    var bytesRead = stream.Read(buffer, 0, requestedBytes);
                    if (bytesRead == 0) {
                        break;
                    }

                    totalBytes += bytesRead;
                    if (totalBytes > maximumBytes) {
                        return ChatJsonFileReadResult.Invalid();
                    }

                    content.Write(buffer, 0, bytesRead);
                }
            } finally {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (content.Length == 0) {
                return ChatJsonFileReadResult.Invalid();
            }

            var json = Encoding.UTF8.GetString(content.GetBuffer(), 0, (int)content.Length);
            return string.IsNullOrWhiteSpace(json)
                ? ChatJsonFileReadResult.Invalid()
                : ChatJsonFileReadResult.Loaded(json);
        }
    }

    /// <summary>
    /// Atomically replaces a JSON snapshot and applies owner-only file permissions.
    /// </summary>
    /// <param name="path">Destination JSON file path.</param>
    /// <param name="json">Serialized JSON content.</param>
    /// <param name="hardenExistingDirectory">
    /// Whether an existing Unix parent directory is dedicated to private chat state and may be restricted to the current user.
    /// Newly created directories are always restricted.
    /// </param>
    public static void Write(string path, string json, bool hardenExistingDirectory = false) {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(json);

        string? temporaryPath = null;
        try {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) {
                EnsureDirectory(directory, hardenExistingDirectory);
            }

            var fileName = Path.GetFileName(path);
            var temporaryName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            temporaryPath = string.IsNullOrWhiteSpace(directory)
                ? temporaryName
                : Path.Combine(directory, temporaryName);

            using (var stream = CreatePrivateTemporaryFile(temporaryPath)) {
                HardenTemporaryFile(temporaryPath);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path)) {
                HardenFile(path);
                File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            } else {
                File.Move(temporaryPath, path);
            }

            temporaryPath = null;
            HardenFile(path);
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
            File.SetUnixFileMode(path, PrivateDirectoryMode);
        }
    }

    private static void TryHardenUnixFile(string path) {
        if (!OperatingSystem.IsWindows()) {
            File.SetUnixFileMode(path, PrivateFileMode);
        }
    }

    private static void EnsureDirectory(string path, bool hardenExistingDirectory) {
        if (OperatingSystem.IsWindows()) {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(path, PrivateDirectoryMode);
        if (hardenExistingDirectory) {
            TryHardenUnixDirectory(path);
        }
    }

    private static FileStream CreatePrivateTemporaryFile(string path) {
        var options = new FileStreamOptions {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows()) {
            options.UnixCreateMode = PrivateFileMode;
        }

        return new FileStream(path, options);
    }

    private static void HardenTemporaryFile(string path) {
        HardenFile(path);
    }

    private static void HardenFile(string path) {
        if (OperatingSystem.IsWindows()) {
            HardenWindowsFile(path);
            return;
        }

        TryHardenUnixFile(path);
    }

    [SupportedOSPlatform("windows")]
    private static void HardenWindowsFile(string path) {
        var currentSid = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("The current Windows identity does not expose a security identifier.");
        var fileInfo = new FileInfo(path);
        var security = fileInfo.GetAccessControl();
        security.SetOwner(currentSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in security.GetAccessRules(
                     includeExplicit: true,
                     includeInherited: false,
                     typeof(SecurityIdentifier))) {
            security.RemoveAccessRuleSpecific(rule);
        }

        security.AddAccessRule(new FileSystemAccessRule(
            currentSid,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        fileInfo.SetAccessControl(security);
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
