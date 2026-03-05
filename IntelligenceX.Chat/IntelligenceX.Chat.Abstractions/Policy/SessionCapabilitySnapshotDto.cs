using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured runtime capability inventory for the active Chat session.
/// </summary>
public sealed record SessionCapabilitySnapshotDto {
    /// <summary>
    /// Total number of registered tools visible to the session.
    /// </summary>
    public required int RegisteredTools { get; init; }

    /// <summary>
    /// Number of enabled tool packs currently available.
    /// </summary>
    public required int EnabledPackCount { get; init; }

    /// <summary>
    /// Whether the runtime currently has any tool capability available.
    /// </summary>
    public required bool ToolingAvailable { get; init; }

    /// <summary>
    /// Number of allowed filesystem roots currently configured.
    /// </summary>
    public required int AllowedRootCount { get; init; }

    /// <summary>
    /// Normalized enabled pack identifiers available to the session.
    /// </summary>
    public string[] EnabledPackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized routing families exposed by the active registry.
    /// </summary>
    public string[] RoutingFamilies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Family/action summaries exposed by the active registry.
    /// </summary>
    public SessionRoutingFamilyActionSummaryDto[] FamilyActions { get; init; } = Array.Empty<SessionRoutingFamilyActionSummaryDto>();

    /// <summary>
    /// Normalized reusable skill identifiers surfaced by the runtime.
    /// </summary>
    public string[] Skills { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Recently healthy tool names observed by the session.
    /// </summary>
    public string[] HealthyTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Best-effort runtime reachability mode for the current tool inventory.
    /// </summary>
    public string? RemoteReachabilityMode { get; init; }
}
