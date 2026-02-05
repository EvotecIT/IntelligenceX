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
            foreach (var rule in EnumerateJsonFiles(rulesRoot, SearchOption.AllDirectories)
                         .Select(TryLoadRule)
                         .Where(rule => rule is not null)) {
                var resolved = rule!;
                if (!rules.ContainsKey(resolved.Id)) {
                    rules[resolved.Id] = resolved;
                }
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
            var rules = AnalysisJsonHelpers.ReadStringList(obj, "rules") ?? Array.Empty<string>();
            var overrides = AnalysisJsonHelpers.ReadStringMap(obj, "severityOverrides") ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return new AnalysisPack(id!, label!, description, rules, overrides, path);
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
}
