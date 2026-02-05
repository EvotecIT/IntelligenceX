using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Json;

namespace IntelligenceX.Analysis;

/// <summary>
/// Loads analysis rule and pack definitions from disk.
/// </summary>
public static class AnalysisCatalogLoader {
    /// <summary>
    /// Loads catalog content from the workspace (defaults to current directory).
    /// </summary>
    public static AnalysisCatalog LoadFromWorkspace(string? workspace) {
        if (string.IsNullOrWhiteSpace(workspace)) {
            workspace = Environment.CurrentDirectory;
        }
        var rulesRoot = Path.Combine(workspace!, "Analysis", "Catalog", "rules");
        var packsRoot = Path.Combine(workspace!, "Analysis", "Packs");
        return LoadFromPaths(rulesRoot, packsRoot);
    }

    /// <summary>
    /// Loads catalog content from explicit rule and pack directories.
    /// </summary>
    public static AnalysisCatalog LoadFromPaths(string rulesRoot, string packsRoot) {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase);
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(rulesRoot)) {
            foreach (var file in Directory.EnumerateFiles(rulesRoot, "*.json", SearchOption.AllDirectories)) {
                var rule = TryLoadRule(file);
                if (rule is null) {
                    continue;
                }
                if (!rules.ContainsKey(rule.Id)) {
                    rules[rule.Id] = rule;
                }
            }
        }

        if (Directory.Exists(packsRoot)) {
            foreach (var file in Directory.EnumerateFiles(packsRoot, "*.json", SearchOption.TopDirectoryOnly)) {
                var pack = TryLoadPack(file);
                if (pack is null) {
                    continue;
                }
                if (!packs.ContainsKey(pack.Id)) {
                    packs[pack.Id] = pack;
                }
            }
        }

        return new AnalysisCatalog(rules, packs);
    }

    private static AnalysisRule? TryLoadRule(string path) {
        try {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            var value = JsonLite.Parse(text);
            var obj = value?.AsObject();
            if (obj is null) {
                return null;
            }
            var id = obj.GetString("id");
            var language = obj.GetString("language");
            var tool = obj.GetString("tool");
            var toolRuleId = obj.GetString("toolRuleId") ?? id;
            var title = obj.GetString("title");
            var description = obj.GetString("description");
            var category = obj.GetString("category") ?? "General";
            var defaultSeverity = obj.GetString("defaultSeverity") ?? "warning";
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(language) ||
                string.IsNullOrWhiteSpace(tool) ||
                string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(description)) {
                return null;
            }
            var tags = ReadStringList(obj, "tags") ?? Array.Empty<string>();
            var docs = obj.GetString("docs");
            return new AnalysisRule(id!, language!, tool!, toolRuleId ?? id!, title!, description!, category,
                defaultSeverity, tags, docs, path);
        } catch {
            return null;
        }
    }

    private static AnalysisPack? TryLoadPack(string path) {
        try {
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) {
                return null;
            }
            var value = JsonLite.Parse(text);
            var obj = value?.AsObject();
            if (obj is null) {
                return null;
            }
            var id = obj.GetString("id");
            var label = obj.GetString("label") ?? id;
            var description = obj.GetString("description");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) {
                return null;
            }
            var rules = ReadStringList(obj, "rules") ?? Array.Empty<string>();
            var overrides = ReadStringMap(obj, "severityOverrides") ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new AnalysisPack(id!, label!, description, rules, overrides, path);
        } catch {
            return null;
        }
    }

    private static IReadOnlyList<string>? ReadStringList(JsonObject obj, string key) {
        if (obj.TryGetValue(key, out var value)) {
            var array = value?.AsArray();
            if (array is not null) {
                var list = new List<string>();
                foreach (var item in array) {
                    var text = item.AsString();
                    if (!string.IsNullOrWhiteSpace(text)) {
                        list.Add(text);
                    }
                }
                return list;
            }
            var textValue = value?.AsString();
            if (!string.IsNullOrWhiteSpace(textValue)) {
                return textValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }
        return null;
    }

    private static IReadOnlyDictionary<string, string>? ReadStringMap(JsonObject obj, string key) {
        if (!obj.TryGetValue(key, out var value)) {
            return null;
        }
        var mapObj = value?.AsObject();
        if (mapObj is null || mapObj.Count == 0) {
            return null;
        }
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in mapObj) {
            if (string.IsNullOrWhiteSpace(entry.Key)) {
                continue;
            }
            var text = entry.Value?.AsString();
            if (text is null) {
                continue;
            }
            result[entry.Key] = text;
        }
        return result;
    }
}
