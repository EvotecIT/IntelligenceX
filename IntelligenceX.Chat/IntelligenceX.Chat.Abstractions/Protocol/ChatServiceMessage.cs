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
[JsonDerivedType(typeof(ProfileListMessage), "profile_list")]
[JsonDerivedType(typeof(ModelListMessage), "model_list")]
[JsonDerivedType(typeof(ModelFavoritesMessage), "model_favorites")]
[JsonDerivedType(typeof(InvokeToolResultMessage), "invoke_tool_result")]
[JsonDerivedType(typeof(ChatStatusMessage), "chat_status")]
[JsonDerivedType(typeof(ChatDeltaMessage), "chat_delta")]
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
    /// Status identifier (e.g. thinking, tool_running, tool_completed).
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
}
