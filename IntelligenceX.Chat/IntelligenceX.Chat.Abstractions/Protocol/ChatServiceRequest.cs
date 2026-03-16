using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using IntelligenceX.Chat.Abstractions.Policy;

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
[JsonDerivedType(typeof(GetBackgroundSchedulerStatusRequest), "get_background_scheduler_status")]
[JsonDerivedType(typeof(SetBackgroundSchedulerStateRequest), "set_background_scheduler_state")]
[JsonDerivedType(typeof(SetBackgroundSchedulerMaintenanceWindowsRequest), "set_background_scheduler_maintenance_windows")]
[JsonDerivedType(typeof(SetBackgroundSchedulerBlockedPacksRequest), "set_background_scheduler_blocked_packs")]
[JsonDerivedType(typeof(SetBackgroundSchedulerBlockedThreadsRequest), "set_background_scheduler_blocked_threads")]
[JsonDerivedType(typeof(CheckToolHealthRequest), "check_tool_health")]
[JsonDerivedType(typeof(ListProfilesRequest), "list_profiles")]
[JsonDerivedType(typeof(SetProfileRequest), "set_profile")]
[JsonDerivedType(typeof(ApplyRuntimeSettingsRequest), "apply_runtime_settings")]
[JsonDerivedType(typeof(ListModelsRequest), "list_models")]
[JsonDerivedType(typeof(ListModelFavoritesRequest), "list_model_favorites")]
[JsonDerivedType(typeof(SetModelFavoriteRequest), "set_model_favorite")]
[JsonDerivedType(typeof(InvokeToolRequest), "invoke_tool")]
[JsonDerivedType(typeof(CancelChatRequest), "chat_cancel")]
[JsonDerivedType(typeof(ChatRequest), "chat")]
public abstract record ChatServiceRequest {
    private protected static string NormalizeBackgroundSchedulerMutationOperation(string? value, string parameterName) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized is "add" or "remove" or "replace" or "clear" or "reset") {
            return normalized;
        }

        throw new ArgumentException("Operation must be one of: add, remove, replace, clear, reset.", parameterName);
    }

    private protected static string[]? NormalizeBackgroundSchedulerMutationTargets(string[]? values) {
        if (values is not { Length: > 0 }) {
            return null;
        }

        var normalized = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .ToArray();

        return normalized.Length > 0 ? normalized : null;
    }

    private protected static void ValidateBackgroundSchedulerTargetMutationState(
        string operation,
        string[]? targets,
        string targetsParameterName) {
        if (operation is "add" or "remove" or "replace") {
            if (targets is not { Length: > 0 }) {
                throw new ArgumentException(
                    $"{targetsParameterName} must be provided for {operation} operations.",
                    targetsParameterName);
            }

            return;
        }

        if (targets is { Length: > 0 }) {
            throw new ArgumentException(
                $"{targetsParameterName} must be omitted for {operation} operations.",
                targetsParameterName);
        }
    }

    private protected static void ValidateBackgroundSchedulerTemporarySuppressionState(
        string operation,
        int? durationSeconds,
        bool untilNextMaintenanceWindow,
        bool untilNextMaintenanceWindowStart,
        string durationParameterName,
        string untilWindowParameterName,
        string untilWindowStartParameterName) {
        if (untilNextMaintenanceWindow && untilNextMaintenanceWindowStart) {
            throw new ArgumentException(
                "UntilNextMaintenanceWindow and UntilNextMaintenanceWindowStart cannot both be true.",
                untilWindowStartParameterName);
        }

        if (durationSeconds is not null && (untilNextMaintenanceWindow || untilNextMaintenanceWindowStart)) {
            throw new ArgumentException(
                "DurationSeconds cannot be combined with maintenance-window suppression flags.",
                durationParameterName);
        }

        if (operation == "add") {
            return;
        }

        if (durationSeconds is not null) {
            throw new ArgumentException(
                $"DurationSeconds is only supported for add operations, not {operation}.",
                durationParameterName);
        }

        if (untilNextMaintenanceWindow) {
            throw new ArgumentException(
                $"UntilNextMaintenanceWindow is only supported for add operations, not {operation}.",
                untilWindowParameterName);
        }

        if (untilNextMaintenanceWindowStart) {
            throw new ArgumentException(
                $"UntilNextMaintenanceWindowStart is only supported for add operations, not {operation}.",
                untilWindowStartParameterName);
        }
    }

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
/// Requests the current background scheduler status summary.
/// </summary>
public sealed record GetBackgroundSchedulerStatusRequest : ChatServiceRequest {
    private static int? ValidateBackgroundSchedulerStatusLimit(int? value, string parameterName) {
        if (value is not int requested) {
            return null;
        }

        if (requested < 0 || requested > ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must be between 0 and {ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems}.");
        }

        return requested;
    }

