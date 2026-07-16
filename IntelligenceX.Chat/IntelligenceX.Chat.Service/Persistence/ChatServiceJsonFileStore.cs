using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace IntelligenceX.Chat.Service.Persistence;

/// <summary>
/// Provides the single durable-file implementation used by chat service JSON snapshot stores.
/// Domain stores retain ownership of their schemas, validation, locking, and retention rules.
/// </summary>
internal static class ChatServiceJsonFileStore {
    /// <summary>
    /// Resolves a chat service state file beneath local application data, with the OS temporary directory as a safe fallback.
    /// </summary>
    internal static string ResolveDefaultPath(string fileName) {
        return ResolveDefaultPath(
            fileName,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath());
    }

    internal static string ResolveDefaultPath(string fileName, string? localApplicationData, string temporaryPath) {
        var normalizedFileName = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (normalizedFileName.Length == 0) {
            throw new ArgumentException("A state file name is required.", nameof(fileName));
        }

        return Path.Combine(ResolveDefaultDirectory(localApplicationData, temporaryPath), normalizedFileName);
    }

    internal static string ResolveDefaultDirectory() {
        return ResolveDefaultDirectory(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath());
    }

    internal static string ResolveDefaultDirectory(string? localApplicationData, string temporaryPath) {
        var root = string.IsNullOrWhiteSpace(localApplicationData)
            ? temporaryPath
            : localApplicationData;
        if (string.IsNullOrWhiteSpace(root)) {
            throw new ArgumentException("A temporary state directory is required when local application data is unavailable.", nameof(temporaryPath));
        }

        return Path.Combine(root, "IntelligenceX.Chat");
    }

    /// <summary>
    /// Resolves a state file beside an already validated anchor store path.
    /// </summary>
    internal static string ResolveSiblingPath(string anchorPath, string fileName) {
        var normalizedFileName = Path.GetFileName((fileName ?? string.Empty).Trim());
        if (normalizedFileName.Length == 0) {
            throw new ArgumentException("A state file name is required.", nameof(fileName));
        }

        var directory = Path.GetDirectoryName(anchorPath);
        return !string.IsNullOrWhiteSpace(directory)
            ? Path.Combine(directory, normalizedFileName)
            : ResolveDefaultPath(normalizedFileName);
    }

    /// <summary>
    /// Reads and validates a JSON snapshot without allowing storage failures to escape into chat execution.
    /// </summary>
    internal static ChatServiceJsonFileReadResult<T> Read<T>(
        string path,
        long maximumBytes,
        Func<string, T?> deserialize,
        Func<T, bool> validate,
        Action<T>? normalize,
        string storeName) where T : class {
        ArgumentNullException.ThrowIfNull(deserialize);
        ArgumentNullException.ThrowIfNull(validate);

        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return ChatServiceJsonFileReadResult<T>.Empty();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > maximumBytes) {
                return ChatServiceJsonFileReadResult<T>.Invalid();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return ChatServiceJsonFileReadResult<T>.Empty();
            }

            var value = deserialize(json);
            if (value is null || !validate(value)) {
                return ChatServiceJsonFileReadResult<T>.Invalid();
            }

            normalize?.Invoke(value);
            return ChatServiceJsonFileReadResult<T>.Loaded(value);
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} read failed: {ex.GetType().Name}: {ex.Message}");
            return ChatServiceJsonFileReadResult<T>.Invalid();
        }
    }

    /// <summary>
    /// Reads a valid JSON snapshot or creates an empty domain store when no usable snapshot exists.
    /// </summary>
    internal static T ReadOrCreate<T>(
        string path,
        long maximumBytes,
        Func<string, T?> deserialize,
        Func<T, bool> validate,
        Action<T>? normalize,
        Func<T> create,
        string storeName) where T : class {
        ArgumentNullException.ThrowIfNull(create);

        var result = Read(path, maximumBytes, deserialize, validate, normalize, storeName);
        return result.State == ChatServiceJsonFileReadState.Loaded && result.Value is not null
            ? result.Value
            : create();
    }

    /// <summary>
    /// Atomically replaces a JSON snapshot and applies best-effort per-user ACL hardening.
    /// </summary>
    internal static void Write<T>(
        string path,
        T value,
        Func<T, string> serialize,
        string storeName) {
        ArgumentNullException.ThrowIfNull(serialize);

        string? temporaryPath = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = serialize(value);
            var fileName = Path.GetFileName(path);
            var temporaryName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            temporaryPath = string.IsNullOrWhiteSpace(directory)
                ? temporaryName
                : Path.Combine(directory, temporaryName);

            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                TryHardenAclNoThrow(temporaryPath, storeName);
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
            TryHardenAclNoThrow(path, storeName);
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} write failed: {ex.GetType().Name}: {ex.Message}");
        } finally {
            if (!string.IsNullOrWhiteSpace(temporaryPath) && File.Exists(temporaryPath)) {
                try {
                    File.Delete(temporaryPath);
                } catch {
                    // Best effort only.
                }
            }
        }
    }

    /// <summary>
    /// Deletes a snapshot without allowing cleanup failures to escape into chat execution.
    /// </summary>
    internal static void Delete(string path, string storeName) {
        try {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) {
                File.Delete(path);
            }
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} clear failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryHardenAclNoThrow(string path, string storeName) {
        if (string.IsNullOrWhiteSpace(path) || !OperatingSystem.IsWindows()) {
            return;
        }

        try {
            if (!File.Exists(path)) {
                return;
            }

            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0) {
                return;
            }

            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid is null) {
                return;
            }

            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl();
            security.SetOwner(currentSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
            security.SetAccessRule(new FileSystemAccessRule(
                currentSid,
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            fileInfo.SetAccessControl(security);
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} ACL hardening failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string NormalizeStoreName(string storeName) {
        var normalized = (storeName ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Chat service JSON store" : normalized;
    }
}

internal enum ChatServiceJsonFileReadState {
    Empty,
    Invalid,
    Loaded
}

internal readonly record struct ChatServiceJsonFileReadResult<T>(
    ChatServiceJsonFileReadState State,
    T? Value) where T : class {
    internal static ChatServiceJsonFileReadResult<T> Empty() =>
        new(ChatServiceJsonFileReadState.Empty, null);

    internal static ChatServiceJsonFileReadResult<T> Invalid() =>
        new(ChatServiceJsonFileReadState.Invalid, null);

    internal static ChatServiceJsonFileReadResult<T> Loaded(T value) =>
        new(ChatServiceJsonFileReadState.Loaded, value);
}
