using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Recent scheduler activity sample for the active Chat runtime.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerActivityDto {
    /// <summary>
    /// Best-effort UTC ticks when the outcome was recorded.
    /// </summary>
    public long RecordedUtcTicks { get; init; }

    /// <summary>
    /// Normalized scheduler outcome label.
    /// </summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>
    /// Thread that owned the scheduled work item.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Background work item identifier.
    /// </summary>
    public string ItemId { get; init; } = string.Empty;

    /// <summary>
    /// Tool executed for the scheduled work item.
    /// </summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// Runtime reason label associated with the outcome.
    /// </summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// Number of tool outputs returned for the scheduled execution.
    /// </summary>
    public int OutputCount { get; init; }

    /// <summary>
    /// Best-effort failure detail for non-success outcomes.
    /// </summary>
    public string FailureDetail { get; init; } = string.Empty;
}