    private readonly int? _maxReadyThreadIds;
    private readonly int? _maxRunningThreadIds;
    private readonly int? _maxRecentActivity;
    private readonly int? _maxThreadSummaries;

    /// <summary>
    /// Optional thread scope. When set, scheduler counters and detail samples are constrained to the matching thread.
    /// </summary>
    public string? ThreadId { get; init; }

    /// <summary>
    /// Whether recent activity samples should be included in the response.
    /// </summary>
    public bool IncludeRecentActivity { get; init; } = true;

    /// <summary>
    /// Whether per-thread summary samples should be included in the response.
    /// </summary>
    public bool IncludeThreadSummaries { get; init; } = true;

    /// <summary>
    /// Optional cap for ready-thread id samples (0..<see cref="ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems"/>).
    /// </summary>
    public int? MaxReadyThreadIds {
        get => _maxReadyThreadIds;
        init => _maxReadyThreadIds = ValidateBackgroundSchedulerStatusLimit(value, nameof(MaxReadyThreadIds));
    }

    /// <summary>
    /// Optional cap for running-thread id samples (0..<see cref="ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems"/>).
    /// </summary>
    public int? MaxRunningThreadIds {
        get => _maxRunningThreadIds;
        init => _maxRunningThreadIds = ValidateBackgroundSchedulerStatusLimit(value, nameof(MaxRunningThreadIds));
    }

    /// <summary>
    /// Optional cap for recent scheduler activity samples (0..<see cref="ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems"/>).
    /// </summary>
    public int? MaxRecentActivity {
        get => _maxRecentActivity;
        init => _maxRecentActivity = ValidateBackgroundSchedulerStatusLimit(value, nameof(MaxRecentActivity));
    }

    /// <summary>
    /// Optional cap for per-thread scheduler summaries (0..<see cref="ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems"/>).
    /// </summary>
    public int? MaxThreadSummaries {
        get => _maxThreadSummaries;
        init => _maxThreadSummaries = ValidateBackgroundSchedulerStatusLimit(value, nameof(MaxThreadSummaries));
    }
}

/// <summary>
/// Applies a runtime pause/resume action to the background scheduler.
/// </summary>
public sealed record SetBackgroundSchedulerStateRequest : ChatServiceRequest {
    private static int? ValidatePositiveDurationSeconds(int? value, string parameterName) {
        if (value is not int requested) {
            return null;
        }

        if (requested < ChatRequestOptionLimits.MinPositiveTimeoutSeconds
            || requested > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }

        return requested;
    }

