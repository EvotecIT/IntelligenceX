using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using IntelligenceX.Analysis;

namespace IntelligenceX.Reviewer;

internal static class AnalysisPolicyBuilder {
    private enum PolicyContextBuildResult {
        Disabled,
        Ready,
        CatalogUnavailable
    }

    private static readonly StringComparer OrdinalIgnoreCaseComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly StringComparer OrdinalComparer = StringComparer.Ordinal;

    private sealed record PolicyContext(
        IReadOnlyList<string> BaseLines,
        IReadOnlyList<string> EnabledRules,
        AnalysisCatalog? Catalog,
        IReadOnlyList<string> DisabledRules,
        IReadOnlyDictionary<string, string> Overrides
    );

    public static string BuildPolicy(ReviewSettings settings, AnalysisLoadResult? loadResult = null,
        Func<string, AnalysisCatalog>? catalogLoader = null) {
        var buildResult = TryBuildBasePolicy(settings, catalogLoader, out var context, out var unavailableReason);
        if (buildResult == PolicyContextBuildResult.Disabled) {
            return string.Empty;
        }
        if (buildResult == PolicyContextBuildResult.CatalogUnavailable) {
            return BuildCatalogUnavailablePolicy(settings, unavailableReason);
        }

        var lines = new List<string>(context.BaseLines);
        if (loadResult?.Report is not null) {
            var loadReport = loadResult.Report;
            lines.Add(
                $"- Result files: {loadReport.ConfiguredInputs} input patterns, {loadReport.ResolvedInputFiles} matched, {loadReport.ParsedInputFiles} parsed, {loadReport.FailedInputFiles} failed");
            AddOutcomeLines(lines, context.EnabledRules, loadResult.Findings, loadReport, context.Catalog);
        }

        return RenderPolicy(lines, context.DisabledRules, context.Overrides);
    }

    public static string BuildUnavailablePolicy(ReviewSettings settings, string reason,
        Func<string, AnalysisCatalog>? catalogLoader = null) {
        var buildResult = TryBuildBasePolicy(settings, catalogLoader, out var context, out var unavailableReason);
        if (buildResult == PolicyContextBuildResult.Disabled) {
            return string.Empty;
        }
        if (buildResult == PolicyContextBuildResult.CatalogUnavailable) {
            return BuildCatalogUnavailablePolicy(settings, unavailableReason);
        }

        var lines = new List<string>(context.BaseLines);
        var resolvedReason = SanitizeUnavailableReason(reason);
        lines.Add("- Status: unavailable ℹ️");
        lines.Add($"- Rule outcomes: unavailable ({resolvedReason})");
        return RenderPolicy(lines, context.DisabledRules, context.Overrides);
    }

    private static PolicyContextBuildResult TryBuildBasePolicy(ReviewSettings settings,
        Func<string, AnalysisCatalog>? catalogLoader,
        out PolicyContext context,
        out string unavailableReason) {
        context = new PolicyContext(
            Array.Empty<string>(),
            Array.Empty<string>(),
            null,
            Array.Empty<string>(),
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(OrdinalIgnoreCaseComparer)));
        unavailableReason = "analysis catalog unavailable";
        if (settings?.Analysis?.Enabled != true || settings.Analysis?.Results?.ShowPolicy != true) {
            return PolicyContextBuildResult.Disabled;
        }

        var packs = settings.Analysis.Packs ?? Array.Empty<string>();

        var workspace = ResolveWorkspaceRoot();
        if (!TryLoadCatalog(workspace, catalogLoader, out var loadedCatalog, out unavailableReason)) {
            return PolicyContextBuildResult.CatalogUnavailable;
        }

        var disabledSet = new HashSet<string>(settings.Analysis.DisabledRules ?? Array.Empty<string>(),
            OrdinalIgnoreCaseComparer);
        var overrideMap = settings.Analysis.SeverityOverrides is null
            ? new Dictionary<string, string>(OrdinalIgnoreCaseComparer)
            : new Dictionary<string, string>(settings.Analysis.SeverityOverrides, OrdinalIgnoreCaseComparer);

