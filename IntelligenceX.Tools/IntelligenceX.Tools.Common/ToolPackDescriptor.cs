using System;
using System.Collections.Generic;

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
    /// Optional legacy aliases accepted for this pack id.
    /// Aliases should normalize back to the same canonical id as <see cref="Id"/>.
    /// </summary>
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();

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

    /// <summary>
    /// Optional stable engine identifier (for example: adplayground/computerx/eventviewerx).
    /// </summary>
    public string? EngineId { get; init; }

    /// <summary>
    /// Optional normalized capability tags advertised by the pack.
    /// </summary>
    public IReadOnlyList<string> CapabilityTags { get; init; } = Array.Empty<string>();
}
