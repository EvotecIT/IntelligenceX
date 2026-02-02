using System;
using System.Globalization;
using System.IO;
#if NET6_0_OR_GREATER
using System.Security.AccessControl;
using System.Security.Principal;
#endif
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>
/// Represents a cached usage snapshot entry.
/// </summary>
public sealed class ChatGptUsageCacheEntry {
    /// <summary>
    /// Initializes a new cache entry.
    /// </summary>
    /// <param name="snapshot">Usage snapshot.</param>
    /// <param name="updatedAt">Timestamp when the snapshot was stored.</param>
    public ChatGptUsageCacheEntry(ChatGptUsageSnapshot snapshot, DateTimeOffset updatedAt) {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Gets the cached usage snapshot.
    /// </summary>
    public ChatGptUsageSnapshot Snapshot { get; }
    /// <summary>
    /// Gets the timestamp when the snapshot was updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Serializes the cache entry to JSON.
    /// </summary>
    public JsonObject ToJson() {
        return new JsonObject()
            .Add("updated_at", UpdatedAt.ToUnixTimeSeconds())
            .Add("usage", Snapshot.ToJson());
    }

    /// <summary>
    /// Parses a cache entry from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed cache entry or null.</returns>
    public static ChatGptUsageCacheEntry? FromJson(JsonObject obj) {
        if (obj is null) {
            return null;
        }
        var usageObj = obj.GetObject("usage");
        if (usageObj is null) {
            return null;
        }
        var snapshot = ChatGptUsageSnapshot.FromJson(usageObj);
        var updatedAt = ParseUpdatedAt(obj);
        return new ChatGptUsageCacheEntry(snapshot, updatedAt);
    }

    private static DateTimeOffset ParseUpdatedAt(JsonObject obj) {
        var unix = obj.GetInt64("updated_at");
        if (unix.HasValue) {
            return DateTimeOffset.FromUnixTimeSeconds(unix.Value);
        }
        var raw = obj.GetString("updated_at") ?? obj.GetString("updatedAt");
        if (!string.IsNullOrWhiteSpace(raw) &&
            DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            return parsed.ToUniversalTime();
        }
        return DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Helpers for reading and writing the local usage cache.
/// </summary>
public static class ChatGptUsageCache {
    /// <summary>
    /// Resolves the default cache path, honoring INTELLIGENCEX_USAGE_PATH when present.
    /// </summary>
    public static string ResolveCachePath() {
        var overridePath = Environment.GetEnvironmentVariable("INTELLIGENCEX_USAGE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) {
            return overridePath!;
        }
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }
        return Path.Combine(home, ".intelligencex", "usage.json");
    }

    /// <summary>
    /// Attempts to load a cached usage snapshot.
    /// </summary>
    /// <param name="entry">The cache entry when found.</param>
    /// <param name="path">Optional override path.</param>
    /// <returns><c>true</c> when a cache entry was loaded.</returns>
    public static bool TryLoad(out ChatGptUsageCacheEntry? entry, string? path = null) {
        entry = null;
        try {
            var resolved = path ?? ResolveCachePath();
            if (!File.Exists(resolved)) {
                return false;
            }
            var content = File.ReadAllText(resolved);
            if (string.IsNullOrWhiteSpace(content)) {
                return false;
            }
            var obj = JsonLite.Parse(content)?.AsObject();
            if (obj is null) {
                return false;
            }
            entry = ChatGptUsageCacheEntry.FromJson(obj);
            return entry is not null;
        } catch {
            entry = null;
            return false;
        }
    }

    /// <summary>
    /// Saves a usage snapshot to the cache.
    /// </summary>
    /// <param name="snapshot">Snapshot to persist.</param>
    /// <param name="path">Optional override path.</param>
    public static void Save(ChatGptUsageSnapshot snapshot, string? path = null) {
        var resolved = path ?? ResolveCachePath();
        var dir = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        var entry = new ChatGptUsageCacheEntry(snapshot, DateTimeOffset.UtcNow);
        var json = JsonLite.Serialize(JsonValue.From(entry.ToJson()));
        var tempPath = resolved + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tempPath, json);
        TrySetOwnerOnlyPermissions(tempPath);
        try {
            ReplaceFile(tempPath, resolved);
        } finally {
            try {
                if (File.Exists(tempPath)) {
                    File.Delete(tempPath);
                }
            } catch {
                // Best-effort cleanup.
            }
        }
        TrySetOwnerOnlyPermissions(resolved);
    }

    private static void ReplaceFile(string source, string destination) {
#if NET6_0_OR_GREATER
        File.Move(source, destination, true);
#else
        if (File.Exists(destination)) {
            File.Delete(destination);
        }
        File.Move(source, destination);
#endif
    }

    private static void TrySetOwnerOnlyPermissions(string path) {
#if NET6_0_OR_GREATER
        try {
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            } else if (OperatingSystem.IsWindows()) {
                var identity = WindowsIdentity.GetCurrent();
                var user = identity.User;
                if (user is null) {
                    return;
                }
                var fileInfo = new FileInfo(path);
                var security = fileInfo.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                security.SetOwner(user);
                security.SetAccessRule(new FileSystemAccessRule(user,
                    FileSystemRights.Read | FileSystemRights.Write,
                    AccessControlType.Allow));
                fileInfo.SetAccessControl(security);
            }
        } catch {
            // Best-effort permissions.
        }
#endif
    }
}
