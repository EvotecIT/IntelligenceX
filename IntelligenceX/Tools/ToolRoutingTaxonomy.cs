using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Shared routing-taxonomy constants used by selection metadata and tool-catalog contracts.
/// </summary>
public static class ToolRoutingTaxonomy {
    /// <summary>Default scope value.</summary>
    public const string ScopeGeneral = "general";
    /// <summary>Default operation value.</summary>
    public const string OperationRead = "read";
    /// <summary>Default entity value.</summary>
    public const string EntityResource = "resource";
    /// <summary>Low-risk routing value.</summary>
    public const string RiskLow = "low";
    /// <summary>Medium-risk routing value.</summary>
    public const string RiskMedium = "medium";
    /// <summary>High-risk routing value.</summary>
    public const string RiskHigh = "high";
    /// <summary>Routing source for inferred metadata.</summary>
    public const string SourceInferred = "inferred";
    /// <summary>Routing source for explicit overrides.</summary>
    public const string SourceExplicit = "explicit";

    /// <summary>Allowed routing risk values.</summary>
    public static readonly IReadOnlyList<string> AllowedRisks = new[] {
        RiskLow,
        RiskMedium,
        RiskHigh
    };

    /// <summary>Allowed routing source values.</summary>
    public static readonly IReadOnlyList<string> AllowedSources = new[] {
        SourceExplicit,
        SourceInferred
    };

    /// <summary>
    /// Determines whether the supplied value is an allowed routing risk token.
    /// </summary>
    public static bool IsAllowedRisk(string? value) {
        if (value is null) {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < AllowedRisks.Count; i++) {
            if (string.Equals(normalized, AllowedRisks[i], StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Determines whether the supplied value is an allowed routing source token.
    /// </summary>
    public static bool IsAllowedSource(string? value) {
        if (value is null) {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < AllowedSources.Count; i++) {
            if (string.Equals(normalized, AllowedSources[i], StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }
}
