using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Analysis;

namespace IntelligenceX.Reviewer;

internal static class AnalysisPolicyBuilder {
    private const int MaxListItems = 10;
    private const int MaxUnavailableReasonTextElements = 120;

    public static string BuildPolicy(ReviewSettings settings, AnalysisLoadResult? loadResult = null) {
        if (!TryBuildBasePolicy(settings, out var lines, out var enabledRules, out var disabled, out var overrides,
                out var overridesCount)) {
            return string.Empty;
        }
        if (loadResult?.Report is not null) {
            var loadReport = loadResult.Report;
            lines.Add(
                $"- Result files: {loadReport.ConfiguredInputs} input patterns, {loadReport.ResolvedInputFiles} matched, {loadReport.ParsedInputFiles} parsed, {loadReport.FailedInputFiles} failed");
            AddOutcomeLines(lines, enabledRules, loadResult.Findings, loadReport);
        }

        return RenderPolicy(lines, disabled, overrides, overridesCount);
    }

    public static string BuildUnavailablePolicy(ReviewSettings settings, string reason) {
        if (!TryBuildBasePolicy(settings, out var lines, out _, out var disabled, out var overrides, out var overridesCount)) {
            return string.Empty;
        }

        var resolvedReason = SanitizeUnavailableReason(reason);
        lines.Add("- Status: unavailable ℹ️");
        lines.Add($"- Rule outcomes: unavailable ({resolvedReason})");
        return RenderPolicy(lines, disabled, overrides, overridesCount);
    }

    private static bool TryBuildBasePolicy(ReviewSettings settings,
        out List<string> lines,
        out IReadOnlyList<string> enabledRules,
        out HashSet<string> disabled,
        out Dictionary<string, string> overrides,
        out int overridesCount) {
        lines = new List<string>();
        enabledRules = Array.Empty<string>();
        disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        overridesCount = 0;

        if (settings?.Analysis?.Enabled != true || settings.Analysis?.Results?.ShowPolicy != true) {
            return false;
        }

        var packs = settings.Analysis.Packs ?? Array.Empty<string>();

        var workspace = ResolveWorkspaceRoot();
        AnalysisCatalog catalog;
        try {
            catalog = AnalysisCatalogLoader.LoadFromWorkspace(workspace);
        } catch {
            return false;
        }

        var disabledSet = new HashSet<string>(settings.Analysis.DisabledRules ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);
        var overrideMap = settings.Analysis.SeverityOverrides is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(settings.Analysis.SeverityOverrides, StringComparer.OrdinalIgnoreCase);
        var overrideCount = overrideMap.Count;

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

        enabledRules = selectedRules.Where(rule => !disabledSet.Contains(rule)).ToList();

        lines = new List<string> {
            "### Static Analysis Policy 🧭",
            $"- Config mode: {DescribeConfigMode(settings.Analysis.ConfigMode)}"
        };

        lines.Add(packSummaries.Count > 0
            ? $"- Packs: {string.Join(", ", packSummaries)}"
            : "- Packs: none");
        if (missingPacks.Count > 0) {
            lines.Add($"- Missing packs: {string.Join(", ", missingPacks)}");
        }

        lines.Add($"- Rules: {enabledRules.Count} enabled" +
                  (disabledSet.Count > 0 ? $", {disabledSet.Count} disabled" : string.Empty) +
                  (overrideCount > 0 ? $", {overrideCount} overrides" : string.Empty));
        AddEnabledRulePreviewLine(lines, enabledRules, catalog);
        disabled = disabledSet;
        overrides = overrideMap;
        overridesCount = overrideCount;
        return true;
    }

