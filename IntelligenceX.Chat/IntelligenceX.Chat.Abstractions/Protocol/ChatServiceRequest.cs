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
[JsonDerivedType(typeof(ListProfilesRequest), "list_profiles")]
[JsonDerivedType(typeof(SetProfileRequest), "set_profile")]
[JsonDerivedType(typeof(ListModelsRequest), "list_models")]
[JsonDerivedType(typeof(ListModelFavoritesRequest), "list_model_favorites")]
[JsonDerivedType(typeof(SetModelFavoriteRequest), "set_model_favorite")]
[JsonDerivedType(typeof(InvokeToolRequest), "invoke_tool")]
[JsonDerivedType(typeof(CancelChatRequest), "chat_cancel")]
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
/// Requests the list of saved runtime profiles.
/// </summary>
public sealed record ListProfilesRequest : ChatServiceRequest;

/// <summary>
/// Switches the active runtime profile for this session.
/// </summary>
public sealed record SetProfileRequest : ChatServiceRequest {
    /// <summary>
    /// Profile name to load.
    /// </summary>
    public required string ProfileName { get; init; }
    /// <summary>
    /// When true, clears the active thread so history isn't mixed across profiles.
    /// </summary>
    public bool NewThread { get; init; } = true;
}

/// <summary>
/// Requests the list of available models for the current provider/profile.
/// </summary>
public sealed record ListModelsRequest : ChatServiceRequest {
    /// <summary>
    /// When true, bypasses any server-side cache (best-effort).
    /// </summary>
    public bool ForceRefresh { get; init; }
}

/// <summary>
/// Requests the list of favorite models for the active profile.
/// </summary>
public sealed record ListModelFavoritesRequest : ChatServiceRequest;

/// <summary>
/// Adds or removes a model from favorites for the active profile.
/// </summary>
public sealed record SetModelFavoriteRequest : ChatServiceRequest {
    /// <summary>
    /// Model name to update.
    /// </summary>
    public required string Model { get; init; }
    /// <summary>
    /// When true, adds to favorites; when false, removes from favorites.
    /// </summary>
    public bool IsFavorite { get; init; } = true;
}

/// <summary>
/// Invokes a specific registered tool directly (outside model chat flow).
/// </summary>
public sealed record InvokeToolRequest : ChatServiceRequest {
    /// <summary>
    /// Tool name to invoke.
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Optional raw JSON object string for tool arguments.
    /// </summary>
    public string? ArgumentsJson { get; init; }

    /// <summary>
    /// Optional tool timeout override in seconds (0 = no timeout; null = service default).
    /// </summary>
    public int? ToolTimeoutSeconds { get; init; }
}

/// <summary>
/// Cancels an in-progress chat request.
/// </summary>
public sealed record CancelChatRequest : ChatServiceRequest {
    /// <summary>
    /// Request id of the chat turn to cancel.
    /// </summary>
    public required string ChatRequestId { get; init; }
}

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
    public int MaxToolRounds { get; init; } = 24;
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
    /// <summary>
    /// Optional tool names to disable for this request.
    /// </summary>
    public string[]? DisabledTools { get; init; }
    /// <summary>
    /// Optional override for weighted tool routing (null means service default).
    /// </summary>
    public bool? WeightedToolRouting { get; init; }
    /// <summary>
    /// Optional cap for how many candidate tools are exposed to the model per turn.
    /// Null/0 means service-selected default.
    /// </summary>
    public int? MaxCandidateTools { get; init; }
}
