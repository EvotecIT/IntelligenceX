using System;

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
