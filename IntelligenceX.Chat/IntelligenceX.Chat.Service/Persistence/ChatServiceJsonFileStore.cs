using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        string defaultPath;
        try {
            defaultPath = ResolveDefaultPath(defaultFileName);
        } catch {
            return string.Empty;
        }

        try {
            var normalizedCandidate = (candidate ?? string.Empty).Trim();
            if (normalizedCandidate.Length == 0) {
                return defaultPath;
            }

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

                return ResolveDefaultPath(fileName);
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
        if (string.IsNullOrWhiteSpace(anchorPath)) {
            return string.Empty;
        }

        var normalizedCandidate = (fileName ?? string.Empty).Trim();
        var normalizedFileName = Path.GetFileName(normalizedCandidate);
        if (normalizedFileName.Length == 0
            || normalizedFileName is "." or ".."
            || !string.Equals(normalizedFileName, normalizedCandidate, StringComparison.Ordinal)) {
            throw new ArgumentException("A state file name is required.", nameof(fileName));
        }

        var directory = Path.GetDirectoryName(anchorPath);
        if (string.IsNullOrWhiteSpace(directory)) {
            return string.Empty;
        }

        var candidate = Path.GetFullPath(Path.Combine(directory, normalizedFileName));
        return ChatStatePaths.IsDirectChildPath(directory, candidate)
            ? candidate
            : string.Empty;
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
        if (string.IsNullOrWhiteSpace(path)) {
            return ChatServiceJsonFileReadResult<T>.Empty();
        }
        if (!IsSafeDirectFilePath(path)) {
            return ChatServiceJsonFileReadResult<T>.Invalid();
        }

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
            if (!IsSafeDirectFilePath(path)) {
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
    internal static bool Delete(string path, string storeName) {
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return false;
            }
            if (!IsSafeDirectFilePath(path)) {
                return false;
            }

            ChatJsonFileStore.Delete(path);
            return true;
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} clear failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Serializes a read-modify-write operation across service sessions and processes for one store path.
    /// </summary>
    internal static bool TryWithExclusiveAccess<TState, TResult>(
        string path,
        Func<TState, TResult> action,
        TState state,
        out TResult result,
        string storeName,
        Func<string, bool?>? acquisitionOverride = null) {
        ArgumentNullException.ThrowIfNull(action);
        result = default!;
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }

        var mutexName = BuildMutexName(path);
        if (acquisitionOverride is not null) {
            var overrideResult = acquisitionOverride(path);
            if (overrideResult.HasValue) {
                if (!overrideResult.Value) {
                    Trace.TraceWarning($"{NormalizeStoreName(storeName)} lock timeout for '{mutexName}'.");
                    return false;
                }

                try {
                    result = action(state);
                    return true;
                } catch (Exception ex) {
                    Trace.TraceWarning($"{NormalizeStoreName(storeName)} locked operation failed: {ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }
        }

        Mutex? mutex = null;
        var acquired = false;
        try {
            mutex = new Mutex(initiallyOwned: false, mutexName);
            try {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(15));
            } catch (AbandonedMutexException) {
                acquired = true;
            }

            if (!acquired) {
                Trace.TraceWarning($"{NormalizeStoreName(storeName)} lock timeout for '{mutexName}'.");
                return false;
            }

            result = action(state);
            return true;
        } catch (Exception ex) {
            Trace.TraceWarning($"{NormalizeStoreName(storeName)} lock failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        } finally {
            if (acquired && mutex is not null) {
                try {
                    mutex.ReleaseMutex();
                } catch {
                    // Preserve the operation result when lock cleanup fails.
                }
            }

            mutex?.Dispose();
        }
    }

    private static string BuildMutexName(string path) {
        var normalizedPath = path.Trim();
        try {
            normalizedPath = Path.GetFullPath(normalizedPath);
        } catch {
            // Hash the original path when full path resolution is unavailable.
        }

        if (OperatingSystem.IsWindows()) {
            normalizedPath = normalizedPath.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return $"IntelligenceX.Chat.JsonStore.{Convert.ToHexString(hash)}";
    }

    private static bool IsSafeDirectFilePath(string path) {
        try {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            return !string.IsNullOrWhiteSpace(directory)
                   && ChatStatePaths.IsDirectChildPath(directory, fullPath);
        } catch {
            return false;
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
