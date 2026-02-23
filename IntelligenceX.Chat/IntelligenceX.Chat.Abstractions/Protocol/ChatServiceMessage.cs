using System;
using System.Text.Json.Serialization;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Base type for all service messages (NDJSON frames from service to client).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(ErrorMessage), "error")]
[JsonDerivedType(typeof(AckMessage), "ack")]
[JsonDerivedType(typeof(HelloMessage), "hello")]
[JsonDerivedType(typeof(LoginStatusMessage), "login_status")]
[JsonDerivedType(typeof(ChatGptLoginStartedMessage), "chatgpt_login_started")]
[JsonDerivedType(typeof(ChatGptLoginUrlMessage), "chatgpt_login_url")]
[JsonDerivedType(typeof(ChatGptLoginPromptMessage), "chatgpt_login_prompt")]
[JsonDerivedType(typeof(ChatGptLoginCompletedMessage), "chatgpt_login_completed")]
[JsonDerivedType(typeof(ToolListMessage), "tool_list")]
[JsonDerivedType(typeof(ToolHealthMessage), "tool_health")]
[JsonDerivedType(typeof(ProfileListMessage), "profile_list")]
[JsonDerivedType(typeof(ModelListMessage), "model_list")]
[JsonDerivedType(typeof(ModelFavoritesMessage), "model_favorites")]
[JsonDerivedType(typeof(InvokeToolResultMessage), "invoke_tool_result")]
[JsonDerivedType(typeof(ChatStatusMessage), "chat_status")]
[JsonDerivedType(typeof(ChatDeltaMessage), "chat_delta")]
[JsonDerivedType(typeof(ChatAssistantProvisionalMessage), "assistant_provisional")]
[JsonDerivedType(typeof(ChatInterimResultMessage), "chat_interim_result")]
[JsonDerivedType(typeof(ChatMetricsMessage), "chat_metrics")]
[JsonDerivedType(typeof(ChatResultMessage), "chat_result")]
public abstract record ChatServiceMessage {
    /// <summary>
    /// Message kind.
    /// </summary>
    public required ChatServiceMessageKind Kind { get; init; }
    /// <summary>
    /// Optional request correlation id.
    /// </summary>
    public string? RequestId { get; init; }
}

