namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Canonical request-option bounds shared across Chat host, service, and clients.
/// </summary>
public static class ChatRequestOptionLimits {
    /// <summary>
    /// Minimum allowed tool rounds per request.
    /// </summary>
    public const int MinToolRounds = 1;

    /// <summary>
    /// Maximum allowed tool rounds per request.
    /// </summary>
    public const int MaxToolRounds = 256;

    /// <summary>
    /// Default tool rounds per request.
    /// </summary>
    public const int DefaultToolRounds = 24;

    /// <summary>
    /// Minimum allowed candidate tools per request.
    /// </summary>
    public const int MinCandidateTools = 0;

    /// <summary>
    /// Maximum allowed candidate tools per request.
    /// </summary>
    public const int MaxCandidateTools = 256;

    /// <summary>
    /// Minimum allowed timeout value (seconds).
    /// </summary>
    public const int MinTimeoutSeconds = 0;

    /// <summary>
    /// Maximum allowed timeout value (seconds).
    /// </summary>
    public const int MaxTimeoutSeconds = 3600;

    /// <summary>
    /// Minimum positive timeout value (seconds) when an operation requires a bounded wait.
    /// </summary>
    public const int MinPositiveTimeoutSeconds = 1;

    /// <summary>
    /// Default max review passes when unset.
    /// </summary>
    public const int DefaultReviewPasses = 1;

    /// <summary>
    /// Maximum review passes supported per request.
    /// </summary>
    public const int MaxReviewPasses = 3;

    /// <summary>
    /// Default model heartbeat interval when unset.
    /// </summary>
    public const int DefaultModelHeartbeatSeconds = 8;

    /// <summary>
    /// Maximum model heartbeat interval supported per request.
    /// </summary>
    public const int MaxModelHeartbeatSeconds = 60;
}

/// <summary>
/// Canonical chat status tokens emitted by the service and consumed by clients/UI.
/// </summary>
public static class ChatStatusCodes {
    /// <summary>
    /// Generic thinking/progress status.
    /// </summary>
    public const string Thinking = "thinking";

    /// <summary>
    /// Indicates the serving model was selected.
    /// </summary>
    public const string ModelSelected = "model_selected";

    /// <summary>
    /// Indicates router analysis is running.
    /// </summary>
    public const string Routing = "routing";

    /// <summary>
    /// Emits structured routing metadata details.
    /// </summary>
    public const string RoutingMeta = "routing_meta";

    /// <summary>
    /// Indicates an individual tool-routing decision.
    /// </summary>
    public const string RoutingTool = "routing_tool";

    /// <summary>
    /// Indicates a tool invocation is being issued.
    /// </summary>
    public const string ToolCall = "tool_call";

    /// <summary>
    /// Indicates a tool is actively running.
    /// </summary>
    public const string ToolRunning = "tool_running";

    /// <summary>
    /// Heartbeat emitted while a tool is still running.
    /// </summary>
    public const string ToolHeartbeat = "tool_heartbeat";

    /// <summary>
    /// Indicates a tool completed successfully.
    /// </summary>
    public const string ToolCompleted = "tool_completed";

    /// <summary>
    /// Indicates a tool invocation was canceled.
    /// </summary>
    public const string ToolCanceled = "tool_canceled";

    /// <summary>
    /// Indicates tool execution was recovered after failure.
    /// </summary>
    public const string ToolRecovered = "tool_recovered";

    /// <summary>
    /// Announces configured tool parallelization mode.
    /// </summary>
    public const string ToolParallelMode = "tool_parallel_mode";

    /// <summary>
    /// Indicates parallel mode was forced on.
    /// </summary>
    public const string ToolParallelForced = "tool_parallel_forced";

    /// <summary>
    /// Indicates parallel safety guardrails were relaxed.
    /// </summary>
    public const string ToolParallelSafetyOff = "tool_parallel_safety_off";

    /// <summary>
    /// Indicates a tool batch execution started.
    /// </summary>
    public const string ToolBatchStarted = "tool_batch_started";

    /// <summary>
    /// Progress event for ongoing tool batch execution.
    /// </summary>
    public const string ToolBatchProgress = "tool_batch_progress";

    /// <summary>
    /// Heartbeat event for ongoing tool batch execution.
    /// </summary>
    public const string ToolBatchHeartbeat = "tool_batch_heartbeat";

    /// <summary>
    /// Indicates a batch is entering recovery mode.
    /// </summary>
    public const string ToolBatchRecovering = "tool_batch_recovering";

    /// <summary>
    /// Indicates a batch successfully recovered.
    /// </summary>
    public const string ToolBatchRecovered = "tool_batch_recovered";

    /// <summary>
    /// Indicates a tool batch completed.
    /// </summary>
    public const string ToolBatchCompleted = "tool_batch_completed";

    /// <summary>
    /// Indicates a tool round started.
    /// </summary>
    public const string ToolRoundStarted = "tool_round_started";

    /// <summary>
    /// Indicates a tool round completed.
    /// </summary>
    public const string ToolRoundCompleted = "tool_round_completed";

    /// <summary>
    /// Indicates max tool rounds were reached.
    /// </summary>
    public const string ToolRoundLimitReached = "tool_round_limit_reached";

    /// <summary>
    /// Indicates tool round cap was applied/clamped.
    /// </summary>
    public const string ToolRoundCapApplied = "tool_round_cap_applied";

    /// <summary>
    /// Indicates review pass count was clamped.
    /// </summary>
    public const string ReviewPassesClamped = "review_passes_clamped";

    /// <summary>
    /// Indicates model heartbeat interval was clamped.
    /// </summary>
    public const string ModelHeartbeatClamped = "model_heartbeat_clamped";

    /// <summary>
    /// Indicates the plan phase is active.
    /// </summary>
    public const string PhasePlan = "phase_plan";

    /// <summary>
    /// Indicates the execute phase is active.
    /// </summary>
    public const string PhaseExecute = "phase_execute";

    /// <summary>
    /// Indicates the review phase is active.
    /// </summary>
    public const string PhaseReview = "phase_review";

    /// <summary>
    /// Heartbeat emitted for long-running phases.
    /// </summary>
    public const string PhaseHeartbeat = "phase_heartbeat";

    /// <summary>
    /// Indicates repeated plan/review loops were cut off with deterministic blocker output.
    /// </summary>
    public const string NoResultWatchdogTriggered = "no_result_watchdog_triggered";
}
