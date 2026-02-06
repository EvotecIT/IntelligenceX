using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Reviewer;

internal static class AnalysisPolicyBuilder {
    private const int MaxListItems = 10;

    public static string BuildPolicy(ReviewSettings settings, AnalysisLoadResult? loadResult = null) {
        if (settings?.Analysis?.Enabled != true || settings.Analysis?.Results?.ShowPolicy != true) {
            return string.Empty;
        }

        var packs = settings.Analysis.Packs ?? Array.Empty<string>();

        var workspace = ResolveWorkspaceRoot();
        AnalysisCatalog catalog;
        try {
            catalog = AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        } catch {
            return string.Empty;
        }
        var disabled = new HashSet<string>(settings.Analysis.DisabledRules ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var overrides = settings.Analysis.SeverityOverrides is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(settings.Analysis.SeverityOverrides, StringComparer.OrdinalIgnoreCase);

        var packSummaries = new List<string>();
        var selectedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingPacks = new List<string>();
        foreach (var packId in packs) {
            if (catalog.TryGetPack(packId, out var pack)) {
                packSummaries.Add(string.IsNullOrWhiteSpace(pack.Label) ? pack.Id : pack.Label);
                foreach (var ruleId in pack.Rules ?? Array.Empty<string>()) {
                    if (string.IsNullOrWhiteSpace(ruleId)) {
                        continue;
                    }
                    selectedRules.Add(ruleId);
                }
            } else if (!string.IsNullOrWhiteSpace(packId)) {
                missingPacks.Add(packId);
            }
        }

        var enabledRules = selectedRules.Where(rule => !disabled.Contains(rule)).ToList();
        var overridesCount = overrides.Count;

        var lines = new List<string> {
            "### Static analysis policy",
            $"Config mode: {DescribeConfigMode(settings.Analysis.ConfigMode)}"
        };

        lines.Add(packSummaries.Count > 0
            ? $"Packs: {string.Join(", ", packSummaries)}"
            : "Packs: none");
        if (missingPacks.Count > 0) {
            lines.Add($"Missing packs: {string.Join(", ", missingPacks)}");
        }

        lines.Add($"Rules: {enabledRules.Count} enabled" +
                  (disabled.Count > 0 ? $", {disabled.Count} disabled" : string.Empty) +
                  (overridesCount > 0 ? $", {overridesCount} overrides" : string.Empty));
        if (loadResult?.Report is not null) {
            var loadReport = loadResult.Report;
            lines.Add(
                $"Result files: {loadReport.ConfiguredInputs} input patterns, {loadReport.ResolvedInputFiles} matched, {loadReport.ParsedInputFiles} parsed, {loadReport.FailedInputFiles} failed");
            AddOutcomeLines(lines, enabledRules, loadResult.Findings ?? Array.Empty<AnalysisFinding>(), loadReport);
        }

        if (disabled.Count > 0) {
            var disabledList = disabled.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).Take(MaxListItems).ToList();
            var suffix = disabled.Count > disabledList.Count ? " (truncated)" : string.Empty;
            lines.Add($"Disabled: {string.Join(", ", disabledList)}{suffix}");
        }

        if (overridesCount > 0) {
            var overrideList = overrides
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxListItems)
                .Select(item => $"{item.Key}={item.Value}")
                .ToList();
            var suffix = overridesCount > overrideList.Count ? " (truncated)" : string.Empty;
            lines.Add($"Overrides: {string.Join(", ", overrideList)}{suffix}");
        }

        return string.Join("\n", lines).TrimEnd();
    }

    private static string DescribeConfigMode(AnalysisConfigMode mode) {
        return mode switch {
            AnalysisConfigMode.Respect => "respect",
            AnalysisConfigMode.Overlay => "overlay",
            AnalysisConfigMode.Replace => "replace",
            _ => "unknown"
        };
    }

    private static string ResolveWorkspaceRoot() {
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(workspace)) {
            return workspace;
        }
        return Environment.CurrentDirectory;
    }

    private static void AddOutcomeLines(ICollection<string> lines, IReadOnlyList<string> enabledRules,
        IReadOnlyList<AnalysisFinding> findings, AnalysisLoadReport loadReport) {
        if (loadReport.ResolvedInputFiles == 0) {
            lines.Add("Status: unavailable");
            lines.Add("Rule outcomes: unavailable (no analysis result files matched configured inputs)");
            return;
        }

        var enabledSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in enabledRules ?? Array.Empty<string>()) {
            var normalizedRuleId = NormalizeRuleId(rule);
            if (!string.IsNullOrWhiteSpace(normalizedRuleId)) {
                enabledSet.Add(normalizedRuleId);
            }
        }
        var findingRuleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var finding in findings ?? Array.Empty<AnalysisFinding>()) {
            var normalizedRuleId = NormalizeRuleId(finding.RuleId);
            if (string.IsNullOrWhiteSpace(normalizedRuleId)) {
                continue;
            }
            findingRuleCounts[normalizedRuleId] =
                (findingRuleCounts.TryGetValue(normalizedRuleId, out var count) ? count : 0) + 1;
        }

        var impactedEnabledRules = findingRuleCounts.Keys.Where(rule => enabledSet.Contains(rule)).ToList();
        var cleanEnabledRules = Math.Max(0, enabledSet.Count - impactedEnabledRules.Count);
        var status = impactedEnabledRules.Count > 0 ? "fail" : (loadReport.FailedInputFiles > 0 ? "partial" : "pass");
        lines.Add($"Status: {status}");
        lines.Add($"Rule outcomes: {impactedEnabledRules.Count} with findings, {cleanEnabledRules} clean");

        if (findingRuleCounts.Count == 0) {
            return;
        }

        var unknownRuleCount = findingRuleCounts.Keys.Count(rule => !enabledSet.Contains(rule));
        if (unknownRuleCount > 0) {
            lines.Add($"Findings outside enabled packs: {unknownRuleCount} rule(s)");
        }

        var topRules = findingRuleCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .Select(item => $"{item.Key}={item.Value}")
            .ToList();
        var suffix = findingRuleCounts.Count > topRules.Count ? " (truncated)" : string.Empty;
        lines.Add($"Rules with findings: {string.Join(", ", topRules)}{suffix}");
    }

    private static string? NormalizeRuleId(string? ruleId) {
        return string.IsNullOrWhiteSpace(ruleId) ? null : ruleId.Trim();
    }
}
