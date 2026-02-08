using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Analysis;

/// <summary>
/// Validates analysis catalog rules and packs for structural integrity.
/// </summary>
public static class AnalysisCatalogValidator {
    private static readonly HashSet<string> SupportedSeverities = new(StringComparer.OrdinalIgnoreCase) {
        "critical", "error", "high",
        "warning", "warn", "medium",
        "info", "information", "low", "suggestion",
        "none"
    };
    private static readonly HashSet<string> SupportedRuleTypes = new(StringComparer.OrdinalIgnoreCase) {
        "bug",
        "vulnerability",
        "code-smell",
        "security-hotspot"
    };

    /// <summary>
    /// Validates catalog content under the workspace.
    /// </summary>
    public static AnalysisCatalogValidationResult ValidateWorkspace(string? workspace) {
        var resolvedWorkspace = string.IsNullOrWhiteSpace(workspace)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(workspace);
        var analysisRoot = Path.Combine(resolvedWorkspace, "Analysis");
        var rulesRoot = Path.Combine(Path.Combine(analysisRoot, "Catalog"), "rules");
        var overridesRoot = Path.Combine(Path.Combine(analysisRoot, "Catalog"), "overrides");
        var packsRoot = Path.Combine(analysisRoot, "Packs");
        return ValidatePaths(rulesRoot, overridesRoot, packsRoot);
    }

    /// <summary>
    /// Validates catalog content under explicit rules and packs roots.
    /// </summary>
    public static AnalysisCatalogValidationResult ValidatePaths(string rulesRoot, string packsRoot) {
        var catalogRoot = Path.GetDirectoryName(Path.GetFullPath(rulesRoot)) ?? string.Empty;
        var overridesRoot = string.IsNullOrWhiteSpace(catalogRoot) ? string.Empty : Path.Combine(catalogRoot, "overrides");
        return ValidatePaths(rulesRoot, overridesRoot, packsRoot);
    }

    /// <summary>
    /// Validates catalog content under explicit rules, overrides, and packs roots.
    /// </summary>
    public static AnalysisCatalogValidationResult ValidatePaths(string rulesRoot, string overridesRoot, string packsRoot) {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!Directory.Exists(rulesRoot)) {
            errors.Add($"Rules directory not found: {rulesRoot}");
        }
        if (!Directory.Exists(packsRoot)) {
            errors.Add($"Packs directory not found: {packsRoot}");
        }
        if (errors.Count > 0) {
            return new AnalysisCatalogValidationResult(errors, warnings).Normalize();
        }

        var ruleEntries = LoadRuleEntries(rulesRoot, errors);
        var overrideEntries = LoadOverrideEntries(overridesRoot, errors);
        var packEntries = LoadPackEntries(packsRoot, errors);
        if (errors.Count > 0) {
            return new AnalysisCatalogValidationResult(errors, warnings).Normalize();
        }

        var rulesById = BuildDistinctRules(ruleEntries, errors);
        var packsById = BuildDistinctPacks(packEntries, errors);

        ValidateOverridesReferenceExistingRules(overrideEntries, rulesById, errors);
        ValidatePackRuleReferences(packsById, rulesById, errors);
        ValidatePackIncludesExist(packsById, errors);
        ValidatePackOverrideSeverities(packsById, errors);
        ValidatePackIncludeCycles(packsById, errors);