    private static string? NormalizeOptionalReason(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private int? _pauseSeconds;
    private string? _reason;

    /// <summary>
    /// When true, manually pauses the scheduler; when false, clears any active manual/auto pause state.
    /// </summary>
    public bool Paused { get; init; }

    /// <summary>
    /// Optional manual pause duration in seconds. Null means pause until explicitly resumed.
    /// </summary>
    public int? PauseSeconds {
        get => _pauseSeconds;
        init => _pauseSeconds = ValidatePositiveDurationSeconds(value, nameof(PauseSeconds));
    }

    /// <summary>
    /// Optional operator-facing reason to annotate the manual pause state.
    /// </summary>
    public string? Reason {
        get => _reason;
        init => _reason = NormalizeOptionalReason(value);
    }
}

/// <summary>
/// Applies a runtime maintenance-window policy update to the background scheduler.
/// </summary>
public sealed record SetBackgroundSchedulerMaintenanceWindowsRequest : ChatServiceRequest {
    /// <summary>
    /// Creates a maintenance-window mutation request with operation-aware validation.
    /// </summary>
    /// <param name="requestId">Correlation id for the request.</param>
    /// <param name="operation">Requested operation: add, remove, replace, clear, or reset.</param>
    /// <param name="windows">Optional maintenance window specs for targeted operations.</param>
    [JsonConstructor]
    [SetsRequiredMembers]
    public SetBackgroundSchedulerMaintenanceWindowsRequest(
        string requestId,
        string operation,
        string[]? windows = null) {
        RequestId = requestId;
        Operation = NormalizeBackgroundSchedulerMutationOperation(operation, nameof(Operation));
        Windows = NormalizeBackgroundSchedulerMutationTargets(windows);
        ValidateBackgroundSchedulerTargetMutationState(Operation, Windows, nameof(Windows));
    }

    /// <summary>
    /// Requested operation: add, remove, replace, clear, or reset.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Maintenance window specs used by add/remove/replace operations.
    /// </summary>
    public string[]? Windows { get; }
}

/// <summary>
/// Applies a runtime blocked-pack policy update to the background scheduler.
/// </summary>
public sealed record SetBackgroundSchedulerBlockedPacksRequest : ChatServiceRequest {
    private static int? ValidatePositiveDurationSeconds(int? value, string parameterName) {
        if (value is not int requested) {
            return null;
        }

        if (requested < ChatRequestOptionLimits.MinPositiveTimeoutSeconds
            || requested > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }

        return requested;
    }

    /// <summary>
    /// Creates a blocked-pack mutation request with operation-aware validation.
    /// </summary>
    /// <param name="requestId">Correlation id for the request.</param>
    /// <param name="operation">Requested operation: add, remove, replace, clear, or reset.</param>
    /// <param name="packIds">Optional pack ids for targeted operations.</param>
    /// <param name="durationSeconds">Optional temporary suppression duration in seconds for add operations.</param>
    /// <param name="untilNextMaintenanceWindow">Whether the add operation should suppress until the next relevant maintenance window ends.</param>
    /// <param name="untilNextMaintenanceWindowStart">Whether the add operation should suppress until the next relevant maintenance window starts.</param>
    [JsonConstructor]
    [SetsRequiredMembers]
    public SetBackgroundSchedulerBlockedPacksRequest(
        string requestId,
        string operation,
        string[]? packIds = null,
        int? durationSeconds = null,
        bool untilNextMaintenanceWindow = false,
        bool untilNextMaintenanceWindowStart = false) {
        RequestId = requestId;
        Operation = NormalizeBackgroundSchedulerMutationOperation(operation, nameof(Operation));
        PackIds = NormalizeBackgroundSchedulerMutationTargets(packIds);
        DurationSeconds = ValidatePositiveDurationSeconds(durationSeconds, nameof(DurationSeconds));
        UntilNextMaintenanceWindow = untilNextMaintenanceWindow;
        UntilNextMaintenanceWindowStart = untilNextMaintenanceWindowStart;

        ValidateBackgroundSchedulerTargetMutationState(Operation, PackIds, nameof(PackIds));
        ValidateBackgroundSchedulerTemporarySuppressionState(
            Operation,
            DurationSeconds,
            UntilNextMaintenanceWindow,
            UntilNextMaintenanceWindowStart,
            nameof(DurationSeconds),
            nameof(UntilNextMaintenanceWindow),
            nameof(UntilNextMaintenanceWindowStart));
    }

    /// <summary>
    /// Requested operation: add, remove, replace, clear, or reset.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Pack ids used by add/remove/replace operations.
    /// </summary>
    public string[]? PackIds { get; }

    /// <summary>
    /// Optional temporary suppression duration in seconds for add operations.
    /// Null means a persistent policy update.
    /// </summary>
    public int? DurationSeconds { get; }

