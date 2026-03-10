using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Read-only engine parity summary for a runtime capability family.
/// </summary>
public sealed record SessionCapabilityParityEntryDto {
    /// <summary>
    /// Stable upstream engine identifier.
    /// </summary>
    public required string EngineId { get; init; }

    /// <summary>
    /// Canonical IntelligenceX pack id associated with the engine surface.
    /// </summary>
    public required string PackId { get; init; }

    /// <summary>
    /// Parity status for this engine surface.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Whether the upstream source/capability contract was available for inspection.
    /// </summary>
    public required bool SourceAvailable { get; init; }

    /// <summary>
    /// Number of registered tools currently exposed for the mapped pack.
    /// </summary>
    public int RegisteredToolCount { get; init; }

    /// <summary>
    /// Number of expected read-only capabilities discovered upstream for this parity slice.
    /// </summary>
    public int ExpectedCapabilityCount { get; init; }

    /// <summary>
    /// Number of discovered upstream capabilities already surfaced through live IX tools.
    /// </summary>
    public int SurfacedCapabilityCount { get; init; }

    /// <summary>
    /// Number of missing upstream read-only capabilities in this parity slice.
    /// </summary>
    public int MissingCapabilityCount { get; init; }

    /// <summary>
    /// Canonical identifiers for missing capabilities in this parity slice.
    /// </summary>
    public string[] MissingCapabilities { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Optional operator-facing note for governed or scoped parity states.
    /// </summary>
    public string? Note { get; init; }
}
