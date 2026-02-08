using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using IntelligenceX.Json;

namespace IntelligenceX.Reviewer;

internal sealed record HotspotStateEntry(string Key, string Status, string? Note, string? CreatedAt);

internal static class HotspotStateStore {
    internal const string SchemaValue = "intelligencex.hotspots.v1";

    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    internal sealed record HotspotStateFile(IReadOnlyList<HotspotStateEntry> Items, bool Loaded);

    internal static HotspotStateFile TryLoad(string path) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            return new HotspotStateFile(Array.Empty<HotspotStateEntry>(), Loaded: false);
        }
        try {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) {
                return new HotspotStateFile(Array.Empty<HotspotStateEntry>(), Loaded: false);
            }
            var parsed = JsonLite.Parse(text);
            var root = parsed?.AsObject();
            if (root is null) {
                return new HotspotStateFile(Array.Empty<HotspotStateEntry>(), Loaded: false);
            }

            var items = root.GetArray("items");
            if (items is null || items.Count == 0) {
                return new HotspotStateFile(Array.Empty<HotspotStateEntry>(), Loaded: true);
            }

            var list = new List<HotspotStateEntry>();
            foreach (var obj in items.Select(v => v.AsObject()).Where(o => o is not null)) {
                var key = obj!.GetString("key");
                var status = obj.GetString("status");
                if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(status)) {
                    continue;
                }
                var note = obj.GetString("note");
                var createdAt = obj.GetString("createdAt");
                list.Add(new HotspotStateEntry(key.Trim(), status.Trim(), note, createdAt));
            }

            return new HotspotStateFile(list, Loaded: true);
        } catch {
            return new HotspotStateFile(Array.Empty<HotspotStateEntry>(), Loaded: false);
        }
    }

    internal static IReadOnlyDictionary<string, HotspotStateEntry> ToMap(IReadOnlyList<HotspotStateEntry> items) {
        var map = new Dictionary<string, HotspotStateEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? Array.Empty<HotspotStateEntry>()) {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Status)) {
                continue;
            }
            map[item.Key.Trim()] = item;
        }
        return map;
    }

    internal static IReadOnlyList<HotspotStateEntry> MergeMissing(
        IReadOnlyList<HotspotStateEntry> existing,
        IReadOnlyList<string> missingKeys,
        string defaultStatus,
        string? createdAt = null) {
        var list = new List<HotspotStateEntry>();
        list.AddRange(existing ?? Array.Empty<HotspotStateEntry>());

        if (missingKeys is null || missingKeys.Count == 0) {
            return NormalizeAndSort(list);
        }

        var existingMap = ToMap(list);
        var effectiveCreatedAt = string.IsNullOrWhiteSpace(createdAt)
            ? DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : createdAt!.Trim();

        foreach (var rawKey in missingKeys) {
            if (string.IsNullOrWhiteSpace(rawKey)) {
                continue;
            }
            var key = rawKey.Trim();
            if (existingMap.ContainsKey(key)) {
                continue;
            }
            list.Add(new HotspotStateEntry(key, defaultStatus, Note: null, CreatedAt: effectiveCreatedAt));
        }

        return NormalizeAndSort(list);
    }

    internal static IReadOnlyList<HotspotStateEntry> PruneMissing(
        IReadOnlyList<HotspotStateEntry> existing,
        IReadOnlySet<string> presentKeys,
        Func<HotspotStateEntry, bool> shouldPrune) {
        if (existing is null || existing.Count == 0) {
            return Array.Empty<HotspotStateEntry>();
        }
        var list = new List<HotspotStateEntry>();
        foreach (var item in existing) {
            if (item is null || string.IsNullOrWhiteSpace(item.Key)) {
                continue;
            }
            var key = item.Key.Trim();
            if (presentKeys is not null && presentKeys.Contains(key)) {
                list.Add(item);
                continue;
            }
            if (shouldPrune is not null && shouldPrune(item)) {
                continue;
            }
            list.Add(item);
        }
        return NormalizeAndSort(list);
    }

    internal static void Save(string path, IReadOnlyList<HotspotStateEntry> items) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var normalized = NormalizeAndSort(items ?? Array.Empty<HotspotStateEntry>());
        var payload = new Dictionary<string, object?> {
            ["schema"] = SchemaValue,
            ["items"] = normalized.Select(item => new Dictionary<string, object?> {
                ["key"] = item.Key,
                ["status"] = item.Status,
                ["note"] = item.Note,
                ["createdAt"] = item.CreatedAt
            }).ToList()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, JsonOptions) + "\n");
    }

    internal static string BuildSuggestedStateSnippet(IReadOnlyList<string> missingKeys, string defaultStatus) {
        var createdAt = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var items = (missingKeys ?? Array.Empty<string>())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Select(key => new HotspotStateEntry(key.Trim(), defaultStatus, Note: null, CreatedAt: createdAt))
            .ToList();

        var payload = new Dictionary<string, object?> {
            ["schema"] = SchemaValue,
            ["items"] = items.Select(item => new Dictionary<string, object?> {
                ["key"] = item.Key,
                ["status"] = item.Status,
                ["note"] = item.Note,
                ["createdAt"] = item.CreatedAt
            }).ToList()
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static IReadOnlyList<HotspotStateEntry> NormalizeAndSort(IReadOnlyList<HotspotStateEntry> items) {
        var map = new Dictionary<string, HotspotStateEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? Array.Empty<HotspotStateEntry>()) {
            if (item is null || string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.Status)) {
                continue;
            }
            map[item.Key.Trim()] = item;
        }
        return map.Values
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
    }
}
