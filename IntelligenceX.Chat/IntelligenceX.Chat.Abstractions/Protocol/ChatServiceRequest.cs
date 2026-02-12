using System.Text.Json.Serialization;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Base type for all service requests (NDJSON frames from client to service).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(HelloRequest), "hello")]
[JsonDerivedType(typeof(EnsureLoginRequest), "ensure_login")]
[JsonDerivedType(typeof(StartChatGptLoginRequest), "chatgpt_login_start")]
[JsonDerivedType(typeof(ChatGptLoginPromptResponseRequest), "chatgpt_login_prompt_response")]
[JsonDerivedType(typeof(CancelChatGptLoginRequest), "chatgpt_login_cancel")]
[JsonDerivedType(typeof(ListToolsRequest), "list_tools")]
[JsonDerivedType(typeof(ChatRequest), "chat")]
public abstract record ChatServiceRequest {
    /// <summary>
    /// Correlation id for the request.
    /// </summary>
    public required string RequestId { get; init; }
}

/// <summary>
/// Requests basic server information.
/// </summary>
public sealed record HelloRequest : ChatServiceRequest;

/// <summary>
/// Requests that the service verifies an existing cached login.
/// </summary>
public sealed record EnsureLoginRequest : ChatServiceRequest {
    /// <summary>
    /// When true, forces an interactive login flow.
    /// </summary>
    public bool ForceLogin { get; init; }
}

/// <summary>
/// Starts a ChatGPT OAuth login flow.
/// </summary>
public sealed record StartChatGptLoginRequest : ChatServiceRequest {
    /// <summary>
    /// Whether the service should try to use a local listener for the OAuth redirect.
    /// When false, the service will always prompt for a redirect URL/code via the client.
    /// </summary>
    public bool UseLocalListener { get; init; } = true;

    /// <summary>
    /// Optional timeout in seconds for the login flow (default: 180).
    /// </summary>
    public int TimeoutSeconds { get; init; } = 180;
}

/// <summary>
/// Provides a response to an interactive login prompt.
/// </summary>
public sealed record ChatGptLoginPromptResponseRequest : ChatServiceRequest {
    /// <summary>
    /// Login flow id returned by <see cref="StartChatGptLoginRequest"/>.
    /// </summary>
    public required string LoginId { get; init; }
    /// <summary>
    /// Prompt id emitted by the service.
    /// </summary>
    public required string PromptId { get; init; }
    /// <summary>
    /// User-provided input (redirect URL or authorization code).
    /// </summary>
    public required string Input { get; init; }
}

/// <summary>
/// Cancels an in-progress ChatGPT OAuth login flow.
/// </summary>
public sealed record CancelChatGptLoginRequest : ChatServiceRequest {
    /// <summary>
    /// Login flow id returned by <see cref="StartChatGptLoginRequest"/>.
    /// </summary>
    public required string LoginId { get; init; }
}

/// <summary>
/// Requests the list of registered tool definitions.
/// </summary>
public sealed record ListToolsRequest : ChatServiceRequest;

/// <summary>
/// Submits a text chat message for the given (optional) thread.
/// </summary>
public sealed record ChatRequest : ChatServiceRequest {
    /// <summary>
    /// Optional thread id. When omitted, the service will create a new thread.
    /// </summary>
    public string? ThreadId { get; init; }
    /// <summary>
    /// Prompt text.
    /// </summary>
    public required string Text { get; init; }
    /// <summary>
    /// Optional request options.
    /// </summary>
    public ChatRequestOptions? Options { get; init; }
}

/// <summary>
/// Options controlling chat behavior and tool execution.
/// </summary>
public sealed record ChatRequestOptions {
    /// <summary>
    /// Optional model override.
    /// </summary>
    public string? Model { get; init; }
    /// <summary>
    /// Max tool-call rounds per user message.
    /// </summary>
    public int MaxToolRounds { get; init; } = 3;
    /// <summary>
    /// Whether to execute tool calls in parallel when possible.
    /// </summary>
    public bool ParallelTools { get; init; } = true;
    /// <summary>
    /// Optional per-turn timeout in seconds (null means use service default; 0 means no explicit timeout).
    /// </summary>
    public int? TurnTimeoutSeconds { get; init; }
    /// <summary>
    /// Optional per-tool timeout in seconds (null means use service default; 0 means no explicit timeout).
    /// </summary>
    public int? ToolTimeoutSeconds { get; init; }
}
