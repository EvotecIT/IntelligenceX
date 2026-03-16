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
}
