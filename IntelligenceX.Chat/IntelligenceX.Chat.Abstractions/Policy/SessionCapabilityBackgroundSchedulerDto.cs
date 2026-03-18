using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Lightweight background scheduler/readiness summary for the active Chat runtime.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerDto {
    /// <summary>
    /// Optional thread scope applied to this scheduler snapshot.
    /// </summary>
    public string ScopeThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the runtime persists background work across restarts.
    /// </summary>
    public bool SupportsPersistentQueue { get; init; }

    /// <summary>
    /// Indicates whether the runtime can auto-replay safe read-only follow-up work.
    /// </summary>
    public bool SupportsReadOnlyAutoReplay { get; init; }

    /// <summary>
    /// Indicates whether the runtime can choose ready work across tracked threads.
    /// </summary>
    public bool SupportsCrossThreadScheduling { get; init; }

    /// <summary>
    /// Indicates whether the daemon scheduler loop is enabled for this runtime.
    /// </summary>
    public bool DaemonEnabled { get; init; }

    /// <summary>
    /// Indicates whether daemon auto-pause is enabled for repeated failures.
    /// </summary>
    public bool AutoPauseEnabled { get; init; }

    /// <summary>
    /// Indicates whether an operator-driven manual pause is active.
    /// </summary>
    public bool ManualPauseActive { get; init; }

    /// <summary>
    /// Indicates whether a configured maintenance window is currently pausing the daemon.
    /// </summary>
    public bool ScheduledPauseActive { get; init; }

    /// <summary>
    /// Consecutive non-success threshold that triggers daemon auto-pause.
    /// </summary>
    public int FailureThreshold { get; init; }

    /// <summary>
    /// Pause duration applied after the failure threshold is hit.
    /// </summary>
    public int FailurePauseSeconds { get; init; }

    /// <summary>
    /// Normalized pack ids explicitly allowed for daemon background execution.
    /// </summary>
    public string[] AllowedPackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Normalized pack ids explicitly blocked from daemon background execution.
    /// </summary>
    public string[] BlockedPackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Detailed active pack suppressions, including temporary/runtime entries.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerSuppressionDto[] BlockedPackSuppressions { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerSuppressionDto>();

    /// <summary>
    /// Thread ids explicitly allowed for daemon background execution.
    /// </summary>
    public string[] AllowedThreadIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Thread ids explicitly blocked from daemon background execution.
    /// </summary>
    public string[] BlockedThreadIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Detailed active thread suppressions, including temporary/runtime entries.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerSuppressionDto[] BlockedThreadSuppressions { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerSuppressionDto>();

    /// <summary>
    /// Configured recurring maintenance window specs evaluated in local service time.
    /// </summary>
    public string[] MaintenanceWindowSpecs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Structured configured maintenance windows.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerMaintenanceWindowDto[] MaintenanceWindows { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>();

    /// <summary>
    /// Time-active maintenance window specs, including scoped windows that may not pause the whole daemon.
    /// </summary>
    public string[] ActiveMaintenanceWindowSpecs { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Structured time-active maintenance windows, including scoped windows that may not pause the whole daemon.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerMaintenanceWindowDto[] ActiveMaintenanceWindows { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>();

    /// <summary>
    /// Indicates whether the background scheduler is currently paused.
    /// </summary>
    public bool Paused { get; init; }

    /// <summary>
    /// Best-effort UTC ticks when the active pause window ends.
    /// </summary>
    public long PausedUntilUtcTicks { get; init; }

    /// <summary>
    /// Normalized reason for the current pause window.
    /// </summary>
    public string PauseReason { get; init; } = string.Empty;

    /// <summary>
    /// Number of tracked threads with persisted or in-memory background work.
    /// </summary>
    public int TrackedThreadCount { get; init; }

    /// <summary>
    /// Number of tracked threads with ready work.
    /// </summary>
    public int ReadyThreadCount { get; init; }

    /// <summary>
    /// Number of tracked threads with running work.
    /// </summary>
    public int RunningThreadCount { get; init; }

    /// <summary>
    /// Number of tracked threads whose queued work is blocked on prerequisite helpers.
    /// </summary>
    public int DependencyBlockedThreadCount { get; init; }

    /// <summary>
    /// Total queued work items across tracked threads.
    /// </summary>
    public int QueuedItemCount { get; init; }

    /// <summary>
    /// Total queued work items currently blocked on prerequisite helpers.
    /// </summary>
    public int DependencyBlockedItemCount { get; init; }

    /// <summary>
    /// Sample of helper tool names currently blocking queued dependent work across tracked threads.
    /// </summary>
    public string[] DependencyHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Best-effort normalized recovery reason for dependency-blocked work across tracked threads.
    /// </summary>
    public string DependencyRecoveryReason { get; init; } = string.Empty;

    /// <summary>
    /// Best-effort normalized next action for dependency-blocked work across tracked threads.
    /// </summary>
    public string DependencyNextAction { get; init; } = string.Empty;

    /// <summary>
    /// Helper tools whose previously attempted executions are still pending retry/cooldown across tracked threads.
    /// </summary>
    public string[] DependencyRetryCooldownHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Helper tools indicating runtime authentication context is required before dependent work can continue.
    /// </summary>
    public string[] DependencyAuthenticationHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Minimal runtime authentication argument names inferred for blocked dependent work across tracked threads.
    /// </summary>
    public string[] DependencyAuthenticationArgumentNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Setup helpers indicating additional setup or runtime-profile context is required before dependent work can continue.
    /// </summary>
    public string[] DependencySetupHelperToolNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Total ready work items across tracked threads.
    /// </summary>
    public int ReadyItemCount { get; init; }

    /// <summary>
    /// Total running work items across tracked threads.
    /// </summary>
    public int RunningItemCount { get; init; }

    /// <summary>
    /// Total completed work items across tracked threads.
    /// </summary>
    public int CompletedItemCount { get; init; }

    /// <summary>
    /// Total read-only pending items across tracked threads.
    /// </summary>
    public int PendingReadOnlyItemCount { get; init; }

    /// <summary>
    /// Total unknown-mutability pending items across tracked threads.
    /// </summary>
    public int PendingUnknownItemCount { get; init; }

    /// <summary>
    /// Best-effort UTC ticks of the last scheduler scan/claim attempt.
    /// </summary>
    public long LastSchedulerTickUtcTicks { get; init; }

    /// <summary>
    /// Best-effort UTC ticks of the last scheduler outcome record.
    /// </summary>
    public long LastOutcomeUtcTicks { get; init; }

    /// <summary>
    /// Best-effort UTC ticks of the last successful completion.
    /// </summary>
    public long LastSuccessUtcTicks { get; init; }

    /// <summary>
    /// Best-effort UTC ticks of the last non-success outcome.
    /// </summary>
    public long LastFailureUtcTicks { get; init; }

    /// <summary>
    /// Indicates whether a recent adaptive-idle decision is still active for the current scheduler poll window.
    /// </summary>
    public bool AdaptiveIdleActive { get; init; }

    /// <summary>
    /// Best-effort UTC ticks when the scheduler last shortened its idle poll due to fresh reused prerequisite evidence.
    /// </summary>
    public long LastAdaptiveIdleUtcTicks { get; init; }

    /// <summary>
    /// Poll delay selected for the most recent adaptive-idle decision.
    /// </summary>
    public int LastAdaptiveIdleDelaySeconds { get; init; }

    /// <summary>
    /// Normalized reason recorded for the most recent adaptive-idle decision.
    /// </summary>
    public string LastAdaptiveIdleReason { get; init; } = string.Empty;

    /// <summary>
    /// Total successful background executions observed in this runtime.
    /// </summary>
    public int CompletedExecutionCount { get; init; }

    /// <summary>
    /// Total background executions requeued after tool failure in this runtime.
    /// </summary>
    public int RequeuedExecutionCount { get; init; }

    /// <summary>
    /// Total claimed background executions released without completion in this runtime.
    /// </summary>
    public int ReleasedExecutionCount { get; init; }

    /// <summary>
    /// Consecutive non-success outcomes observed since the last successful completion.
    /// </summary>
    public int ConsecutiveFailureCount { get; init; }

    /// <summary>
    /// Normalized label for the last observed scheduler outcome.
    /// </summary>
    public string LastOutcome { get; init; } = string.Empty;

    /// <summary>
    /// Sample of tracked thread ids with ready work.
    /// </summary>
    public string[] ReadyThreadIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Sample of tracked thread ids with running work.
    /// </summary>
    public string[] RunningThreadIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Recent scheduler activity sample for operators and runtime policy.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerActivityDto[] RecentActivity { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>();

    /// <summary>
    /// Compact per-thread scheduler summary sample.
    /// </summary>
    public SessionCapabilityBackgroundSchedulerThreadSummaryDto[] ThreadSummaries { get; init; } = Array.Empty<SessionCapabilityBackgroundSchedulerThreadSummaryDto>();
}
