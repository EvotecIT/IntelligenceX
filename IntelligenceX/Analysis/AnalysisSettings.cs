using System;
using System.Collections.Generic;

namespace IntelligenceX.Analysis;

/// <summary>
/// Controls how analysis configs interact with existing repository settings.
/// </summary>
public enum AnalysisConfigMode {
    /// <summary>
    /// Keep repository analyzer configs intact and only filter findings.
    /// </summary>
    Respect,
    /// <summary>
    /// Merge pack rules on top of repository configs for the analysis run.
    /// </summary>
    Overlay,
    /// <summary>
    /// Ignore repository configs and use only pack rules for the analysis run.
    /// </summary>
    Replace
}

/// <summary>
/// Settings that control how analysis findings are ingested and rendered.
/// </summary>
public sealed class AnalysisResultsSettings {
    private static readonly IReadOnlyList<string> DefaultInputs = new[] {
        "artifacts/**/*.sarif",
        "artifacts/intelligencex.findings.json"
    };
    /// <summary>
    /// Default number of rules shown per policy preview line.
    /// </summary>
    public const int DefaultPolicyRulePreviewItems = 10;
    /// <summary>
    /// Maximum number of rules shown per policy preview line.
    /// </summary>
    public const int MaxPolicyRulePreviewItems = 500;

    /// <summary>
    /// Paths or globs to analysis result files (SARIF or IntelligenceX findings JSON).
    /// </summary>
    public IReadOnlyList<string> Inputs { get; set; } = DefaultInputs;
    /// <summary>
    /// Minimum severity to include when rendering analysis findings.
    /// </summary>
    public string MinSeverity { get; set; } = "warning";
    /// <summary>
    /// Maximum inline static-analysis comments to emit when a repository opts into legacy inline comments.
    /// Defaults to <c>0</c>, which keeps static-analysis findings summary-only.
    /// </summary>
    public int MaxInline { get; set; } = 0;
    /// <summary>
    /// Whether to emit a summary block for analysis findings.
    /// </summary>
    public bool Summary { get; set; } = true;
    /// <summary>
    /// Maximum findings to include in the summary block.
    /// </summary>
    public int SummaryMaxItems { get; set; } = 10;
    /// <summary>
    /// Placement for the summary block.
    /// </summary>
    public string SummaryPlacement { get; set; } = "bottom";
    /// <summary>
    /// Whether to include a policy overview in the PR summary.
    /// </summary>
    public bool ShowPolicy { get; set; } = true;
    /// <summary>
    /// Maximum number of rules shown per policy rule list line (for example enabled, failing, clean).
    /// Set to 0 to hide per-rule lists while keeping aggregate counts.
    /// </summary>
    public int PolicyRulePreviewItems { get; set; } = DefaultPolicyRulePreviewItems;
}

/// <summary>
/// Settings that control how security hotspots are rendered and tracked.
/// </summary>
public sealed class AnalysisHotspotsSettings {
    /// <summary>
    /// Whether to render the security hotspots block.
    /// </summary>
    public bool Show { get; set; } = true;
    /// <summary>
    /// Maximum number of hotspots shown in the summary block. Set to 0 to hide item lists while keeping counts.
    /// </summary>
    public int MaxItems { get; set; } = 10;
    /// <summary>
    /// Relative or absolute path to the persisted hotspots state file.
    /// Defaults to a repo-local file under .intelligencex.
    /// </summary>
    public string StatePath { get; set; } = ".intelligencex/hotspots.json";
    /// <summary>
    /// Whether to include state breakdown and missing-state hints in output.
    /// </summary>
    public bool ShowStateSummary { get; set; } = true;
    /// <summary>
    /// Render the hotspots block even when there are no hotspots in the current PR.
    /// </summary>
    public bool AlwaysRender { get; set; }
}

/// <summary>
/// Settings that control <c>intelligencex analyze run</c> execution behavior.
/// </summary>
public sealed class AnalysisRunSettings {
    /// <summary>
    /// When true, analysis runner failures (for example tool execution failures) return a non-zero exit code.
    /// </summary>
    public bool Strict { get; set; }
}