/// <summary>
/// Error response message.
/// </summary>
public sealed record ErrorMessage : ChatServiceMessage {
    /// <summary>
    /// Error message.
    /// </summary>
    public required string Error { get; init; }
    /// <summary>
    /// Optional stable error code.
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>
/// Generic acknowledgement response.
/// </summary>
public sealed record AckMessage : ChatServiceMessage {
    /// <summary>
    /// Whether the request was accepted.
    /// </summary>
    public required bool Ok { get; init; }
    /// <summary>
    /// Optional message.
    /// </summary>
    public string? Message { get; init; }
    /// <summary>
    /// Optional stable error code when <see cref="Ok"/> is false.
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>
/// Response message for <see cref="HelloRequest"/>.
/// </summary>
public sealed record HelloMessage : ChatServiceMessage {
    /// <summary>
    /// Service name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Service version.
    /// </summary>
    public required string Version { get; init; }
    /// <summary>
    /// Process id as string (for diagnostics).
    /// </summary>
    public required string ProcessId { get; init; }
    /// <summary>
    /// Optional session policy banner data that a UI can render.
    /// </summary>
    public SessionPolicyDto? Policy { get; init; }
}

/// <summary>
/// Response message for <see cref="EnsureLoginRequest"/>.
/// </summary>
public sealed record LoginStatusMessage : ChatServiceMessage {
    /// <summary>
    /// Whether a cached login is available.
    /// </summary>
    public required bool IsAuthenticated { get; init; }
    /// <summary>
    /// Optional account id when authenticated.
    /// </summary>
    public string? AccountId { get; init; }
    /// <summary>
    /// Optional native ChatGPT usage/limit snapshot for the authenticated account.
    /// </summary>
    public NativeUsageSnapshotDto? NativeUsage { get; init; }
}

/// <summary>
/// Native ChatGPT usage snapshot payload (best-effort).
/// </summary>
public sealed record NativeUsageSnapshotDto {
    /// <summary>
    /// Account id associated with the snapshot.
    /// </summary>
    public string? AccountId { get; init; }
    /// <summary>
    /// Account email when available.
    /// </summary>
    public string? Email { get; init; }
    /// <summary>
    /// Plan identifier (for example Plus/Pro/Team variant names).
    /// </summary>
    public string? PlanType { get; init; }
    /// <summary>
    /// Primary chat rate-limit status.
    /// </summary>
    public NativeRateLimitStatusDto? RateLimit { get; init; }
    /// <summary>
    /// Code-review specific rate-limit status.
    /// </summary>
    public NativeRateLimitStatusDto? CodeReviewRateLimit { get; init; }
    /// <summary>
    /// Credits snapshot when exposed by provider endpoints.
    /// </summary>
    public NativeCreditsSnapshotDto? Credits { get; init; }
    /// <summary>
    /// UTC timestamp when this snapshot was collected.
    /// </summary>
    public DateTime? RetrievedAtUtc { get; init; }
    /// <summary>
    /// Snapshot source (for example live or cache).
    /// </summary>
    public string? Source { get; init; }
}

/// <summary>
/// Native ChatGPT rate-limit status payload.
/// </summary>
public sealed record NativeRateLimitStatusDto {
    /// <summary>
    /// Whether requests are currently allowed.
    /// </summary>
    public bool Allowed { get; init; }
    /// <summary>
    /// Whether the provider reports the limit as reached.
    /// </summary>
    public bool LimitReached { get; init; }
    /// <summary>
    /// Primary rate-limit window details.
    /// </summary>
    public NativeRateLimitWindowDto? Primary { get; init; }
    /// <summary>
    /// Secondary/auxiliary rate-limit window details.
    /// </summary>
    public NativeRateLimitWindowDto? Secondary { get; init; }
}

/// <summary>
/// Native ChatGPT rate-limit window.
/// </summary>
public sealed record NativeRateLimitWindowDto {
    /// <summary>
    /// Consumed percentage for the window.
    /// </summary>
    public double? UsedPercent { get; init; }
    /// <summary>
    /// Window size in seconds.
    /// </summary>
    public long? LimitWindowSeconds { get; init; }
    /// <summary>
    /// Seconds remaining until reset.
    /// </summary>
    public long? ResetAfterSeconds { get; init; }
    /// <summary>
    /// Reset timestamp in Unix seconds.
    /// </summary>
    public long? ResetAtUnixSeconds { get; init; }
}

/// <summary>
/// Native ChatGPT credits snapshot payload.
/// </summary>
public sealed record NativeCreditsSnapshotDto {
    /// <summary>
    /// Whether credits are available for the account.
    /// </summary>
    public bool HasCredits { get; init; }
    /// <summary>
    /// Whether credits are unlimited.
    /// </summary>
    public bool Unlimited { get; init; }
    /// <summary>
    /// Credit balance when available.
    /// </summary>
    public double? Balance { get; init; }
    /// <summary>
    /// Approximate local message counters.
    /// </summary>
    public int[]? ApproxLocalMessages { get; init; }
    /// <summary>
    /// Approximate cloud message counters.
    /// </summary>
    public int[]? ApproxCloudMessages { get; init; }
}

/// <summary>
/// Response message for <see cref="StartChatGptLoginRequest"/>.
/// </summary>
public sealed record ChatGptLoginStartedMessage : ChatServiceMessage {
    /// <summary>
    /// Login flow id.
    /// </summary>
    public required string LoginId { get; init; }
}

/// <summary>
/// Event message containing the OAuth URL to open in a browser.
/// </summary>
public sealed record ChatGptLoginUrlMessage : ChatServiceMessage {
    /// <summary>
    /// Login flow id.
    /// </summary>
    public required string LoginId { get; init; }
    /// <summary>
    /// OAuth authorization URL.
    /// </summary>
    public required string Url { get; init; }
}

/// <summary>
/// Event message asking the client for a redirect URL or authorization code.
/// </summary>
public sealed record ChatGptLoginPromptMessage : ChatServiceMessage {
    /// <summary>
    /// Login flow id.
    /// </summary>
    public required string LoginId { get; init; }
    /// <summary>
    /// Prompt correlation id.
    /// </summary>
    public required string PromptId { get; init; }
    /// <summary>
    /// Prompt message.
    /// </summary>
    public required string Prompt { get; init; }
}

/// <summary>
/// Event message indicating login completion.
/// </summary>
public sealed record ChatGptLoginCompletedMessage : ChatServiceMessage {
    /// <summary>
    /// Login flow id.
    /// </summary>
    public required string LoginId { get; init; }
    /// <summary>
    /// Whether login completed successfully.
    /// </summary>
    public required bool Ok { get; init; }
    /// <summary>
    /// Optional error message when <see cref="Ok"/> is false.
    /// </summary>
    public string? Error { get; init; }
}

/// <summary>
/// Response message for <see cref="ListToolsRequest"/>.
/// </summary>
public sealed record ToolListMessage : ChatServiceMessage {
    /// <summary>
    /// Tool definitions registered by the service.
    /// </summary>
    public ToolDefinitionDto[] Tools { get; init; } = Array.Empty<ToolDefinitionDto>();
}

/// <summary>
/// Response message for <see cref="InvokeToolRequest"/>.
/// </summary>
public sealed record InvokeToolResultMessage : ChatServiceMessage {
    /// <summary>
    /// Tool name that was invoked.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Tool output envelope/result.
    /// </summary>
    public required ToolOutputDto Output { get; init; }
}

/// <summary>
/// Status/progress event emitted during a chat request (thinking, running tools, etc.).
/// </summary>
public sealed record ChatStatusMessage : ChatServiceMessage {
    /// <summary>
    /// Active thread id for the status event.
    /// </summary>
    public required string ThreadId { get; init; }
    /// <summary>
    /// Status identifier (e.g. thinking, tool_running, tool_recovered, tool_completed).
    /// </summary>
    public required string Status { get; init; }
    /// <summary>
    /// Optional tool name for tool-related statuses.
    /// </summary>
    public string? ToolName { get; init; }
    /// <summary>
    /// Optional tool call id for tool-related statuses.
    /// </summary>
    public string? ToolCallId { get; init; }
    /// <summary>
    /// Optional tool execution duration (milliseconds) for tool_completed.
    /// </summary>
    public long? DurationMs { get; init; }
    /// <summary>
    /// Optional short message for UI display.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Streaming delta event for a chat request.
/// </summary>
public sealed record ChatDeltaMessage : ChatServiceMessage {
    /// <summary>
    /// Active thread id for the streamed delta.
    /// </summary>
    public required string ThreadId { get; init; }
    /// <summary>
    /// Delta text fragment.
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// Streaming provisional assistant event for a chat request.
/// </summary>
public sealed record ChatAssistantProvisionalMessage : ChatServiceMessage {
    /// <summary>
    /// Active thread id for the streamed provisional fragment.
    /// </summary>
    public required string ThreadId { get; init; }
    /// <summary>
    /// Provisional text fragment.
    /// </summary>
    public required string Text { get; init; }
}

/// <summary>
/// Interim assistant result snapshot emitted before final synthesis.
/// </summary>
public sealed record ChatInterimResultMessage : ChatServiceMessage {
    /// <summary>
    /// Active thread id for the interim snapshot.
    /// </summary>
    public required string ThreadId { get; init; }
    /// <summary>
    /// Interim assistant text.
    /// </summary>
    public required string Text { get; init; }
    /// <summary>
    /// Optional stage marker (for example review_draft/final_draft).
    /// </summary>
    public string? Stage { get; init; }
    /// <summary>
    /// Optional tool-call count at interim capture.
    /// </summary>
    public int? ToolCallsCount { get; init; }
    /// <summary>
    /// Optional tool-output count at interim capture.
    /// </summary>
    public int? ToolOutputsCount { get; init; }
}

/// <summary>
/// Structured timeline event captured for a turn.
/// </summary>
public sealed record TurnTimelineEventDto {
    private readonly DateTime _atUtc;

