using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using IntelligenceX.Analysis;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Analysis;

internal static partial class AnalyzeGateCommand {
    private sealed record DuplicationGateEvaluation(
        bool Available,
        string? MetricsPath,
        int RulesEvaluated,
        IReadOnlyList<AnalysisFinding> Violations,
        IReadOnlyList<DuplicationOverallSnapshot> OverallSnapshots,
        IReadOnlyList<DuplicationFileSnapshot> FileSnapshots,
        string Scope,
        string? UnavailableReason) {
        public static DuplicationGateEvaluation Disabled => new(
            Available: true,
            MetricsPath: null,
            RulesEvaluated: 0,
            Violations: Array.Empty<AnalysisFinding>(),
            OverallSnapshots: Array.Empty<DuplicationOverallSnapshot>(),
            FileSnapshots: Array.Empty<DuplicationFileSnapshot>(),
            Scope: "changed-files",
            UnavailableReason: null);

        public static DuplicationGateEvaluation WithData(string metricsPath, int rulesEvaluated,
            IReadOnlyList<AnalysisFinding> violations,
            IReadOnlyList<DuplicationOverallSnapshot> overallSnapshots,
            IReadOnlyList<DuplicationFileSnapshot> fileSnapshots,
            string scope) {
            return new DuplicationGateEvaluation(
                Available: true,
                MetricsPath: metricsPath,
                RulesEvaluated: rulesEvaluated,
                Violations: violations ?? Array.Empty<AnalysisFinding>(),
                OverallSnapshots: overallSnapshots ?? Array.Empty<DuplicationOverallSnapshot>(),
                FileSnapshots: fileSnapshots ?? Array.Empty<DuplicationFileSnapshot>(),
                Scope: scope,
                UnavailableReason: null);
        }

        public static DuplicationGateEvaluation Unavailable(string reason) {
            return new DuplicationGateEvaluation(
                Available: false,
                MetricsPath: null,
                RulesEvaluated: 0,
                Violations: Array.Empty<AnalysisFinding>(),
                OverallSnapshots: Array.Empty<DuplicationOverallSnapshot>(),
                FileSnapshots: Array.Empty<DuplicationFileSnapshot>(),
                Scope: "changed-files",
                UnavailableReason: reason);
        }
    }

    private sealed record DuplicationOverallSnapshot(
        string RuleId,
        string Tool,
        string Scope,
        int SignificantLines,
        int DuplicatedLines,
        double DuplicatedPercent,
        int WindowLines,
        string Fingerprint);

    private sealed record DuplicationFileSnapshot(
        string RuleId,
        string Tool,
        string Scope,
        string Path,
        int SignificantLines,
        int DuplicatedLines,
        double DuplicatedPercent,
        int WindowLines,
        string Fingerprint);

    private sealed class GateOptions {
        public bool ShowHelp { get; set; }
        public string? Workspace { get; set; }
        public string? ConfigPath { get; set; }
        public string? ChangedFilesPath { get; set; }
        public bool NewIssuesOnly { get; set; }
        public string? BaselinePath { get; set; }
        public string? WriteBaselinePath { get; set; }
    }

    private readonly record struct GateFindingFilters(
        IReadOnlySet<string> AllowedTypes,
        IReadOnlySet<string> RuleIds,
        bool HasTypeFilter,
        bool HasRuleIdFilter,
        bool IncludeAllFilters) {
        public bool Matches(string ruleId, string? ruleType) {
            if (IncludeAllFilters) {
                return true;
            }

            var effectiveRuleType = string.IsNullOrEmpty(ruleType) ? "unknown" : ruleType;
            if (HasTypeFilter &&
                AllowedTypes.Contains(effectiveRuleType)) {
                return true;
            }

            return HasRuleIdFilter &&
                   !string.IsNullOrEmpty(ruleId) &&
                   RuleIds.Contains(ruleId);
        }
    }
}
