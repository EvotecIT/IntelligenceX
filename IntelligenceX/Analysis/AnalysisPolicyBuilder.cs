using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Analysis;

/// <summary>
/// Resolves configured packs, disabled rules, and severity overrides into a concrete policy.
/// </summary>
public static class AnalysisPolicyBuilder {
    /// <summary>
    /// Builds an analysis policy from settings and catalog metadata.
    /// </summary>
    public static AnalysisPolicy Build(AnalysisSettings settings, AnalysisCatalog catalog) {
        if (settings is null) {
            throw new ArgumentNullException(nameof(settings));
        }
        if (catalog is null) {
            throw new ArgumentNullException(nameof(catalog));
        }

        var warnings = new List<string>();
        var selected = new Dictionary<string, AnalysisPolicyRule>(StringComparer.OrdinalIgnoreCase);
        var disabled = new HashSet<string>(
            (settings.DisabledRules ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var packId in settings.Packs ?? Array.Empty<string>()) {
            if (!catalog.TryGetPack(packId, out var pack)) {
                warnings.Add($"Pack not found: {packId}");
                continue;
            }

            foreach (var configuredRuleId in pack.Rules ?? Array.Empty<string>()) {
                if (string.IsNullOrWhiteSpace(configuredRuleId)) {
                    continue;
                }
                if (!catalog.TryGetRule(configuredRuleId, out var rule)) {
                    warnings.Add($"Rule not found: {configuredRuleId}");
                    continue;
                }
                if (IsRuleDisabled(disabled, rule)) {
                    continue;
                }

                var severity = rule.DefaultSeverity;
                if (TryResolveOverride(pack.SeverityOverrides, rule, out var packSeverity)) {
                    severity = packSeverity;
                }

                selected[rule.Id] = new AnalysisPolicyRule(rule, severity);
            }
        }

        foreach (var overrideEntry in settings.SeverityOverrides ??
                                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(overrideEntry.Key) || string.IsNullOrWhiteSpace(overrideEntry.Value)) {
                continue;
            }

            var updated = false;
            foreach (var existing in selected.Values) {
                if (!IsRuleMatch(overrideEntry.Key, existing.Rule)) {
                    continue;
                }
                selected[existing.Rule.Id] = new AnalysisPolicyRule(existing.Rule, overrideEntry.Value);
                updated = true;
            }
            if (!updated) {
                warnings.Add($"Severity override ignored (rule not selected): {overrideEntry.Key}");
            }
        }

        return new AnalysisPolicy(selected, warnings);
    }

    private static bool TryResolveOverride(IReadOnlyDictionary<string, string>? overrides, AnalysisRule rule,
        out string severity) {
        severity = string.Empty;
        if (overrides is null || overrides.Count == 0 || rule is null) {
            return false;
        }
        if (TryReadOverride(overrides, rule.Id, out severity)) {
            return true;
        }
        var toolRuleId = rule.ToolRuleId;
        if (!string.IsNullOrWhiteSpace(toolRuleId) && TryReadOverride(overrides, toolRuleId, out severity)) {
            return true;
        }
        return false;
    }

    private static bool TryReadOverride(IReadOnlyDictionary<string, string> overrides, string key, out string severity) {
        severity = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) {
            return false;
        }
        if (!overrides.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        severity = value;
        return true;
    }

    private static bool IsRuleDisabled(ISet<string> disabled, AnalysisRule rule) {
        if (disabled is null || disabled.Count == 0 || rule is null) {
            return false;
        }
        if (disabled.Contains(rule.Id)) {
            return true;
        }
        return !string.IsNullOrWhiteSpace(rule.ToolRuleId) && disabled.Contains(rule.ToolRuleId);
    }

    private static bool IsRuleMatch(string key, AnalysisRule rule) {
        if (string.IsNullOrWhiteSpace(key) || rule is null) {
            return false;
        }
        if (key.Equals(rule.Id, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        if (!string.IsNullOrWhiteSpace(rule.ToolRuleId) &&
            key.Equals(rule.ToolRuleId, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return false;
    }
}
