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
    /// Total number of plugin-style tool sources visible to the session.
    /// </summary>
    public required int PluginCount { get; init; }

    /// <summary>
    /// Number of enabled plugin-style tool sources currently available.
    /// </summary>
    public required int EnabledPluginCount { get; init; }

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
    /// Normalized enabled plugin identifiers available to the session.
    /// </summary>
    public string[] EnabledPluginIds { get; init; } = Array.Empty<string>();

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

    /// <summary>
    /// Aggregate autonomy surface derived from enabled packs and orchestration contracts.
    /// </summary>
    public SessionCapabilityAutonomySummaryDto? Autonomy { get; init; }

    /// <summary>
    /// Phase-1 read-only parity inventory derived from live pack surfaces and upstream engines.
    /// </summary>
    public SessionCapabilityParityEntryDto[] ParityEntries { get; init; } = Array.Empty<SessionCapabilityParityEntryDto>();

    /// <summary>
    /// Number of parity entries that still require operator attention.
    /// </summary>
    public int ParityAttentionCount { get; init; }

    /// <summary>
    /// Total number of missing upstream read-only capabilities across the current parity inventory.
    /// </summary>
    public int ParityMissingCapabilityCount { get; init; }
}
