using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Compact per-thread background scheduler summary for the active Chat runtime.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerThreadSummaryDto {
    /// <summary>
    /// Thread identifier.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Number of queued work items in the thread.
    /// </summary>
    public int QueuedItemCount { get; init; }

    /// <summary>
    /// Number of queued work items in the thread that are blocked on prerequisite helpers.
    /// </summary>
    public int DependencyBlockedItemCount { get; init; }

    /// <summary>
    /// Number of ready work items in the thread.
    /// </summary>
    public int ReadyItemCount { get; init; }

    /// <summary>
    /// Number of running work items in the thread.
    /// </summary>
    public int RunningItemCount { get; init; }

    /// <summary>
    /// Number of completed work items in the thread.
    /// </summary>
    public int CompletedItemCount { get; init; }

    /// <summary>
    /// Number of read-only pending work items in the thread.
    /// </summary>
    public int PendingReadOnlyItemCount { get; init; }

    /// <summary>
    /// Number of unknown-mutability pending work items in the thread.
    /// </summary>
    public int PendingUnknownItemCount { get; init; }

    /// <summary>
    /// Recent evidence tool sample for the thread.
    /// </summary>
    public string[] RecentEvidenceTools { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Sample of helper tool names currently blocking queued dependent work in the thread.
    /// </summary>
    public string[] DependencyHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Number of prerequisite helper items satisfied from fresh cached read-only evidence.
    /// </summary>
    public int ReusedHelperItemCount { get; init; }

    /// <summary>
    /// Sample of helper tool names satisfied from fresh cached read-only evidence.
    /// </summary>
    public string[] ReusedHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Best-effort normalized helper-reuse policy names applied to cached prerequisite evidence in the thread.
    /// </summary>
    public string[] ReusedHelperPolicyNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Smallest observed cached-helper freshness age, in seconds, when available.
    /// </summary>
    public int? ReusedHelperFreshestAgeSeconds { get; init; }

    /// <summary>
    /// Largest observed cached-helper freshness age, in seconds, when available.
    /// </summary>
    public int? ReusedHelperOldestAgeSeconds { get; init; }

    /// <summary>
    /// Smallest observed cached-helper freshness window, in seconds, when available.
    /// </summary>
    public int? ReusedHelperFreshestTtlSeconds { get; init; }

    /// <summary>
    /// Largest observed cached-helper freshness window, in seconds, when available.
    /// </summary>
    public int? ReusedHelperOldestTtlSeconds { get; init; }

    /// <summary>
    /// Best-effort normalized recovery reason for dependency-blocked work in the thread.
    /// </summary>
    public string DependencyRecoveryReason { get; init; } = string.Empty;

    /// <summary>
    /// Best-effort normalized next action for dependency-blocked work in the thread.
    /// </summary>
    public string DependencyNextAction { get; init; } = string.Empty;

    /// <summary>
    /// Structured continuation hint for the blocked thread, when available.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerContinuationHintDto? ContinuationHint { get; init; }

    /// <summary>
    /// Helper tools whose previously attempted executions are still pending retry/cooldown.
    /// </summary>
    public string[] DependencyRetryCooldownHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Helper tools indicating runtime authentication context is required before dependent work can continue.
    /// </summary>
    public string[] DependencyAuthenticationHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Minimal runtime authentication argument names inferred for blocked dependent work in the thread.
    /// </summary>
    public string[] DependencyAuthenticationArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Setup helpers indicating additional setup or runtime-profile context is required before dependent work can continue.
    /// </summary>
    public string[] DependencySetupHelperToolNames { get; init; } = Array.Empty<string>();
}