    /// <summary>
    /// Status code emitted for this timeline event.
    /// </summary>
    public required string Status { get; init; }
    /// <summary>
    /// Optional tool name associated with this event.
    /// </summary>
    public string? ToolName { get; init; }
    /// <summary>
    /// Optional tool call id associated with this event.
    /// </summary>
    public string? ToolCallId { get; init; }
    /// <summary>
    /// Optional duration in milliseconds for this event.
    /// </summary>
    public long? DurationMs { get; init; }
    /// <summary>
    /// Optional event message for UI display.
    /// </summary>
    public string? Message { get; init; }
    /// <summary>
    /// UTC timestamp when the event was captured.
    /// </summary>
    public DateTime AtUtc {
        get => _atUtc;
        init => _atUtc = NormalizeUtc(value);
    }

    private static DateTime NormalizeUtc(DateTime value) {
        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

/// <summary>
/// Final response message for a chat request.
/// </summary>
public sealed record ChatResultMessage : ChatServiceMessage {
    /// <summary>
    /// Active thread id.
    /// </summary>
    public required string ThreadId { get; init; }
    /// <summary>
    /// Final assistant text.
    /// </summary>
    public required string Text { get; init; }
    /// <summary>
    /// Optional tool calls and outputs.
    /// </summary>
    public ToolRunDto? Tools { get; init; }
    /// <summary>
    /// Optional structured timeline captured for this completed turn.
    /// </summary>
    public TurnTimelineEventDto[]? TurnTimelineEvents { get; init; }
}
