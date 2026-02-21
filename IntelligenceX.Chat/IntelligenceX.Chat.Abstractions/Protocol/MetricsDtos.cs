using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Per-turn metrics emitted by the service for a chat request.
/// </summary>
public sealed record ChatMetricsMessage : ChatServiceMessage {
    /// <summary>
    /// Active thread id for the metrics event.
    /// </summary>
    public required string ThreadId { get; init; }

    /// <summary>
    /// Turn start timestamp (UTC).
    /// </summary>
    public required DateTime StartedAtUtc { get; init; }
    /// <summary>
    /// Timestamp of the first streamed delta (UTC) when streaming is enabled.
    /// </summary>
    public DateTime? FirstDeltaAtUtc { get; init; }
    /// <summary>
    /// Turn completion timestamp (UTC).
    /// </summary>
    public required DateTime CompletedAtUtc { get; init; }
    /// <summary>
    /// Total elapsed time for the turn (milliseconds).
    /// </summary>
    public long DurationMs { get; init; }
    /// <summary>
    /// Time-to-first-token (milliseconds) when streaming is enabled.
    /// </summary>
    public long? TtftMs { get; init; }

    /// <summary>
    /// Token usage details when available.
    /// </summary>
    public TokenUsageDto? Usage { get; init; }
    /// <summary>
    /// Total number of tool calls in the turn.
    /// </summary>
    public int ToolCallsCount { get; init; }
    /// <summary>
    /// Number of tool-call rounds executed in the turn.
    /// </summary>
    public int ToolRounds { get; init; }
    /// <summary>
    /// Number of tool calls where view-projection arguments were auto-reset and retried.
    /// </summary>
    public int ProjectionFallbackCount { get; init; }
    /// <summary>
    /// Optional per-tool error-code counts for this turn.
    /// </summary>
    public IReadOnlyList<ToolErrorMetricDto>? ToolErrors { get; init; }
    /// <summary>
    /// Effective model identifier used for the turn.
    /// </summary>
    public string? Model { get; init; }
    /// <summary>
    /// Requested model override for the turn, when one was provided.
    /// </summary>
    public string? RequestedModel { get; init; }
    /// <summary>
    /// Runtime transport used for the turn (native/appserver/compatible-http/copilot-cli).
    /// </summary>
    public string? Transport { get; init; }
    /// <summary>
    /// Sanitized runtime endpoint host (when available).
    /// </summary>
    public string? EndpointHost { get; init; }

    /// <summary>
    /// Outcome identifier (ok, canceled, error).
    /// </summary>
    public required string Outcome { get; init; }
    /// <summary>
    /// Optional error code when outcome is error/canceled.
    /// </summary>
    public string? ErrorCode { get; init; }
}

/// <summary>
/// Token usage DTO (best-effort; availability depends on provider).
/// </summary>
public sealed record TokenUsageDto {
    /// <summary>
    /// Prompt/input tokens.
    /// </summary>
    public long? PromptTokens { get; init; }
    /// <summary>
    /// Completion/output tokens.
    /// </summary>
    public long? CompletionTokens { get; init; }
    /// <summary>
    /// Total tokens.
    /// </summary>
    public long? TotalTokens { get; init; }
    /// <summary>
    /// Cached prompt tokens when the provider exposes them.
    /// </summary>
    public long? CachedPromptTokens { get; init; }
    /// <summary>
    /// Reasoning tokens when the provider exposes them.
    /// </summary>
    public long? ReasoningTokens { get; init; }
}

/// <summary>
/// Per-tool error taxonomy entry for a single turn.
/// </summary>
public sealed record ToolErrorMetricDto {
    /// <summary>
    /// Tool name.
    /// </summary>
    public required string ToolName { get; init; }
    /// <summary>
    /// Stable error code observed for the tool output.
    /// </summary>
    public required string ErrorCode { get; init; }
    /// <summary>
    /// Number of occurrences for this tool/error-code pair in the turn.
    /// </summary>
    public int Count { get; init; }
}