    /// <summary>
    /// When true, add operations derive a temporary suppression that lasts until the next relevant maintenance window ends.
    /// </summary>
    public bool UntilNextMaintenanceWindow { get; }

    /// <summary>
    /// When true, add operations derive a temporary suppression that lasts until the next relevant maintenance window starts.
    /// </summary>
    public bool UntilNextMaintenanceWindowStart { get; }
}

/// <summary>
/// Applies a runtime blocked-thread policy update to the background scheduler.
/// </summary>
public sealed record SetBackgroundSchedulerBlockedThreadsRequest : ChatServiceRequest {
    private static int? ValidatePositiveDurationSeconds(int? value, string parameterName) {
        if (value is not int requested) {
            return null;
        }

        if (requested < ChatRequestOptionLimits.MinPositiveTimeoutSeconds
            || requested > ChatRequestOptionLimits.MaxTimeoutSeconds) {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"{parameterName} must be between {ChatRequestOptionLimits.MinPositiveTimeoutSeconds} and {ChatRequestOptionLimits.MaxTimeoutSeconds}.");
        }

        return requested;
    }

    /// <summary>
    /// Creates a blocked-thread mutation request with operation-aware validation.
    /// </summary>
    /// <param name="requestId">Correlation id for the request.</param>
    /// <param name="operation">Requested operation: add, remove, replace, clear, or reset.</param>
    /// <param name="threadIds">Optional thread ids for targeted operations.</param>
    /// <param name="durationSeconds">Optional temporary suppression duration in seconds for add operations.</param>
    /// <param name="untilNextMaintenanceWindow">Whether the add operation should suppress until the next relevant maintenance window ends.</param>
    /// <param name="untilNextMaintenanceWindowStart">Whether the add operation should suppress until the next relevant maintenance window starts.</param>
    [JsonConstructor]
    [SetsRequiredMembers]
    public SetBackgroundSchedulerBlockedThreadsRequest(
        string requestId,
        string operation,
        string[]? threadIds = null,
        int? durationSeconds = null,
        bool untilNextMaintenanceWindow = false,
        bool untilNextMaintenanceWindowStart = false) {
        RequestId = requestId;
        Operation = NormalizeBackgroundSchedulerMutationOperation(operation, nameof(Operation));
        ThreadIds = NormalizeBackgroundSchedulerMutationTargets(threadIds);
        DurationSeconds = ValidatePositiveDurationSeconds(durationSeconds, nameof(DurationSeconds));
        UntilNextMaintenanceWindow = untilNextMaintenanceWindow;
        UntilNextMaintenanceWindowStart = untilNextMaintenanceWindowStart;

        ValidateBackgroundSchedulerTargetMutationState(Operation, ThreadIds, nameof(ThreadIds));
        ValidateBackgroundSchedulerTemporarySuppressionState(
            Operation,
            DurationSeconds,
            UntilNextMaintenanceWindow,
            UntilNextMaintenanceWindowStart,
            nameof(DurationSeconds),
            nameof(UntilNextMaintenanceWindow),
            nameof(UntilNextMaintenanceWindowStart));
    }

    /// <summary>
    /// Requested operation: add, remove, replace, clear, or reset.
    /// </summary>
    public string Operation { get; }

    /// <summary>
    /// Thread ids used by add/remove/replace operations.
    /// </summary>
    public string[]? ThreadIds { get; }

    /// <summary>
    /// Optional temporary suppression duration in seconds for add operations.
    /// Null means a persistent policy update.
    /// </summary>
    public int? DurationSeconds { get; }

    /// <summary>
    /// When true, add operations derive a temporary suppression that lasts until the next relevant maintenance window ends.
    /// </summary>
    public bool UntilNextMaintenanceWindow { get; }

    /// <summary>
    /// When true, add operations derive a temporary suppression that lasts until the next relevant maintenance window starts.
    /// </summary>
    public bool UntilNextMaintenanceWindowStart { get; }
}

