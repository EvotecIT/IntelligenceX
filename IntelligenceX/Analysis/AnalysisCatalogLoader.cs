using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        var overridesRoot = Path.Combine(workspace!, "Analysis", "Catalog", "overrides");
        var packsRoot = Path.Combine(workspace!, "Analysis", "Packs");
        return LoadFromPaths(rulesRoot, overridesRoot, packsRoot);
    }

    /// <summary>
    /// Loads catalog content from explicit rule and pack directories.
    /// </summary>
    public static AnalysisCatalog LoadFromPaths(string rulesRoot, string packsRoot) {
        var catalogRoot = Path.GetDirectoryName(Path.GetFullPath(rulesRoot)) ?? string.Empty;
        var overridesRoot = string.IsNullOrWhiteSpace(catalogRoot) ? string.Empty : Path.Combine(catalogRoot, "overrides");
        return LoadFromPaths(rulesRoot, overridesRoot, packsRoot);
    }

    /// <summary>
    /// Loads catalog content from explicit rule, override, and pack directories.
    /// </summary>
    public static AnalysisCatalog LoadFromPaths(string rulesRoot, string overridesRoot, string packsRoot) {
        var rules = new Dictionary<string, AnalysisRule>(StringComparer.OrdinalIgnoreCase);
        var packs = new Dictionary<string, AnalysisPack>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(rulesRoot)) {
            foreach (var rule in EnumerateJsonFiles(rulesRoot, SearchOption.AllDirectories)
                         .Select(TryLoadRule)
                         .Where(rule => rule is not null)) {
                var resolved = rule!;
                if (!rules.ContainsKey(resolved.Id)) {
                    rules[resolved.Id] = resolved;
                }
            }
        }

        if (Directory.Exists(overridesRoot) && rules.Count > 0) {
            foreach (var overrideEntry in EnumerateJsonFiles(overridesRoot, SearchOption.AllDirectories)
                         .Select(TryLoadRuleOverride)
                         .Where(entry => entry is not null)) {
                var resolved = overrideEntry!;
                if (!rules.TryGetValue(resolved.Id, out var existing)) {
                    continue;
                }
                rules[existing.Id] = ApplyOverride(existing, resolved);
            }
        }

        if (Directory.Exists(packsRoot)) {
            foreach (var pack in EnumerateJsonFiles(packsRoot, SearchOption.TopDirectoryOnly)
                         .Select(TryLoadPack)
                         .Where(pack => pack is not null)) {
                var resolved = pack!;
                if (!packs.ContainsKey(resolved.Id)) {
                    packs[resolved.Id] = resolved;
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
            var toolRuleId = obj.GetString("toolRuleId");
            var title = obj.GetString("title");
            var description = obj.GetString("description");
            var category = obj.GetString("category") ?? "General";
            var defaultSeverity = obj.GetString("defaultSeverity") ?? "warning";
            var type = InferRuleType(obj.GetString("type"), category);
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(language) ||
                string.IsNullOrWhiteSpace(tool) ||
                string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(description)) {
                return null;
            }
            var resolvedToolRuleId = string.IsNullOrWhiteSpace(toolRuleId) ? id! : toolRuleId!;
            var tags = AnalysisJsonHelpers.ReadStringList(obj, "tags") ?? Array.Empty<string>();
            var docs = obj.GetString("docs");
            return new AnalysisRule(id!, language!, tool!, resolvedToolRuleId, title!, description!, category,
                defaultSeverity, tags, docs, path, type);
        } catch {
            return null;
        }
    }

    private static string? InferRuleType(string? explicitType, string? category) {
        if (!string.IsNullOrWhiteSpace(explicitType)) {
            return explicitType!.Trim();
        }
        if (string.IsNullOrWhiteSpace(category)) {
            return null;
        }
        var normalized = category.Trim();
        if (normalized.Equals("Security", StringComparison.OrdinalIgnoreCase)) {
            return "vulnerability";
        }
        if (normalized.Equals("Reliability", StringComparison.OrdinalIgnoreCase)) {
            return "bug";
        }
        if (normalized.Equals("Maintainability", StringComparison.OrdinalIgnoreCase)) {
            return "code-smell";
        }
        if (normalized.Equals("Design", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Performance", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Style", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Usage", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Naming", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Documentation", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("BestPractices", StringComparison.OrdinalIgnoreCase)) {
            return "code-smell";
        }
        return null;
    }

    private static AnalysisRuleOverride? TryLoadRuleOverride(string path) {
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
            if (string.IsNullOrWhiteSpace(id)) {
                return null;
            }
            var tags = AnalysisJsonHelpers.ReadStringList(obj, "tags");
            var type = obj.GetString("type");
            var category = obj.GetString("category");
            var defaultSeverity = obj.GetString("defaultSeverity");
            var title = obj.GetString("title");
            var description = obj.GetString("description");
            var docs = obj.GetString("docs");

            return new AnalysisRuleOverride(
                id.Trim(),
                tags,
                type,
                category,
                defaultSeverity,
                title,
                description,
                docs,
                path);
        } catch {
            return null;
        }
    }

    private static AnalysisRule ApplyOverride(AnalysisRule existing, AnalysisRuleOverride ruleOverride) {
        var mergedTags = MergeTags(existing.Tags, ruleOverride.Tags);
        return new AnalysisRule(
            existing.Id,
            existing.Language,
            existing.Tool,
            existing.ToolRuleId,
            string.IsNullOrWhiteSpace(ruleOverride.Title) ? existing.Title : ruleOverride.Title!,
            string.IsNullOrWhiteSpace(ruleOverride.Description) ? existing.Description : ruleOverride.Description!,
            string.IsNullOrWhiteSpace(ruleOverride.Category) ? existing.Category : ruleOverride.Category!,
            string.IsNullOrWhiteSpace(ruleOverride.DefaultSeverity) ? existing.DefaultSeverity : ruleOverride.DefaultSeverity!,
            mergedTags,
            string.IsNullOrWhiteSpace(ruleOverride.Docs) ? existing.Docs : ruleOverride.Docs,
            existing.SourcePath,
            string.IsNullOrWhiteSpace(ruleOverride.Type) ? existing.Type : ruleOverride.Type);
    }

    private static IReadOnlyList<string> MergeTags(IReadOnlyList<string> existing, IReadOnlyList<string>? overrides) {
        if (overrides is null || overrides.Count == 0) {
            return existing ?? Array.Empty<string>();
        }
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<string>();
        foreach (var tag in existing ?? Array.Empty<string>()) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }
            var value = tag.Trim();
            if (set.Add(value)) {
                merged.Add(value);
            }
        }
        foreach (var tag in overrides) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }
            var value = tag.Trim();
            if (set.Add(value)) {
                merged.Add(value);
            }
        }
        return merged;
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
            var rules = AnalysisJsonHelpers.ReadStringList(obj, "rules") ?? Array.Empty<string>();
            var includes = AnalysisJsonHelpers.ReadStringList(obj, "includes") ?? Array.Empty<string>();
            var overrides = AnalysisJsonHelpers.ReadStringMap(obj, "severityOverrides") ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new AnalysisPack(id!, label!, description, rules, overrides, path, includes);
        } catch {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateJsonFiles(string root, SearchOption searchOption) {
        var rootFull = Path.GetFullPath(root);
        foreach (var file in Directory.EnumerateFiles(root, "*.json", searchOption)) {
            var full = Path.GetFullPath(file);
            if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            yield return full;
        }
    }

    private sealed class AnalysisRuleOverride {
        public AnalysisRuleOverride(string id, IReadOnlyList<string>? tags, string? type, string? category,
            string? defaultSeverity, string? title, string? description, string? docs, string path) {
            Id = id;
            Tags = tags;
            Type = type;
            Category = category;
            DefaultSeverity = defaultSeverity;
            Title = title;
            Description = description;
            Docs = docs;
            Path = path;
        }

        public string Id { get; }
        public IReadOnlyList<string>? Tags { get; }
        public string? Type { get; }
        public string? Category { get; }
        public string? DefaultSeverity { get; }
        public string? Title { get; }
        public string? Description { get; }
        public string? Docs { get; }
        public string Path { get; }
    }
}
