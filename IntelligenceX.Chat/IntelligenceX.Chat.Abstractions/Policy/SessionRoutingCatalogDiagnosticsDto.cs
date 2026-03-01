using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured routing-catalog diagnostics exposed in hello/session policy payloads.
/// </summary>
public sealed record SessionRoutingCatalogDiagnosticsDto {
    /// <summary>
    /// Total tool definitions observed in registry.
    /// </summary>
    public int TotalTools { get; init; }

    /// <summary>
    /// Tools explicitly participating in routing metadata.
    /// </summary>
    public int RoutingAwareTools { get; init; }

    /// <summary>
    /// Tools missing routing contracts.
    /// </summary>
    public int MissingRoutingContractTools { get; init; }

    /// <summary>
    /// Tools declaring a non-empty domain intent family.
    /// </summary>
    public int DomainFamilyTools { get; init; }

    /// <summary>
    /// Tools where a domain family is inferred but not declared in routing metadata.
    /// </summary>
    public int ExpectedDomainFamilyMissingTools { get; init; }

    /// <summary>
    /// Tools declaring family but missing action id.
    /// </summary>
    public int DomainFamilyMissingActionTools { get; init; }

    /// <summary>
    /// Tools declaring action id without family.
    /// </summary>
    public int ActionWithoutFamilyTools { get; init; }

    /// <summary>
    /// Number of families with conflicting action ids.
    /// </summary>
    public int FamilyActionConflictFamilies { get; init; }

    /// <summary>
    /// True when no catalog inconsistencies are detected.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Family/action distribution summary.
    /// </summary>
    public SessionRoutingFamilyActionSummaryDto[] FamilyActions { get; init; } = Array.Empty<SessionRoutingFamilyActionSummaryDto>();
}

/// <summary>
/// Family/action pair distribution entry for routing diagnostics.
/// </summary>
public sealed record SessionRoutingFamilyActionSummaryDto {
    /// <summary>
    /// Normalized domain intent family token.
    /// </summary>
    public required string Family { get; init; }

    /// <summary>
    /// Action id declared for the family.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Number of tools mapped to this family/action pair.
    /// </summary>
    public int ToolCount { get; init; }
}
