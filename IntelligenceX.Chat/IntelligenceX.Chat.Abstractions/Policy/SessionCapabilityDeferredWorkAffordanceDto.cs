using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured deferred-work capability advertised by the active runtime.
/// </summary>
public sealed record SessionCapabilityDeferredWorkAffordanceDto {
    /// <summary>
    /// Stable deferred-work capability identifier.
    /// </summary>
    public required string CapabilityId { get; init; }

    /// <summary>
    /// Human-friendly deferred-work capability label.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Best-effort runtime summary describing how this deferred-work capability is surfaced.
    /// </summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Stable availability mode for the capability (for example <c>pack_declared</c> or <c>runtime_scheduler</c>).
    /// </summary>
    public string AvailabilityMode { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the current runtime can execute or advance this capability through background scheduling.
    /// </summary>
    public bool SupportsBackgroundExecution { get; init; }

    /// <summary>
    /// Enabled pack ids contributing this capability.
    /// </summary>
    public string[] PackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Routing families associated with contributing packs when available.
    /// </summary>
    public string[] RoutingFamilies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Representative examples contributed by current pack/tool registration when available.
    /// </summary>
    public string[] RepresentativeExamples { get; init; } = Array.Empty<string>();
}