        return new AnalysisCatalogValidationResult(errors, warnings).Normalize();
    }

    private static IReadOnlyList<RuleEntry> LoadRuleEntries(string rulesRoot, ICollection<string> errors) {
        var entries = new List<RuleEntry>();
        foreach (var path in EnumerateJsonFiles(rulesRoot, SearchOption.AllDirectories)) {
            JsonObject? obj;
            try {
                var parsed = JsonLite.Parse(File.ReadAllText(path));
                obj = parsed?.AsObject();
            } catch (Exception ex) when (!IsFatalException(ex)) {
                errors.Add($"Invalid rule JSON '{path}': {ex.Message}");
                continue;
            }

            if (obj is null) {
                errors.Add($"Invalid rule object in '{path}'.");
                continue;
            }

            var id = obj.GetString("id");
            if (string.IsNullOrWhiteSpace(id)) {
                errors.Add($"Rule file missing non-empty id: '{path}'.");
                continue;
            }

            var normalizedId = (id ?? string.Empty).Trim();
            var hasRequiredFields = true;
            hasRequiredFields &= ValidateRequiredRuleField(obj, normalizedId, path, "language", errors);
            hasRequiredFields &= ValidateRequiredRuleField(obj, normalizedId, path, "tool", errors);
            hasRequiredFields &= ValidateRequiredRuleField(obj, normalizedId, path, "title", errors);
            hasRequiredFields &= ValidateRequiredRuleField(obj, normalizedId, path, "description", errors);
            if (!hasRequiredFields) {
                continue;
            }
            ValidateRuleType(obj, normalizedId, path, errors);

            entries.Add(new RuleEntry(normalizedId, path));
        }
        return entries;
    }

    private static IReadOnlyList<OverrideEntry> LoadOverrideEntries(string overridesRoot, ICollection<string> errors) {
        var entries = new List<OverrideEntry>();
        if (string.IsNullOrWhiteSpace(overridesRoot) || !Directory.Exists(overridesRoot)) {
            return entries;
        }

        foreach (var path in EnumerateJsonFiles(overridesRoot, SearchOption.AllDirectories)) {
            JsonObject? obj;
            try {
                var parsed = JsonLite.Parse(File.ReadAllText(path));
                obj = parsed?.AsObject();
            } catch (Exception ex) when (!IsFatalException(ex)) {
                errors.Add($"Invalid rule override JSON '{path}': {ex.Message}");
                continue;
            }

            if (obj is null) {
                errors.Add($"Invalid rule override object in '{path}'.");
                continue;
            }

            var id = obj.GetString("id");
            if (string.IsNullOrWhiteSpace(id)) {
                errors.Add($"Rule override file missing non-empty id: '{path}'.");
                continue;
            }

            var normalizedId = (id ?? string.Empty).Trim();
            ValidateRuleType(obj, normalizedId, path, errors);
            entries.Add(new OverrideEntry(normalizedId, path));
        }

        return entries;
    }

    private static void ValidateOverridesReferenceExistingRules(IReadOnlyList<OverrideEntry> overrides,
        IReadOnlyDictionary<string, RuleEntry> rulesById, ICollection<string> errors) {
        if (overrides is null || overrides.Count == 0) {
            return;
        }
        foreach (var entry in overrides) {
            if (!rulesById.ContainsKey(entry.Id)) {
                errors.Add($"Rule override '{entry.Id}' does not match any base rule ({entry.Path}).");
            }
        }
    }

    private static IReadOnlyList<PackEntry> LoadPackEntries(string packsRoot, ICollection<string> errors) {
        var entries = new List<PackEntry>();
        foreach (var path in EnumerateJsonFiles(packsRoot, SearchOption.TopDirectoryOnly)) {
            JsonObject? obj;
            try {
                var parsed = JsonLite.Parse(File.ReadAllText(path));
                obj = parsed?.AsObject();
            } catch (Exception ex) when (!IsFatalException(ex)) {
                errors.Add($"Invalid pack JSON '{path}': {ex.Message}");
                continue;
            }

            if (obj is null) {
                errors.Add($"Invalid pack object in '{path}'.");
                continue;
            }

            var id = obj.GetString("id");
            if (string.IsNullOrWhiteSpace(id)) {
                errors.Add($"Pack file missing non-empty id: '{path}'.");
                continue;
            }

            var rules = AnalysisJsonHelpers.ReadStringList(obj, "rules") ?? Array.Empty<string>();
            var includes = AnalysisJsonHelpers.ReadStringList(obj, "includes") ?? Array.Empty<string>();
            var overrides = AnalysisJsonHelpers.ReadStringMap(obj, "severityOverrides") ??
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var overrideEntry in overrides) {
                if (string.IsNullOrWhiteSpace(overrideEntry.Key)) {
                    continue;
                }
                normalizedOverrides[overrideEntry.Key] = overrideEntry.Value;
            }

            entries.Add(new PackEntry(
                (id ?? string.Empty).Trim(),
                path,
                rules.Where(rule => !string.IsNullOrWhiteSpace(rule)).Select(rule => rule.Trim()).ToList(),
                includes.Where(include => !string.IsNullOrWhiteSpace(include)).Select(include => include.Trim()).ToList(),
                normalizedOverrides));
        }
        return entries;
    }

    private static IReadOnlyDictionary<string, RuleEntry> BuildDistinctRules(IReadOnlyList<RuleEntry> entries,
        ICollection<string> errors) {
        var duplicates = entries
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var duplicate in duplicates) {
            errors.Add(
                $"Duplicate rule id '{duplicate.Key}' in: {string.Join(", ", duplicate.Select(item => item.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))}");
        }

        var map = new Dictionary<string, RuleEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (!map.ContainsKey(entry.Id)) {
                map[entry.Id] = entry;
            }
        }
        return map;
    }

    private static IReadOnlyDictionary<string, PackEntry> BuildDistinctPacks(IReadOnlyList<PackEntry> entries,
        ICollection<string> errors) {
        var duplicates = entries
            .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .ToList();
        foreach (var duplicate in duplicates) {
            errors.Add(
                $"Duplicate pack id '{duplicate.Key}' in: {string.Join(", ", duplicate.Select(item => item.Path).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))}");
        }

        var map = new Dictionary<string, PackEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries) {
            if (!map.ContainsKey(entry.Id)) {
                map[entry.Id] = entry;
            }
        }
        return map;
    }

    private static void ValidatePackRuleReferences(IReadOnlyDictionary<string, PackEntry> packsById,
        IReadOnlyDictionary<string, RuleEntry> rulesById, ICollection<string> errors) {
        foreach (var pack in packsById.Values) {
            foreach (var ruleId in pack.Rules) {
                if (string.IsNullOrWhiteSpace(ruleId)) {
                    continue;
                }
                if (!rulesById.ContainsKey(ruleId)) {
                    errors.Add($"Pack '{pack.Id}' references unknown rule '{ruleId}' ({pack.Path}).");
                }
            }
        }
    }

    private static void ValidatePackIncludesExist(IReadOnlyDictionary<string, PackEntry> packsById, ICollection<string> errors) {
        foreach (var pack in packsById.Values) {
            foreach (var includeId in pack.Includes) {
                if (string.IsNullOrWhiteSpace(includeId)) {
                    continue;
                }
                if (!packsById.ContainsKey(includeId)) {
                    errors.Add($"Pack '{pack.Id}' includes unknown pack '{includeId}' ({pack.Path}).");
                }
            }
        }
    }

    private static void ValidatePackOverrideSeverities(IReadOnlyDictionary<string, PackEntry> packsById,
        ICollection<string> errors) {
        foreach (var pack in packsById.Values) {
            foreach (var overrideEntry in pack.SeverityOverrides) {
                if (string.IsNullOrWhiteSpace(overrideEntry.Value)) {
                    errors.Add($"Pack '{pack.Id}' has empty severity override for '{overrideEntry.Key}' ({pack.Path}).");
                    continue;
                }
                if (SupportedSeverities.Contains(overrideEntry.Value.Trim())) {
                    continue;
                }
                errors.Add(
                    $"Pack '{pack.Id}' has unsupported severity '{overrideEntry.Value}' for '{overrideEntry.Key}' ({pack.Path}).");
            }
        }
    }

    private static void ValidatePackIncludeCycles(IReadOnlyDictionary<string, PackEntry> packsById, ICollection<string> errors) {
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();
        var cycleMessages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var packId in packsById.Keys.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)) {
            if (state.TryGetValue(packId, out var existing) && existing == 2) {
                continue;
            }
            Visit(packId, packsById, state, stack, cycleMessages);
        }
        foreach (var cycle in cycleMessages.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)) {
            errors.Add(cycle);
        }
    }

    private static void Visit(string packId, IReadOnlyDictionary<string, PackEntry> packsById,
        IDictionary<string, int> state, IList<string> stack, ISet<string> cycleMessages) {
        if (state.TryGetValue(packId, out var currentState)) {
            if (currentState == 1) {
                var cycleIndex = IndexOf(stack, packId);
                if (cycleIndex >= 0) {
                    var cycle = new List<string>();
                    for (var i = cycleIndex; i < stack.Count; i++) {
                        cycle.Add(stack[i]);
                    }
                    cycle.Add(packId);
                    cycleMessages.Add($"Pack include cycle detected: {string.Join(" -> ", cycle)}");
                }
            }
            return;
        }

        state[packId] = 1;
        stack.Add(packId);
        if (packsById.TryGetValue(packId, out var pack)) {
            foreach (var includeId in pack.Includes) {
                if (string.IsNullOrWhiteSpace(includeId) || !packsById.ContainsKey(includeId)) {
                    continue;
                }
                Visit(includeId, packsById, state, stack, cycleMessages);
            }
        }
        stack.RemoveAt(stack.Count - 1);
        state[packId] = 2;
    }

    private static int IndexOf(IList<string> values, string value) {
        if (values is null || values.Count == 0 || string.IsNullOrWhiteSpace(value)) {
            return -1;
        }
        for (var i = 0; i < values.Count; i++) {
            if (value.Equals(values[i], StringComparison.OrdinalIgnoreCase)) {
                return i;
            }
        }
        return -1;
    }

    private static bool ValidateRequiredRuleField(JsonObject obj, string ruleId, string path, string fieldName,
        ICollection<string> errors) {
        if (!string.IsNullOrWhiteSpace(obj.GetString(fieldName))) {
            return true;
        }
        errors.Add($"Rule '{ruleId}' missing required field '{fieldName}' ({path}).");
        return false;
    }

    private static void ValidateRuleType(JsonObject obj, string ruleId, string path, ICollection<string> errors) {
        if (obj is null) {
            return;
        }
        var type = obj.GetString("type");
        if (string.IsNullOrWhiteSpace(type)) {
            return;
        }
        // string.IsNullOrWhiteSpace is not annotated for netstandard2.0 flow analysis; force non-null.
        if (SupportedRuleTypes.Contains(type!.Trim())) {
            return;
        }
        errors.Add($"Rule '{ruleId}' has unsupported type '{type}' ({path}).");
    }

    private static bool IsFatalException(Exception ex) {
        return ex is OutOfMemoryException
               || ex is StackOverflowException
               || ex is AccessViolationException;
    }

    private static IEnumerable<string> EnumerateJsonFiles(string root, SearchOption searchOption) {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
            yield break;
        }
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootPrefix = rootFull + Path.DirectorySeparatorChar;
        foreach (var full in Directory.EnumerateFiles(root, "*.json", searchOption).Select(Path.GetFullPath)) {
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            yield return full;
        }
    }

    private sealed class RuleEntry {
        public RuleEntry(string id, string path) {
            Id = id;
            Path = path;
        }

        public string Id { get; }
        public string Path { get; }
    }

    private sealed class OverrideEntry {
        public OverrideEntry(string id, string path) {
            Id = id;
            Path = path;
        }

        public string Id { get; }
        public string Path { get; }
    }

    private sealed class PackEntry {
        public PackEntry(string id, string path, IReadOnlyList<string> rules, IReadOnlyList<string> includes,
            IReadOnlyDictionary<string, string> severityOverrides) {
            Id = id;
            Path = path;
            Rules = rules;
            Includes = includes;
            SeverityOverrides = severityOverrides;
        }

        public string Id { get; }
        public string Path { get; }
        public IReadOnlyList<string> Rules { get; }
        public IReadOnlyList<string> Includes { get; }
        public IReadOnlyDictionary<string, string> SeverityOverrides { get; }
    }
}
