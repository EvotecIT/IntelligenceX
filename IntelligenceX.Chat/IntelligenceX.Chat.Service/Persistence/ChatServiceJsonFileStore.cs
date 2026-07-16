using System.Diagnostics;
using IntelligenceX.Chat.Abstractions.Storage;

namespace IntelligenceX.Chat.Service.Persistence;

/// <summary>
/// Provides the single durable-file implementation used by chat service JSON snapshot stores.
/// Domain stores retain ownership of their schemas, validation, locking, and retention rules.
/// </summary>
internal static class ChatServiceJsonFileStore {
    /// <summary>
    /// Resolves a chat service state file beneath the shared durable per-user state directory.
    /// </summary>
    internal static string ResolveDefaultPath(string fileName) {
        return ChatStatePaths.GetDefaultPath(fileName);
    }

    internal static string ResolveDefaultDirectory() {
        return ChatStatePaths.GetDefaultDirectory();
    }

    /// <summary>
    /// Resolves an optional store override while keeping it inside the shared state directory.
    /// </summary>
    internal static string ResolvePathOverrideWithinDefaultDirectory(string? candidate, string defaultFileName) {
        var defaultPath = ResolveDefaultPath(defaultFileName);
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0) {
            return defaultPath;
        }

        try {
            if (normalizedCandidate.StartsWith(@"\\", StringComparison.Ordinal)) {
                return defaultPath;
            }

            if (!Path.IsPathFullyQualified(normalizedCandidate)) {
                var fileName = Path.GetFileName(normalizedCandidate);
                if (fileName.Length == 0
                    || fileName is "." or ".."
                    || !string.Equals(fileName, normalizedCandidate, StringComparison.Ordinal)) {
                    return defaultPath;
                }

                return Path.Combine(ResolveDefaultDirectory(), fileName);
            }

            var fullCandidate = Path.GetFullPath(normalizedCandidate);
            return ChatStatePaths.IsPathInDefaultDirectory(fullCandidate)
                ? fullCandidate
                : defaultPath;
        } catch {
            return defaultPath;
        }
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
            var snapshot = ChatJsonFileStore.Read(path, maximumBytes);
            if (snapshot.State == ChatJsonFileReadState.Empty) {
                return ChatServiceJsonFileReadResult<T>.Empty();
            }
            if (snapshot.State != ChatJsonFileReadState.Loaded || snapshot.Json is null) {
                return ChatServiceJsonFileReadResult<T>.Invalid();
            }

            var value = deserialize(snapshot.Json);
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
    /// Atomically replaces a JSON snapshot and reports whether owner-only persistence succeeded.
    /// </summary>
    internal static bool Write<T>(
        string path,
        T value,
        Func<T, string> serialize,
        string storeName) {
        ArgumentNullException.ThrowIfNull(serialize);

        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return false;
            }

            var json = serialize(value);
            ChatJsonFileStore.Write(
                path,
                json,
                hardenExistingDirectory: ChatStatePaths.IsPathInDefaultDirectory(path));
            return true;
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} write failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Deletes a snapshot without allowing cleanup failures to escape into chat execution.
    /// </summary>
    internal static void Delete(string path, string storeName) {
        try {
            ChatJsonFileStore.Delete(path);
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} clear failed: {ex.GetType().Name}: {ex.Message}");
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
