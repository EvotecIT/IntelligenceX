using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured continuation hint for dependency-blocked background scheduler work.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerContinuationHintDto {
    /// <summary>
    /// Thread identifier for the blocked continuation.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Best-effort normalized next action for the blocked continuation.
    /// </summary>
    public string NextAction { get; init; } = string.Empty;

    /// <summary>
    /// Best-effort normalized recovery reason for the blocked continuation.
    /// </summary>
    public string RecoveryReason { get; init; } = string.Empty;

    /// <summary>
    /// Helper tools currently associated with the blocked continuation.
    /// </summary>
    public string[] HelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Minimal runtime input argument names inferred for the blocked continuation.
    /// </summary>
    public string[] InputArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Structured request hints that can help a client continue blocked work without parsing free-form status text.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerContinuationRequestDto[] SuggestedRequests { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestDto>();

    /// <summary>
    /// Human-readable continuation hint summary.
    /// </summary>
    public string StatusSummary { get; init; } = string.Empty;
}