        var packSummaries = new List<string>();
        var selectedRuleIds = new HashSet<string>(OrdinalIgnoreCaseComparer);
        var selectedRules = new List<string>();
        var missingPacks = new List<string>();
        foreach (var packId in packs) {
            if (loadedCatalog.TryGetPack(packId, out var pack)) {
                packSummaries.Add(string.IsNullOrWhiteSpace(pack.Label) ? pack.Id : pack.Label);
                foreach (var ruleId in pack.Rules ?? Array.Empty<string>()) {
                    if (string.IsNullOrWhiteSpace(ruleId)) {
                        continue;
                    }
                    if (selectedRuleIds.Add(ruleId)) {
                        selectedRules.Add(ruleId);
                    }
                }
            } else if (!string.IsNullOrWhiteSpace(packId)) {
                missingPacks.Add(packId);
            }
        }

        // Effective enabled order follows pack rule order after disabled-rule filtering.
        var enabledRules = selectedRules.Where(rule => !disabledSet.Contains(rule)).ToList();

        var lines = new List<string> {
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
                  (overrideMap.Count > 0 ? $", {overrideMap.Count} overrides" : string.Empty));
        AddEnabledRulePreviewLine(lines, enabledRules, loadedCatalog);
        context = new PolicyContext(
            lines.ToArray(),
            enabledRules,
            loadedCatalog,
            disabledSet.ToArray(),
            new ReadOnlyDictionary<string, string>(overrideMap));
        return PolicyContextBuildResult.Ready;
    }

    private static bool TryLoadCatalog(string workspace, Func<string, AnalysisCatalog>? catalogLoader,
        out AnalysisCatalog catalog,
        out string unavailableReason) {
        catalog = null!;
        unavailableReason = "analysis catalog unavailable";
        var loader = catalogLoader ?? AnalysisCatalogLoader.LoadFromWorkspace;
        try {
            catalog = loader(workspace);
            return true;
        } catch (UnauthorizedAccessException ex) {
            unavailableReason = BuildCatalogLoadUnavailableReason(ex);
            return false;
        } catch (SecurityException ex) {
            unavailableReason = BuildCatalogLoadUnavailableReason(ex);
            return false;
        } catch (IOException ex) {
            unavailableReason = BuildCatalogLoadUnavailableReason(ex);
            return false;
        } catch (ArgumentException ex) {
            unavailableReason = BuildCatalogLoadUnavailableReason(ex);
            return false;
        } catch (NotSupportedException ex) {
            unavailableReason = BuildCatalogLoadUnavailableReason(ex);
            return false;
        }
    }

    private static string BuildCatalogLoadUnavailableReason(Exception ex) {
        return ex switch {
            UnauthorizedAccessException => "insufficient permissions while loading analysis catalog",
            SecurityException => "security policy blocked analysis catalog loading",
            IOException => "I/O error while loading analysis catalog",
            ArgumentException => "invalid analysis catalog path configuration",
            NotSupportedException => "unsupported analysis catalog path configuration",
            _ => "analysis catalog unavailable"
        };
    }

    private static string BuildCatalogUnavailablePolicy(ReviewSettings settings, string reason) {
        if (settings?.Analysis?.Enabled != true || settings.Analysis?.Results?.ShowPolicy != true) {
            return string.Empty;
        }

        var lines = new List<string> {
            "### Static Analysis Policy 🧭",
            $"- Config mode: {DescribeConfigMode(settings.Analysis.ConfigMode)}"
        };

        var packs = (settings.Analysis.Packs ?? Array.Empty<string>())
            .Where(pack => !string.IsNullOrWhiteSpace(pack))
            .Select(pack => pack.Trim())
            .ToArray();
        lines.Add(packs.Length > 0
            ? $"- Packs: {string.Join(", ", packs)}"
            : "- Packs: none");
        lines.Add("- Rules: unavailable (analysis catalog could not be loaded)");
        lines.Add("- Status: unavailable ℹ️");
        lines.Add($"- Rule outcomes: unavailable ({SanitizeUnavailableReason(reason)})");
        return string.Join("\n", lines).TrimEnd();
    }

    private static void AddRuleConfigurationLines(ICollection<string> lines, IReadOnlyCollection<string> disabled,
        IReadOnlyDictionary<string, string> overrides) {
        if (disabled.Count > 0) {
            var disabledList = disabled.OrderBy(item => item, OrdinalComparer)
                .Take(AnalysisPolicyFormatting.MaxRulePreviewItems)
                .ToList();
            var suffix = disabled.Count > disabledList.Count ? AnalysisPolicyFormatting.TruncatedPreviewSuffix : string.Empty;
            lines.Add($"- Disabled: {string.Join(", ", disabledList)}{suffix}");
        }

        if (overrides.Count > 0) {
            var overrideList = overrides
                .OrderBy(item => item.Key, OrdinalComparer)
                .Take(AnalysisPolicyFormatting.MaxRulePreviewItems)
                .Select(item => $"{item.Key}={item.Value}")
                .ToList();
            var suffix = overrides.Count > overrideList.Count
                ? AnalysisPolicyFormatting.TruncatedPreviewSuffix
                : string.Empty;
            lines.Add($"- Overrides: {string.Join(", ", overrideList)}{suffix}");
        }
    }

    private static string RenderPolicy(ICollection<string> lines, IReadOnlyCollection<string> disabled,
        IReadOnlyDictionary<string, string> overrides) {
        AddRuleConfigurationLines(lines, disabled, overrides);
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

    private static void AddEnabledRulePreviewLine(IList<string> lines, IReadOnlyList<string> enabledRules,
        AnalysisCatalog catalog) {
        if (enabledRules.Count == 0) {
            lines.Add("- Enabled rules preview: none");
            return;
        }

        var preview = enabledRules
            .Take(AnalysisPolicyFormatting.MaxRulePreviewItems)
            .Select(ruleId => DescribeRuleForPreview(ruleId, catalog))
            .ToList();
        var suffix = enabledRules.Count > preview.Count ? AnalysisPolicyFormatting.TruncatedPreviewSuffix : string.Empty;
        lines.Add($"- Enabled rules preview: {string.Join(", ", preview)}{suffix}");
    }

    private static string DescribeRuleForPreview(string ruleId, AnalysisCatalog? catalog) {
        if (catalog is null || !catalog.TryGetRule(ruleId, out var rule)) {
            return ruleId;
        }
        if (string.IsNullOrWhiteSpace(rule.Title)) {
            return rule.Id;
        }
        return $"{rule.Id} ({TruncatePreviewTitle(rule.Title)})";
    }

    private static void AddOutcomeLines(ICollection<string> lines, IReadOnlyList<string> enabledRules,
        IReadOnlyList<AnalysisFinding>? findings, AnalysisLoadReport loadReport, AnalysisCatalog? catalog) {
        if (loadReport.ResolvedInputFiles == 0) {
            lines.Add("- Status: unavailable ℹ️");
            lines.Add("- Rule outcomes: unavailable (no analysis result files matched configured inputs)");
            return;
        }

        var normalizedEnabledRules = enabledRules
            .Select(NormalizeRuleId)
            .OfType<string>()
            .Distinct(OrdinalIgnoreCaseComparer)
            .ToList();
        var enabledSet = new HashSet<string>(
            normalizedEnabledRules,
            OrdinalIgnoreCaseComparer);

        var resolvedFindings = findings ?? Array.Empty<AnalysisFinding>();
        var findingRuleCounts = resolvedFindings
            .Select(finding => NormalizeRuleId(finding.RuleId))
            .OfType<string>()
            .GroupBy(normalizedRuleId => normalizedRuleId, OrdinalIgnoreCaseComparer)
            .ToDictionary(group => group.Key, group => group.Count(), OrdinalIgnoreCaseComparer);

        if (enabledSet.Count == 0 && findingRuleCounts.Count == 0) {
            lines.Add("- Status: unavailable ℹ️");
            lines.Add("- Rule outcomes: unavailable (no enabled rules configured)");
            return;
        }

        var impactedEnabledRules = normalizedEnabledRules
            .Where(rule => findingRuleCounts.ContainsKey(rule))
            .ToList();
        var outsideEnabledRules = findingRuleCounts.Keys.Count(rule => !enabledSet.Contains(rule));
        var cleanEnabledRules = normalizedEnabledRules
            .Where(rule => !findingRuleCounts.ContainsKey(rule))
            .ToList();
        var status = impactedEnabledRules.Count > 0
            ? "fail"
            : (loadReport.FailedInputFiles > 0 || outsideEnabledRules > 0 ? "partial" : "pass");
        lines.Add($"- Status: {FormatStatus(status)}");
        lines.Add($"- Rule outcomes: {impactedEnabledRules.Count} with findings, {cleanEnabledRules.Count} clean" +
                  (outsideEnabledRules > 0 ? $", {outsideEnabledRules} outside enabled packs" : string.Empty));
        AddRuleCountPreviewLine(lines, "Failing rules", impactedEnabledRules
            .Select(ruleId => new KeyValuePair<string, int>(ruleId, findingRuleCounts[ruleId]))
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, OrdinalComparer)
            .ToList(), catalog);
        AddRulePreviewLine(lines, "Clean rules", cleanEnabledRules, catalog);
        AddRuleCountPreviewLine(lines, "Outside-pack rules", findingRuleCounts
            .Where(item => !enabledSet.Contains(item.Key))
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Key, OrdinalComparer)
            .ToList(), catalog);
    }

    private static void AddRulePreviewLine(ICollection<string> lines, string label, IReadOnlyList<string> ruleIds,
        AnalysisCatalog? catalog) {
        if (ruleIds.Count == 0) {
            lines.Add($"- {label}: none");
            return;
        }

        var preview = ruleIds
            .Take(AnalysisPolicyFormatting.MaxRulePreviewItems)
            .Select(ruleId => DescribeRuleForPreview(ruleId, catalog))
            .ToList();
        var suffix = ruleIds.Count > preview.Count ? AnalysisPolicyFormatting.TruncatedPreviewSuffix : string.Empty;
        lines.Add($"- {label}: {string.Join(", ", preview)}{suffix}");
    }

    private static void AddRuleCountPreviewLine(ICollection<string> lines, string label,
        IReadOnlyList<KeyValuePair<string, int>> ruleCounts, AnalysisCatalog? catalog) {
        if (ruleCounts.Count == 0) {
            lines.Add($"- {label}: none");
            return;
        }

        var preview = ruleCounts
            .Take(AnalysisPolicyFormatting.MaxRulePreviewItems)
            .Select(item => $"{DescribeRuleForPreview(item.Key, catalog)}={item.Value}")
            .ToList();
        var suffix = ruleCounts.Count > preview.Count ? AnalysisPolicyFormatting.TruncatedPreviewSuffix : string.Empty;
        lines.Add($"- {label}: {string.Join(", ", preview)}{suffix}");
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
        if (info.LengthInTextElements <= AnalysisPolicyFormatting.MaxUnavailableReasonTextElements) {
            return resolved;
        }
        return info.SubstringByTextElements(0, AnalysisPolicyFormatting.MaxUnavailableReasonTextElements) +
               AnalysisPolicyFormatting.TruncationEllipsis;
    }

    private static string? NormalizeRuleId(string? ruleId) {
        return string.IsNullOrWhiteSpace(ruleId) ? null : ruleId.Trim();
    }

    private static string TruncatePreviewTitle(string? title) {
        if (string.IsNullOrWhiteSpace(title)) {
            return string.Empty;
        }
        var resolved = title.Trim();
        var info = new global::System.Globalization.StringInfo(resolved);
        if (info.LengthInTextElements <= AnalysisPolicyFormatting.MaxRulePreviewTitleTextElements) {
            return resolved;
        }
        return info.SubstringByTextElements(0, AnalysisPolicyFormatting.MaxRulePreviewTitleTextElements) +
               AnalysisPolicyFormatting.TruncationEllipsis;
    }
}
