using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares routing/selection metadata used by host-side chat orchestration.
/// </summary>
public sealed class ToolRoutingContract {
    /// <summary>
    /// Default contract id for IX routing metadata.
    /// </summary>
    public const string DefaultContractId = "ix.tool-routing.v1";

    /// <summary>
    /// True when this definition participates in host-side routing metadata.
    /// </summary>
    public bool IsRoutingAware { get; set; } = true;

    /// <summary>
    /// Stable routing contract identifier.
    /// </summary>
    public string RoutingContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// Routing metadata source (<c>explicit</c> or <c>inferred</c>).
    /// </summary>
    public string RoutingSource { get; set; } = ToolRoutingTaxonomy.SourceExplicit;

    /// <summary>
    /// Optional normalized pack identifier for this tool.
    /// </summary>
    public string PackId { get; set; } = string.Empty;

    /// <summary>
    /// Routing role for orchestrator behavior (for example <c>operational</c>, <c>pack_info</c>, <c>environment_discover</c>).
    /// </summary>
    public string Role { get; set; } = ToolRoutingTaxonomy.RoleOperational;

    /// <summary>
    /// Optional primary scope where the tool operates.
    /// </summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>
    /// Optional primary operation kind for the tool.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// Optional primary entity class handled by the tool.
    /// </summary>
    public string Entity { get; set; } = string.Empty;

    /// <summary>
    /// Optional relative risk profile for the tool.
    /// </summary>
    public string Risk { get; set; } = string.Empty;

    /// <summary>
    /// Optional domain intent family token (for example ad_domain/public_domain).
    /// </summary>
    public string DomainIntentFamily { get; set; } = string.Empty;

    /// <summary>
    /// Optional action id used when selecting this domain intent family.
    /// </summary>
    public string DomainIntentActionId { get; set; } = string.Empty;

    /// <summary>
    /// Optional user-facing display label for this domain intent family.
    /// </summary>
    public string DomainIntentFamilyDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Optional natural-language reply example for selecting this domain intent family.
    /// </summary>
    public string DomainIntentFamilyReplyExample { get; set; } = string.Empty;

    /// <summary>
    /// Optional user-facing clarification description for this domain intent family.
    /// </summary>
    public string DomainIntentFamilyChoiceDescription { get; set; } = string.Empty;

    /// <summary>
    /// Optional normalized domain-intent signal tokens associated with this tool.
    /// </summary>
    public IReadOnlyList<string> DomainSignalTokens { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Indicates fallback requires selector-like arguments.
    /// </summary>
    public bool RequiresSelectionForFallback { get; set; }

    /// <summary>
    /// Selector argument names required for fallback execution.
    /// </summary>
    public IReadOnlyList<string> FallbackSelectionKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Hint argument names preferred for fallback execution.
    /// </summary>
    public IReadOnlyList<string> FallbackHintKeys { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsRoutingAware) {
            return;
        }

        if (string.IsNullOrWhiteSpace(RoutingContractId)) {
            throw new InvalidOperationException("RoutingContractId is required when IsRoutingAware is enabled.");
        }

        var normalizedSource = (RoutingSource ?? string.Empty).Trim();
        if (normalizedSource.Length == 0 || !ToolRoutingTaxonomy.IsAllowedSource(normalizedSource)) {
            throw new InvalidOperationException(
                $"RoutingSource must be one of: {string.Join(", ", ToolRoutingTaxonomy.AllowedSources)}.");
        }

        var normalizedRole = (Role ?? string.Empty).Trim();
        if (normalizedRole.Length == 0 || !ToolRoutingTaxonomy.IsAllowedRole(normalizedRole)) {
            throw new InvalidOperationException(
                $"Role must be one of: {string.Join(", ", ToolRoutingTaxonomy.AllowedRoles)}.");
        }

        ValidateOptionalToken(
            Risk,
            ToolRoutingTaxonomy.IsAllowedRisk,
            "Risk",
            ToolRoutingTaxonomy.AllowedRisks);

        var normalizedFamily = (DomainIntentFamily ?? string.Empty).Trim();
        if (normalizedFamily.Length > 0
            && !ToolSelectionMetadata.TryNormalizeDomainIntentFamily(normalizedFamily, out _)) {
            throw new InvalidOperationException("DomainIntentFamily must be a normalized non-empty token when provided.");
        }

        if (normalizedFamily.Length > 0 && string.IsNullOrWhiteSpace(DomainIntentActionId)) {
            throw new InvalidOperationException("DomainIntentActionId is required when DomainIntentFamily is provided.");
        }

        if (normalizedFamily.Length == 0
            && (!string.IsNullOrWhiteSpace(DomainIntentFamilyDisplayName)
                || !string.IsNullOrWhiteSpace(DomainIntentFamilyReplyExample)
                || !string.IsNullOrWhiteSpace(DomainIntentFamilyChoiceDescription))) {
            throw new InvalidOperationException(
                "DomainIntentFamily is required when domain-intent family presentation metadata is provided.");
        }

        if (RequiresSelectionForFallback && (FallbackSelectionKeys is null || FallbackSelectionKeys.Count == 0)) {
            throw new InvalidOperationException(
                "FallbackSelectionKeys must include at least one argument when RequiresSelectionForFallback is enabled.");
        }
    }

    private static void ValidateOptionalToken(
        string? value,
        Func<string?, bool> validator,
        string propertyName,
        IReadOnlyList<string> allowedValues) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        if (validator(normalized)) {
            return;
        }

        throw new InvalidOperationException(
            $"{propertyName} must be one of: {string.Join(", ", allowedValues)}.");
    }
}
