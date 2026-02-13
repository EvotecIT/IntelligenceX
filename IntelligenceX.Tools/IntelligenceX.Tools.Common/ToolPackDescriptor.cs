namespace IntelligenceX.Tools.Common;

/// <summary>
/// Metadata describing a tool pack.
/// </summary>
public sealed record ToolPackDescriptor {
    /// <summary>
    /// Stable id (used for policy and UI).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-friendly name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Capability tier.
    /// </summary>
    public required ToolCapabilityTier Tier { get; init; }

    /// <summary>
    /// True when the pack includes tools that can change system state.
    /// </summary>
    public bool IsDangerous { get; init; }

    /// <summary>
    /// Optional description used in UI/help.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional provenance classification for pack origin (for example: builtin/open_source/closed_source).
    /// </summary>
    public string? SourceKind { get; init; }
}