/// <summary>
/// Requests health probes for registered <c>*_pack_info</c> tools.
/// </summary>
public sealed record CheckToolHealthRequest : ChatServiceRequest {
    /// <summary>
    /// Optional per-probe timeout in seconds (null means service default; 0 means no explicit timeout).
    /// </summary>
    public int? ToolTimeoutSeconds { get; init; }

    /// <summary>
    /// Optional source-kind filters. When set, only probes whose pack source kind matches one of these values are included.
    /// </summary>
    public ToolPackSourceKind[]? SourceKinds { get; init; }

    /// <summary>
    /// Optional pack-id filters. When set, only probes for matching pack ids are included.
    /// </summary>
    public string[]? PackIds { get; init; }
}

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
/// Applies runtime/provider settings in-process without restarting the service sidecar.
/// </summary>
public sealed record ApplyRuntimeSettingsRequest : ChatServiceRequest {
    /// <summary>
    /// Optional model override to persist/apply.
    /// </summary>
    public string? Model { get; init; }
    /// <summary>
    /// Optional provider transport override (native|compatible-http|copilot-cli|appserver).
    /// </summary>
    public string? OpenAITransport { get; init; }
    /// <summary>
    /// Optional compatible-http base URL override.
    /// </summary>
    public string? OpenAIBaseUrl { get; init; }
    /// <summary>
    /// Optional compatible-http API key update.
    /// </summary>
    public string? OpenAIApiKey { get; init; }
    /// <summary>
    /// Optional compatible-http auth mode override (bearer|basic|none).
    /// </summary>
    public string? OpenAIAuthMode { get; init; }
    /// <summary>
    /// Optional compatible-http basic auth username update.
    /// </summary>
    public string? OpenAIBasicUsername { get; init; }
    /// <summary>
    /// Optional compatible-http basic auth password update.
    /// </summary>
    public string? OpenAIBasicPassword { get; init; }
    /// <summary>
    /// Optional native transport account id override.
    /// Empty value clears account pinning and lets runtime pick the default bundle.
    /// </summary>
    public string? OpenAIAccountId { get; init; }
    /// <summary>
    /// When true, clears any stored compatible-http API key.
    /// </summary>
    public bool ClearOpenAIApiKey { get; init; }
    /// <summary>
    /// When true, clears any stored compatible-http basic auth username/password.
    /// </summary>
    public bool ClearOpenAIBasicAuth { get; init; }
    /// <summary>
    /// Optional streaming flag override.
    /// </summary>
    public bool? OpenAIStreaming { get; init; }
    /// <summary>
    /// Optional insecure-http allowance override.
    /// </summary>
    public bool? OpenAIAllowInsecureHttp { get; init; }
    /// <summary>
    /// Optional reasoning effort override (minimal|low|medium|high|xhigh). Empty clears override.
    /// </summary>
    public string? ReasoningEffort { get; init; }
    /// <summary>
    /// Optional reasoning summary override (auto|concise|detailed|off). Empty clears override.
    /// </summary>
    public string? ReasoningSummary { get; init; }
    /// <summary>
    /// Optional text verbosity override (low|medium|high). Empty clears override.
    /// </summary>
    public string? TextVerbosity { get; init; }
    /// <summary>
    /// Optional temperature override (0..2). Null keeps prior value.
    /// </summary>
    public double? Temperature { get; init; }
    /// <summary>
    /// Optional runtime pack ids to enable for the active session/profile.
    /// </summary>
    public string[]? EnablePackIds { get; init; }
    /// <summary>
    /// Optional runtime pack ids to disable for the active session/profile.
    /// </summary>
    public string[]? DisablePackIds { get; init; }
    /// <summary>
    /// Optional profile name to persist updated settings into.
    /// </summary>
    public string? ProfileName { get; init; }
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
    /// Optional reasoning effort override for this request (minimal|low|medium|high|xhigh). Empty clears override.
    /// </summary>
    public string? ReasoningEffort { get; init; }
    /// <summary>
    /// Optional reasoning summary override for this request (auto|concise|detailed|off). Empty clears override.
    /// </summary>
    public string? ReasoningSummary { get; init; }
    /// <summary>
    /// Optional text verbosity override for this request (low|medium|high). Empty clears override.
    /// </summary>
    public string? TextVerbosity { get; init; }
    /// <summary>
    /// Optional temperature override for this request (0..2).
    /// </summary>
    public double? Temperature { get; init; }
    /// <summary>
    /// Max tool-call rounds per user message
    /// (<see cref="ChatRequestOptionLimits.MinToolRounds"/>..<see cref="ChatRequestOptionLimits.MaxToolRounds"/>).
    /// </summary>
    public int MaxToolRounds { get; init; } = ChatRequestOptionLimits.DefaultToolRounds;
    /// <summary>
    /// Whether to execute tool calls in parallel when possible.
    /// This remains as a compatibility flag for older clients/servers.
    /// </summary>
    public bool ParallelTools { get; init; } = true;
    /// <summary>
    /// Optional parallel tool strategy override.
    /// Supported values: <c>auto</c>, <c>force_serial</c>, <c>allow_parallel</c>.
    /// </summary>
    public string? ParallelToolMode { get; init; }
    /// <summary>
    /// Optional per-turn timeout in seconds (null means use service default;
    /// <see cref="ChatRequestOptionLimits.MinTimeoutSeconds"/> means no explicit timeout;
    /// <see cref="ChatRequestOptionLimits.MinTimeoutSeconds"/>..<see cref="ChatRequestOptionLimits.MaxTimeoutSeconds"/> when provided).
    /// </summary>
    public int? TurnTimeoutSeconds { get; init; }
    /// <summary>
    /// Optional per-tool timeout in seconds (null means use service default;
    /// <see cref="ChatRequestOptionLimits.MinTimeoutSeconds"/> means no explicit timeout;
    /// <see cref="ChatRequestOptionLimits.MinTimeoutSeconds"/>..<see cref="ChatRequestOptionLimits.MaxTimeoutSeconds"/> when provided).
    /// </summary>
    public int? ToolTimeoutSeconds { get; init; }
    /// <summary>
    /// Optional allow-list of tool names to expose for this request.
    /// When provided, only listed tools remain available before <see cref="DisabledTools"/> filtering applies.
    /// An explicit empty/whitespace-only list disables tool exposure for the turn.
    /// </summary>
    public string[]? EnabledTools { get; init; }
    /// <summary>
    /// Optional allow-list of plugin pack ids to expose for this request.
    /// When provided, only tools from listed packs remain available before <see cref="DisabledPackIds"/> filtering applies.
    /// An explicit empty/whitespace-only list disables tool exposure for the turn.
    /// </summary>
    public string[]? EnabledPackIds { get; init; }
    /// <summary>
    /// Optional tool names to disable for this request.
    /// </summary>
    public string[]? DisabledTools { get; init; }
    /// <summary>
    /// Optional plugin pack ids to disable for this request.
    /// </summary>
    public string[]? DisabledPackIds { get; init; }
    /// <summary>
    /// Optional override for weighted tool routing (null means service default).
    /// </summary>
    public bool? WeightedToolRouting { get; init; }
    /// <summary>
    /// Optional cap for how many candidate tools are exposed to the model per turn.
    /// Null/<see cref="ChatRequestOptionLimits.MinCandidateTools"/> means service-selected default;
    /// <see cref="ChatRequestOptionLimits.MinCandidateTools"/>..<see cref="ChatRequestOptionLimits.MaxCandidateTools"/> when provided.
    /// </summary>
    public int? MaxCandidateTools { get; init; }
    /// <summary>
    /// Optional override for the deliberate runtime loop (<c>plan -> execute -> review</c>).
    /// Null means service default.
    /// </summary>
    public bool? PlanExecuteReviewLoop { get; init; }
    /// <summary>
    /// Optional cap for response-quality review passes per turn.
    /// 0 disables review passes. Null uses the service default.
    /// </summary>
    public int? MaxReviewPasses { get; init; }
    /// <summary>
    /// Optional heartbeat interval for model phases in seconds.
    /// Null means service-selected default. 0 disables model-phase heartbeats.
    /// </summary>
    public int? ModelHeartbeatSeconds { get; init; }
}