/// <summary>
/// Settings that control CI gating behavior for static analysis.
/// </summary>
public sealed class AnalysisGateSettings {
    /// <summary>
    /// Enables the analysis gate. When enabled, <c>intelligencex analyze gate</c> can fail CI deterministically.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Minimum severity to consider for gate evaluation. Defaults to <c>warning</c>.
    /// </summary>
    public string MinSeverity { get; set; } = "warning";
    /// <summary>
    /// Optional list of rule types to gate on (e.g. bug, vulnerability). When empty, all types are considered.
    /// </summary>
    public IReadOnlyList<string> Types { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Optional list of explicit rule IDs to gate on.
    /// When set together with <see cref="Types"/>, a finding is in-scope when it matches either filter.
    /// </summary>
    public IReadOnlyList<string> RuleIds { get; set; } = Array.Empty<string>();
    /// <summary>
    /// When true, findings for rules outside the enabled packs can fail the gate (useful to detect untracked tool rules).
    /// </summary>
    public bool IncludeOutsidePackRules { get; set; }
    /// <summary>
    /// When true (default), fail when results are unavailable (no matched inputs, parse failures, etc.).
    /// </summary>
    public bool FailOnUnavailable { get; set; } = true;
    /// <summary>
    /// When true (default), fail when no rules are enabled (for example packs missing or empty).
    /// </summary>
    public bool FailOnNoEnabledRules { get; set; } = true;
    /// <summary>
    /// When true, fail the gate if any in-scope security hotspot has state <c>to-review</c>.
    /// </summary>
    public bool FailOnHotspotsToReview { get; set; }
    /// <summary>
    /// When true, the gate evaluates only findings that are not present in the configured baseline.
    /// </summary>
    public bool NewIssuesOnly { get; set; }
    /// <summary>
    /// Relative or absolute path to the findings baseline used when <see cref="NewIssuesOnly"/> is enabled.
    /// </summary>
    public string BaselinePath { get; set; } = ".intelligencex/analysis-baseline.json";
    /// <summary>
    /// Duplication-specific gate settings.
    /// </summary>
    public AnalysisGateDuplicationSettings Duplication { get; } = new AnalysisGateDuplicationSettings();
}

/// <summary>
/// Settings that control duplication-specific gate behavior.
/// </summary>
public sealed class AnalysisGateDuplicationSettings {
    /// <summary>
    /// Enables duplication gate checks based on duplication metrics produced by <c>analyze run</c>.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Path to duplication metrics JSON emitted by <c>analyze run</c>.
    /// </summary>
    public string MetricsPath { get; set; } = "artifacts/intelligencex.duplication.json";
    /// <summary>
    /// Rule IDs to evaluate from the duplication metrics payload.
    /// </summary>
    public IReadOnlyList<string> RuleIds { get; set; } = new[] { "IXDUP001" };
    /// <summary>
    /// Optional per-file duplication threshold override (0-100). When null, each rule's configured threshold is used.
    /// </summary>
    public double? MaxFilePercent { get; set; }
    /// <summary>
    /// Optional allowed per-file duplication increase (in percentage points, 0-100) compared to the baseline file snapshot.
    /// Requires <c>analysis.gate.baselinePath</c> to be configured and present.
    /// </summary>
    public double? MaxFilePercentIncrease { get; set; }
    /// <summary>
    /// Optional overall duplication threshold (0-100) across all significant lines.
    /// </summary>
    public double? MaxOverallPercent { get; set; }
    /// <summary>
    /// Optional allowed increase (in percentage points, 0-100) compared to the baseline overall duplication snapshot.
    /// Requires <c>analysis.gate.baselinePath</c> to be configured and present.
    /// </summary>
    public double? MaxOverallPercentIncrease { get; set; }
    /// <summary>
    /// Scope used for duplication gating. Supported values: <c>changed-files</c> (default) and <c>all</c>.
    /// </summary>
    public string Scope { get; set; } = "changed-files";
    /// <summary>
    /// True when <c>analysis.gate.duplication.scope</c> was explicitly provided in config.
    /// Used to distinguish default scope fallback from an intentional strict scope selection.
    /// </summary>
    public bool ScopeExplicitlyConfigured { get; set; }
    /// <summary>
    /// When true, duplication gate checks honor baseline/new-only suppression semantics.
    /// </summary>
    public bool NewIssuesOnly { get; set; }
    /// <summary>
    /// When true, unavailable duplication metrics fail the gate.
    /// </summary>
    public bool FailOnUnavailable { get; set; }
}

/// <summary>
/// Analysis configuration derived from reviewer.json.
/// </summary>
public sealed class AnalysisSettings {
    /// <summary>
    /// Enables analysis ingestion and rendering.
    /// </summary>
    public bool Enabled { get; set; }
    /// <summary>
    /// Strategy for applying pack rules relative to existing repo configs.
    /// </summary>
    public AnalysisConfigMode ConfigMode { get; set; } = AnalysisConfigMode.Respect;
    /// <summary>
    /// Pack identifiers to enable.
    /// </summary>
    public IReadOnlyList<string> Packs { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Rule IDs to disable after packs are applied.
    /// </summary>
    public IReadOnlyList<string> DisabledRules { get; set; } = Array.Empty<string>();
    /// <summary>
    /// Per-rule severity overrides.
    /// </summary>
    public IReadOnlyDictionary<string, string> SeverityOverrides { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// Rendering and ingestion settings for analysis findings.
    /// </summary>
    public AnalysisResultsSettings Results { get; } = new AnalysisResultsSettings();
    /// <summary>
    /// Security hotspot rendering and state settings.
    /// </summary>
    public AnalysisHotspotsSettings Hotspots { get; } = new AnalysisHotspotsSettings();
    /// <summary>
    /// Analysis runner execution settings.
    /// </summary>
    public AnalysisRunSettings Run { get; } = new AnalysisRunSettings();
    /// <summary>
    /// CI gate settings.
    /// </summary>
    public AnalysisGateSettings Gate { get; } = new AnalysisGateSettings();

    /// <summary>
    /// Parses a config mode value with a fallback.
    /// </summary>
    public static AnalysisConfigMode ParseConfigMode(string? value, AnalysisConfigMode fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return value!.Trim().ToLowerInvariant() switch {
            "overlay" => AnalysisConfigMode.Overlay,
            "replace" => AnalysisConfigMode.Replace,
            _ => AnalysisConfigMode.Respect
        };
    }
}
