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
    /// <summary>Routing tag prefix for scope taxonomy tags.</summary>
    public const string ScopeTagPrefix = "scope:";
    /// <summary>Routing tag prefix for operation taxonomy tags.</summary>
    public const string OperationTagPrefix = "operation:";
    /// <summary>Routing tag prefix for entity taxonomy tags.</summary>
    public const string EntityTagPrefix = "entity:";
    /// <summary>Routing tag prefix for risk taxonomy tags.</summary>
    public const string RiskTagPrefix = "risk:";
    /// <summary>Routing tag prefix for routing-source taxonomy tags.</summary>
    public const string RoutingTagPrefix = "routing:";

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

    /// <summary>
    /// Returns true when the tag belongs to the routing taxonomy namespace.
    /// </summary>
    public static bool IsTaxonomyTag(string? tag) {
        return TryGetTagKey(tag, out _);
    }

    /// <summary>
    /// Gets the taxonomy tag key (<c>scope</c>, <c>operation</c>, <c>entity</c>, <c>risk</c>, <c>routing</c>)
    /// for a routing taxonomy tag.
    /// </summary>
    public static bool TryGetTagKey(string? tag, out string tagKey) {
        tagKey = string.Empty;
        if (tag is null) {
            return false;
        }

        var normalized = tag.Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.StartsWith(ScopeTagPrefix, StringComparison.OrdinalIgnoreCase)) {
            tagKey = "scope";
            return true;
        }
        if (normalized.StartsWith(OperationTagPrefix, StringComparison.OrdinalIgnoreCase)) {
            tagKey = "operation";
            return true;
        }
        if (normalized.StartsWith(EntityTagPrefix, StringComparison.OrdinalIgnoreCase)) {
            tagKey = "entity";
            return true;
        }
        if (normalized.StartsWith(RiskTagPrefix, StringComparison.OrdinalIgnoreCase)) {
            tagKey = "risk";
            return true;
        }
        if (normalized.StartsWith(RoutingTagPrefix, StringComparison.OrdinalIgnoreCase)) {
            tagKey = "routing";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the taxonomy tag key and non-empty value for a routing taxonomy tag.
    /// Returns <c>false</c> for malformed tags such as <c>risk:</c>.
    /// </summary>
    public static bool TryGetTagKeyValue(string? tag, out string tagKey, out string tagValue) {
        tagKey = string.Empty;
        tagValue = string.Empty;
        if (!TryGetTagKey(tag, out tagKey)) {
            return false;
        }

        var normalized = tag!.Trim();
        var separator = normalized.IndexOf(':');
        if (separator < 0 || separator == normalized.Length - 1) {
            return false;
        }

        var value = normalized.Substring(separator + 1).Trim();
        if (value.Length == 0) {
            return false;
        }

        tagValue = value;
        return true;
    }
}
