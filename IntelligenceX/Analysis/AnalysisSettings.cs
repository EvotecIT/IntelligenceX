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
    /// Paths or globs to analysis result files (SARIF or IntelligenceX findings JSON).
    /// </summary>
    public IReadOnlyList<string> Inputs { get; set; } = DefaultInputs;
    /// <summary>
    /// Minimum severity to include when rendering analysis findings.
    /// </summary>
    public string MinSeverity { get; set; } = "warning";
    /// <summary>
    /// Maximum inline analysis comments to emit.
    /// </summary>
    public int MaxInline { get; set; } = 20;
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
    /// Parses a config mode value with a fallback.
    /// </summary>
    public static AnalysisConfigMode ParseConfigMode(string? value, AnalysisConfigMode fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return value.Trim().ToLowerInvariant() switch {
            "overlay" => AnalysisConfigMode.Overlay,
            "replace" => AnalysisConfigMode.Replace,
            _ => AnalysisConfigMode.Respect
        };
    }
}