    private static void AddRuleConfigurationLines(ICollection<string> lines, HashSet<string> disabled,
        IReadOnlyDictionary<string, string> overrides, int overridesCount) {
        if (disabled.Count > 0) {
            var disabledList = disabled.OrderBy(item => item, StringComparer.OrdinalIgnoreCase).Take(MaxListItems).ToList();
            var suffix = disabled.Count > disabledList.Count ? " (truncated)" : string.Empty;
            lines.Add($"- Disabled: {string.Join(", ", disabledList)}{suffix}");
        }

        if (overridesCount > 0) {
            var overrideList = overrides
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxListItems)
                .Select(item => $"{item.Key}={item.Value}")
                .ToList();
            var suffix = overridesCount > overrideList.Count ? " (truncated)" : string.Empty;
            lines.Add($"- Overrides: {string.Join(", ", overrideList)}{suffix}");
        }
    }

    private static string RenderPolicy(ICollection<string> lines, HashSet<string> disabled,
        IReadOnlyDictionary<string, string> overrides, int overridesCount) {
        AddRuleConfigurationLines(lines, disabled, overrides, overridesCount);
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

    private static void AddEnabledRulePreviewLine(ICollection<string> lines, IReadOnlyList<string> enabledRules,
        AnalysisCatalog catalog) {
        if (enabledRules.Count == 0) {
            lines.Add("- Enabled rules preview: none");
            return;
        }

        var preview = enabledRules
            .OrderBy(rule => rule, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .Select(ruleId => DescribeRuleForPreview(ruleId, catalog))
            .ToList();
        var suffix = enabledRules.Count > preview.Count ? " (truncated)" : string.Empty;
        lines.Add($"- Enabled rules preview: {string.Join(", ", preview)}{suffix}");
    }

    private static string DescribeRuleForPreview(string ruleId, AnalysisCatalog catalog) {
        if (!catalog.TryGetRule(ruleId, out var rule)) {
            return ruleId;
        }
        if (string.IsNullOrWhiteSpace(rule.Title)) {
            return rule.Id;
        }
        return $"{rule.Id} ({rule.Title})";
    }

    private static void AddOutcomeLines(ICollection<string> lines, IReadOnlyList<string> enabledRules,
        IReadOnlyList<AnalysisFinding> findings, AnalysisLoadReport loadReport) {
        if (loadReport.ResolvedInputFiles == 0) {
            lines.Add("- Status: unavailable ℹ️");
            lines.Add("- Rule outcomes: unavailable (no analysis result files matched configured inputs)");
            return;
        }

        var enabledSet = new HashSet<string>(
            enabledRules
            .Select(NormalizeRuleId)
            .OfType<string>(),
            StringComparer.OrdinalIgnoreCase);

        var findingRuleCounts = findings
            .Select(finding => NormalizeRuleId(finding.RuleId))
            .OfType<string>()
            .GroupBy(normalizedRuleId => normalizedRuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        if (enabledSet.Count == 0 && findingRuleCounts.Count == 0) {
            lines.Add("- Status: unavailable ℹ️");
            lines.Add("- Rule outcomes: unavailable (no enabled rules configured)");
            return;
        }

        var impactedEnabledRules = findingRuleCounts.Keys.Where(rule => enabledSet.Contains(rule)).ToList();
        var outsideEnabledRules = findingRuleCounts.Keys.Count(rule => !enabledSet.Contains(rule));
        var cleanEnabledRules = Math.Max(0, enabledSet.Count - impactedEnabledRules.Count);
        var status = impactedEnabledRules.Count > 0
            ? "fail"
            : (loadReport.FailedInputFiles > 0 || outsideEnabledRules > 0 ? "partial" : "pass");
        lines.Add($"- Status: {FormatStatus(status)}");
        lines.Add($"- Rule outcomes: {impactedEnabledRules.Count} with findings, {cleanEnabledRules} clean" +
                  (outsideEnabledRules > 0 ? $", {outsideEnabledRules} outside enabled packs" : string.Empty));

        if (findingRuleCounts.Count == 0) {
            return;
        }

        if (outsideEnabledRules > 0) {
            lines.Add($"- Findings outside enabled packs: {outsideEnabledRules} rule(s)");
        }

        var topRules = findingRuleCounts
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxListItems)
            .Select(item => $"{item.Key}={item.Value}")
            .ToList();
        var suffix = findingRuleCounts.Count > topRules.Count ? " (truncated)" : string.Empty;
        lines.Add($"- Rules with findings: {string.Join(", ", topRules)}{suffix}");
    }

    private static string FormatStatus(string status) {
        return status switch {
            "fail" => "fail ❌",
            "partial" => "partial ⚠️",
            "pass" => "pass ✅",
            "unavailable" => "unavailable ℹ️",
            _ => status
        };
    }

    private static string SanitizeUnavailableReason(string? reason) {
        var resolved = string.IsNullOrWhiteSpace(reason)
            ? "internal error while loading analysis results"
            : reason.Replace("\r", " ").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(resolved)) {
            return "internal error while loading analysis results";
        }

        var info = new global::System.Globalization.StringInfo(resolved);
        if (info.LengthInTextElements <= MaxUnavailableReasonTextElements) {
            return resolved;
        }
        return info.SubstringByTextElements(0, MaxUnavailableReasonTextElements) + "...";
    }

    private static string? NormalizeRuleId(string? ruleId) {
        return string.IsNullOrWhiteSpace(ruleId) ? null : ruleId.Trim();
    }
}
