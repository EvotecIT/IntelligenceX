using System;
using System.Globalization;
using System.IO;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

public sealed class ChatGptUsageCacheEntry {
    public ChatGptUsageCacheEntry(ChatGptUsageSnapshot snapshot, DateTimeOffset updatedAt) {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        UpdatedAt = updatedAt;
    }

    public ChatGptUsageSnapshot Snapshot { get; }
    public DateTimeOffset UpdatedAt { get; }

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("updated_at", UpdatedAt.ToUnixTimeSeconds())
            .Add("usage", Snapshot.ToJson());
    }

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

public static class ChatGptUsageCache {
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

    public static bool TryLoad(out ChatGptUsageCacheEntry? entry, string? path = null) {
        entry = null;
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
    }

    public static void Save(ChatGptUsageSnapshot snapshot, string? path = null) {
        var resolved = path ?? ResolveCachePath();
        var dir = Path.GetDirectoryName(resolved);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        var entry = new ChatGptUsageCacheEntry(snapshot, DateTimeOffset.UtcNow);
        var json = JsonLite.Serialize(JsonValue.From(entry.ToJson()));
        File.WriteAllText(resolved, json);
    }
}
