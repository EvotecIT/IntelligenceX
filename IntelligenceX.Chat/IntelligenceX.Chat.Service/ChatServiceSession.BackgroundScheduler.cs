using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxBackgroundSchedulerThreadIds = 6;
    private const int MaxBackgroundSchedulerThreadSummaries = 4;
    private const int MaxBackgroundSchedulerRecentActivity = 6;
    private const int MaxBackgroundSchedulerRecentEvidenceTools = 3;
    private const int MaxBackgroundSchedulerActivityDetailLength = 160;
    private static readonly TimeSpan BackgroundSchedulerBusyDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan BackgroundSchedulerManualPausePollingDelay = TimeSpan.FromSeconds(5);
    internal enum BackgroundSchedulerIterationOutcomeKind {
        NoWorkReady = 0,
        Completed,
        RequeuedAfterToolFailure,
        ReleasedAfterEmptyOutput,
        ReleasedAfterException
    }

    private readonly record struct PersistedThreadBackgroundWorkSnapshot(
        string ThreadId,
        ThreadBackgroundWorkSnapshot Snapshot,
        long SeenUtcTicks);

    internal readonly record struct ScheduledBackgroundWorkClaim(
        string ThreadId,
        string ItemId,
        ToolCall ToolCall,
        string Reason);

    internal readonly record struct BackgroundSchedulerIterationResult(
        BackgroundSchedulerIterationOutcomeKind Outcome,
        string ThreadId,
        string ItemId,
        string ToolName,
        string Reason,
        int OutputCount,
        string FailureDetail) {
        public bool ClaimedWork => ThreadId.Length > 0 && ItemId.Length > 0 && ToolName.Length > 0;
        public bool ReleasedLease => Outcome is BackgroundSchedulerIterationOutcomeKind.ReleasedAfterEmptyOutput
            or BackgroundSchedulerIterationOutcomeKind.ReleasedAfterException;
        public bool WorkCompleted => Outcome == BackgroundSchedulerIterationOutcomeKind.Completed;
        public bool WorkRequeued => Outcome == BackgroundSchedulerIterationOutcomeKind.RequeuedAfterToolFailure;
    }

    private readonly record struct BackgroundSchedulerSummaryOptions(
        string ScopeThreadId,
        bool IncludeRecentActivity,
        bool IncludeThreadSummaries,
        int MaxReadyThreadIds,
        int MaxRunningThreadIds,
        int MaxRecentActivity,
        int MaxThreadSummaries);

    private SessionCapabilityBackgroundSchedulerDto BuildBackgroundSchedulerSummary() {
        return BuildBackgroundSchedulerSummary(new BackgroundSchedulerSummaryOptions(
            ScopeThreadId: string.Empty,
            IncludeRecentActivity: true,
            IncludeThreadSummaries: true,
            MaxReadyThreadIds: MaxBackgroundSchedulerThreadIds,
            MaxRunningThreadIds: MaxBackgroundSchedulerThreadIds,
            MaxRecentActivity: MaxBackgroundSchedulerRecentActivity,
            MaxThreadSummaries: MaxBackgroundSchedulerThreadSummaries));
    }

    private SessionCapabilityBackgroundSchedulerDto BuildBackgroundSchedulerSummary(BackgroundSchedulerSummaryOptions options) {
        RememberBackgroundSchedulerTick();

        var scopedThreadId = (options.ScopeThreadId ?? string.Empty).Trim();
        var trackedThreadIds = scopedThreadId.Length == 0
            ? EnumerateTrackedBackgroundWorkThreadIds()
            : new[] { scopedThreadId };
        var readyThreadIds = new List<string>();
        var runningThreadIds = new List<string>();
        var threadSummaries = new List<SessionCapabilityBackgroundSchedulerThreadSummaryDto>();
        var trackedThreadCount = 0;
        var readyThreadCount = 0;
        var runningThreadCount = 0;
        var dependencyBlockedThreadCount = 0;
        var queuedItemCount = 0;
        var dependencyBlockedItemCount = 0;
        var dependencyHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencyRetryCooldownHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencyAuthenticationHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencyAuthenticationArgumentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencySetupHelperToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var readyItemCount = 0;
        var runningItemCount = 0;
        var completedItemCount = 0;
        var pendingReadOnlyItemCount = 0;
        var pendingUnknownItemCount = 0;
        var nowTicks = DateTime.UtcNow.Ticks;
        var manualPauseState = _backgroundSchedulerControlState.GetSnapshot(nowTicks);
        string lastOutcome;
        long lastOutcomeUtcTicks;
        long lastSuccessUtcTicks;
        long lastFailureUtcTicks;
        long pausedUntilUtcTicks;
        string pauseReason;
        int completedExecutionCount;
        int requeuedExecutionCount;
        int releasedExecutionCount;
        int consecutiveFailureCount;
        SessionCapabilityBackgroundSchedulerActivityDto[] recentActivity;
        var allowedPackIds = NormalizeBackgroundSchedulerPackIds(_options.BackgroundSchedulerAllowedPackIds);
        var blockedPackIds = _backgroundSchedulerControlState.GetBlockedPackIds(nowTicks);
        var blockedPackSuppressions = _backgroundSchedulerControlState.GetBlockedPackSuppressions(nowTicks);
        var allowedThreadIds = NormalizeBackgroundSchedulerThreadIds(_options.BackgroundSchedulerAllowedThreadIds);
        var blockedThreadIds = _backgroundSchedulerControlState.GetBlockedThreadIds(nowTicks);
        var blockedThreadSuppressions = _backgroundSchedulerControlState.GetBlockedThreadSuppressions(nowTicks);
        var maxReadyThreadIds = Math.Clamp(options.MaxReadyThreadIds, 0, ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems);
        var maxRunningThreadIds = Math.Clamp(options.MaxRunningThreadIds, 0, ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems);
        var maxRecentActivity = Math.Clamp(options.MaxRecentActivity, 0, ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems);
        var maxThreadSummaries = Math.Clamp(options.MaxThreadSummaries, 0, ChatRequestOptionLimits.MaxBackgroundSchedulerStatusItems);

        lock (_backgroundSchedulerTelemetryLock) {
            NormalizeBackgroundSchedulerPauseStateNoLock(nowTicks);
            lastOutcome = _backgroundSchedulerLastOutcome;
            lastOutcomeUtcTicks = _backgroundSchedulerLastOutcomeUtcTicks;
            lastSuccessUtcTicks = _backgroundSchedulerLastSuccessUtcTicks;
            lastFailureUtcTicks = _backgroundSchedulerLastFailureUtcTicks;
            pausedUntilUtcTicks = _backgroundSchedulerPausedUntilUtcTicks;
            pauseReason = _backgroundSchedulerPauseReason;
            completedExecutionCount = _backgroundSchedulerCompletedExecutionCount;
            requeuedExecutionCount = _backgroundSchedulerRequeuedExecutionCount;
            releasedExecutionCount = _backgroundSchedulerReleasedExecutionCount;
            consecutiveFailureCount = _backgroundSchedulerConsecutiveFailureCount;
            recentActivity = _backgroundSchedulerRecentActivity.ToArray();
        }

        for (var i = 0; i < trackedThreadIds.Length; i++) {
            var threadId = trackedThreadIds[i];
            if (!TryGetRememberedThreadBackgroundWorkSnapshot(threadId, out var snapshot)
                || IsEmptyBackgroundWorkSnapshot(snapshot)) {
                continue;
            }

            trackedThreadCount++;
            queuedItemCount += Math.Max(0, snapshot.QueuedCount);
            readyItemCount += Math.Max(0, snapshot.ReadyCount);
            runningItemCount += Math.Max(0, snapshot.RunningCount);
            completedItemCount += Math.Max(0, snapshot.CompletedCount);
            pendingReadOnlyItemCount += Math.Max(0, snapshot.PendingReadOnlyCount);
            pendingUnknownItemCount += Math.Max(0, snapshot.PendingUnknownCount);
            var dependencySummary = BuildBackgroundWorkDependencySummary(snapshot.Items);
            var dependencyRecoverySummary = BuildBackgroundWorkDependencyRecoverySummary(snapshot.Items, _registry.GetDefinitions());
            dependencyBlockedItemCount += Math.Max(0, dependencySummary.BlockedItemCount);
            foreach (var helperToolName in dependencyRecoverySummary.HelperToolNames) {
                if (!string.IsNullOrWhiteSpace(helperToolName)) {
                    dependencyHelperToolNames.Add(helperToolName);
                }
            }

            foreach (var helperToolName in dependencyRecoverySummary.RetryCooldownHelperToolNames) {
                if (!string.IsNullOrWhiteSpace(helperToolName)) {
                    dependencyRetryCooldownHelperToolNames.Add(helperToolName);
                }
            }

            foreach (var helperToolName in dependencyRecoverySummary.AuthenticationHelperToolNames) {
                if (!string.IsNullOrWhiteSpace(helperToolName)) {
                    dependencyAuthenticationHelperToolNames.Add(helperToolName);
                }
            }

            foreach (var argumentName in dependencyRecoverySummary.AuthenticationArgumentNames) {
                if (!string.IsNullOrWhiteSpace(argumentName)) {
                    dependencyAuthenticationArgumentNames.Add(argumentName);
                }
            }

            foreach (var helperToolName in dependencyRecoverySummary.SetupHelperToolNames) {
                if (!string.IsNullOrWhiteSpace(helperToolName)) {
                    dependencySetupHelperToolNames.Add(helperToolName);
                }
            }

            if (dependencySummary.BlockedItemCount > 0) {
                dependencyBlockedThreadCount++;
            }

            if (snapshot.ReadyCount > 0) {
                readyThreadCount++;
                if (readyThreadIds.Count < maxReadyThreadIds) {
                    readyThreadIds.Add(threadId);
                }
            }

            if (snapshot.RunningCount > 0) {
                runningThreadCount++;
                if (runningThreadIds.Count < maxRunningThreadIds) {
                    runningThreadIds.Add(threadId);
                }
            }

            if (options.IncludeThreadSummaries) {
                threadSummaries.Add(BuildBackgroundSchedulerThreadSummary(threadId, snapshot, _registry.GetDefinitions()));
            }
        }

        if (options.IncludeThreadSummaries && threadSummaries.Count > 0) {
            threadSummaries = threadSummaries
                .OrderByDescending(static summary => Math.Max(0, summary.RunningItemCount))
                .ThenByDescending(static summary => Math.Max(0, summary.ReadyItemCount))
                .ThenByDescending(static summary => Math.Max(0, summary.QueuedItemCount))
                .ThenByDescending(static summary => Math.Max(0, summary.PendingReadOnlyItemCount))
                .ThenBy(static summary => summary.ThreadId, StringComparer.Ordinal)
                .Take(maxThreadSummaries)
                .ToList();
        } else {
            threadSummaries.Clear();
        }

        SessionCapabilityBackgroundSchedulerActivityDto[] activitySample;
        if (options.IncludeRecentActivity && maxRecentActivity > 0) {
            activitySample = recentActivity
                .Where(activity => scopedThreadId.Length == 0 || string.Equals(activity.ThreadId, scopedThreadId, StringComparison.Ordinal))
                .OrderByDescending(static activity => Math.Max(0, activity.RecordedUtcTicks))
                .ThenByDescending(static activity => Math.Max(0, activity.OutputCount))
                .Take(maxRecentActivity)
                .ToArray();
        } else {
            activitySample = Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>();
        }

        var maintenanceWindowSpecs = _backgroundSchedulerControlState.GetMaintenanceWindowSpecs();
        var maintenanceWindows = _backgroundSchedulerControlState.GetMaintenanceWindows();
        var activeMaintenanceWindowSpecs = _backgroundSchedulerControlState.GetActiveMaintenanceWindowSpecs(nowTicks);
        var activeMaintenanceWindows = _backgroundSchedulerControlState.GetActiveMaintenanceWindows(nowTicks);
        var scheduledPauseActive = manualPauseState.ScheduledPauseActive;
        var effectivePauseReason = manualPauseState.ManualPauseActive || scheduledPauseActive
            ? manualPauseState.PauseReason
            : pauseReason;
        var effectivePausedUntilUtcTicks = manualPauseState.ManualPauseActive || scheduledPauseActive
            ? Math.Max(0, manualPauseState.PausedUntilUtcTicks)
            : Math.Max(0, pausedUntilUtcTicks);
        var schedulerDependencyRecoverySummary = new BackgroundWorkDependencyRecoverySummary(
            BlockedItemCount: Math.Max(0, dependencyBlockedItemCount),
            HelperToolNames: dependencyHelperToolNames.Take(MaxBackgroundSchedulerRecentEvidenceTools).ToArray(),
            RetryCooldownHelperToolNames: dependencyRetryCooldownHelperToolNames.Take(MaxBackgroundSchedulerRecentEvidenceTools).ToArray(),
            AuthenticationHelperToolNames: dependencyAuthenticationHelperToolNames.Take(MaxBackgroundSchedulerRecentEvidenceTools).ToArray(),
            AuthenticationArgumentNames: dependencyAuthenticationArgumentNames.Take(4).ToArray(),
            SetupHelperToolNames: dependencySetupHelperToolNames.Take(MaxBackgroundSchedulerRecentEvidenceTools).ToArray());
        var dependencyRecoveryReason = ResolveBackgroundWorkDependencyRecoveryReason(schedulerDependencyRecoverySummary);
        var dependencyNextAction = ResolveBackgroundWorkDependencyNextAction(schedulerDependencyRecoverySummary);

        return new SessionCapabilityBackgroundSchedulerDto {
            ScopeThreadId = scopedThreadId,
            SupportsPersistentQueue = true,
            SupportsReadOnlyAutoReplay = true,
            SupportsCrossThreadScheduling = true,
            DaemonEnabled = _options.EnableBackgroundSchedulerDaemon,
            AutoPauseEnabled = _options.EnableBackgroundSchedulerDaemon && _options.BackgroundSchedulerFailureThreshold > 0,
            ManualPauseActive = manualPauseState.ManualPauseActive,
            ScheduledPauseActive = scheduledPauseActive,
            FailureThreshold = Math.Max(0, _options.BackgroundSchedulerFailureThreshold),
            FailurePauseSeconds = Math.Max(0, _options.BackgroundSchedulerFailurePauseSeconds),
            AllowedPackIds = allowedPackIds,
            BlockedPackIds = blockedPackIds,
            BlockedPackSuppressions = blockedPackSuppressions,
            AllowedThreadIds = allowedThreadIds,
            BlockedThreadIds = blockedThreadIds,
            BlockedThreadSuppressions = blockedThreadSuppressions,
            MaintenanceWindowSpecs = maintenanceWindowSpecs,
            MaintenanceWindows = maintenanceWindows,
            ActiveMaintenanceWindowSpecs = activeMaintenanceWindowSpecs,
            ActiveMaintenanceWindows = activeMaintenanceWindows,
            Paused = manualPauseState.ManualPauseActive || scheduledPauseActive || pausedUntilUtcTicks > nowTicks,
            PausedUntilUtcTicks = effectivePausedUntilUtcTicks,
            PauseReason = effectivePauseReason,
            TrackedThreadCount = Math.Max(0, trackedThreadCount),
            ReadyThreadCount = Math.Max(0, readyThreadCount),
            RunningThreadCount = Math.Max(0, runningThreadCount),
            DependencyBlockedThreadCount = Math.Max(0, dependencyBlockedThreadCount),
            QueuedItemCount = Math.Max(0, queuedItemCount),
            DependencyBlockedItemCount = Math.Max(0, dependencyBlockedItemCount),
            DependencyHelperToolNames = NormalizeDistinctStrings(schedulerDependencyRecoverySummary.HelperToolNames, MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyRecoveryReason = NormalizeBackgroundSchedulerActivityText(dependencyRecoveryReason, maxLength: 80),
            DependencyNextAction = NormalizeBackgroundSchedulerActivityText(dependencyNextAction, maxLength: 80),
            DependencyRetryCooldownHelperToolNames = NormalizeDistinctStrings(schedulerDependencyRecoverySummary.RetryCooldownHelperToolNames, MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyAuthenticationHelperToolNames = NormalizeDistinctStrings(schedulerDependencyRecoverySummary.AuthenticationHelperToolNames, MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyAuthenticationArgumentNames = NormalizeDistinctStrings(schedulerDependencyRecoverySummary.AuthenticationArgumentNames, 4),
            DependencySetupHelperToolNames = NormalizeDistinctStrings(schedulerDependencyRecoverySummary.SetupHelperToolNames, MaxBackgroundSchedulerRecentEvidenceTools),
            ReadyItemCount = Math.Max(0, readyItemCount),
            RunningItemCount = Math.Max(0, runningItemCount),
            CompletedItemCount = Math.Max(0, completedItemCount),
            PendingReadOnlyItemCount = Math.Max(0, pendingReadOnlyItemCount),
            PendingUnknownItemCount = Math.Max(0, pendingUnknownItemCount),
            LastSchedulerTickUtcTicks = Interlocked.Read(ref _backgroundSchedulerLastTickUtcTicks),
            LastOutcomeUtcTicks = Math.Max(0, lastOutcomeUtcTicks),
            LastSuccessUtcTicks = Math.Max(0, lastSuccessUtcTicks),
            LastFailureUtcTicks = Math.Max(0, lastFailureUtcTicks),
            CompletedExecutionCount = Math.Max(0, completedExecutionCount),
            RequeuedExecutionCount = Math.Max(0, requeuedExecutionCount),
            ReleasedExecutionCount = Math.Max(0, releasedExecutionCount),
            ConsecutiveFailureCount = Math.Max(0, consecutiveFailureCount),
            LastOutcome = lastOutcome,
            ReadyThreadIds = readyThreadIds.ToArray(),
            RunningThreadIds = runningThreadIds.ToArray(),
            RecentActivity = activitySample,
            ThreadSummaries = threadSummaries.ToArray()
        };
    }

    private bool TryBuildScheduledBackgroundWorkReplayCandidate(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out string threadId,
        out ToolCall toolCall,
        out string itemId,
        out string reason) {
        threadId = string.Empty;
        toolCall = null!;
        itemId = string.Empty;
        reason = "background_scheduler_empty";

        RememberBackgroundSchedulerTick();

        if (toolDefinitions is null || toolDefinitions.Count == 0) {
            reason = "background_scheduler_missing_definitions";
            return false;
        }

        var previews = new List<(string ThreadId, BackgroundWorkReplayCandidate Candidate)>();
        var trackedThreadIds = EnumerateTrackedBackgroundWorkThreadIds();
        var nowTicks = DateTime.UtcNow.Ticks;
        for (var i = 0; i < trackedThreadIds.Length; i++) {
            var candidateThreadId = trackedThreadIds[i];
            if (!IsBackgroundSchedulerThreadAllowed(candidateThreadId, out _)) {
                continue;
            }
            if (_backgroundSchedulerControlState.TryGetScopedMaintenanceWindowPause(
                    nowTicks,
                    candidateThreadId,
                    packId: null,
                    out _)) {
                continue;
            }
            if (!TryPreviewReadyBackgroundWorkReplayCandidate(
                    candidateThreadId,
                    userRequest: string.Empty,
                    toolDefinitions,
                    mutatingToolHintsByName,
                    out var preview,
                    out _)) {
                continue;
            }

            previews.Add((candidateThreadId, preview));
        }

        if (previews.Count == 0) {
            reason = "background_scheduler_no_ready_work";
            return false;
        }

        var previewTargetDefinitions = new List<ToolDefinition>(previews.Count);
        for (var i = 0; i < previews.Count; i++) {
            if (TryGetToolDefinitionByName(toolDefinitions, previews[i].Candidate.Item.TargetToolName, out var previewDefinition)) {
                previewTargetDefinitions.Add(previewDefinition);
            }
        }

        var helperDemandByToolName = BuildContractHelperDemandByToolName(previewTargetDefinitions, _toolOrchestrationCatalog);
        var availablePreviewTargetToolNames = new HashSet<string>(
            previewTargetDefinitions
                .Select(static definition => NormalizeToolNameForAnswerPlan(definition?.Name))
                .Where(static toolName => toolName.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        previews.Sort((left, right) => {
            var priorityCompare = CompareBackgroundWorkReplayPriority(
                left.Candidate.Item,
                right.Candidate.Item,
                toolDefinitions,
                helperDemandByToolName,
                availablePreviewTargetToolNames);
            if (priorityCompare != 0) {
                return priorityCompare;
            }

            return StringComparer.Ordinal.Compare(left.ThreadId, right.ThreadId);
        });

        for (var i = 0; i < previews.Count; i++) {
            var preview = previews[i];
            if (!TryBuildReadyBackgroundWorkToolCall(
                    preview.ThreadId,
                    userRequest: string.Empty,
                    toolDefinitions,
                    mutatingToolHintsByName,
                    out var claimedToolCall,
                    out var claimedItemId,
                    out var claimReason)) {
                reason = claimReason;
                continue;
            }

            threadId = preview.ThreadId;
            toolCall = claimedToolCall;
            itemId = claimedItemId;
            reason = "background_scheduler_claimed_ready_work";
            return true;
        }

        return false;
    }

    internal async Task<BackgroundSchedulerIterationResult> RunBackgroundSchedulerIterationAsync(
        Func<string, ToolCall, CancellationToken, Task<IReadOnlyList<ToolOutputDto>>> executor,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(executor);

        var toolDefinitions = _registry.GetDefinitions();
        var mutatingToolHintsByName = BuildMutatingToolHintsByName(toolDefinitions);
        return await RunBackgroundSchedulerIterationAsync(toolDefinitions, mutatingToolHintsByName, executor, cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<BackgroundSchedulerIterationResult> RunBackgroundSchedulerIterationAsync(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        Func<string, ToolCall, CancellationToken, Task<IReadOnlyList<ToolOutputDto>>> executor,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(toolDefinitions);
        ArgumentNullException.ThrowIfNull(executor);

        if (!TryClaimScheduledBackgroundWorkExecution(toolDefinitions, mutatingToolHintsByName, out var claim)) {
            return new BackgroundSchedulerIterationResult(
                Outcome: BackgroundSchedulerIterationOutcomeKind.NoWorkReady,
                ThreadId: string.Empty,
                ItemId: string.Empty,
                ToolName: string.Empty,
                Reason: "background_scheduler_no_ready_work",
                OutputCount: 0,
                FailureDetail: string.Empty);
        }

        try {
            var outputs = await executor(claim.ThreadId, claim.ToolCall, cancellationToken).ConfigureAwait(false)
                          ?? Array.Empty<ToolOutputDto>();
            if (outputs.Count == 0) {
                var released = TryReleaseScheduledBackgroundWorkReplayCandidate(claim.ThreadId, claim.ItemId);
                var result = new BackgroundSchedulerIterationResult(
                    Outcome: BackgroundSchedulerIterationOutcomeKind.ReleasedAfterEmptyOutput,
                    ThreadId: claim.ThreadId,
                    ItemId: claim.ItemId,
                    ToolName: claim.ToolCall.Name,
                    Reason: released ? "background_scheduler_released_after_empty_output" : "background_scheduler_empty_output_release_failed",
                    OutputCount: 0,
                    FailureDetail: string.Empty);
                RememberBackgroundSchedulerIterationResult(result);
                return result;
            }

            RememberBackgroundWorkExecutionOutcome(claim.ThreadId, claim.ItemId, claim.ToolCall.CallId, outputs);
            var succeeded = outputs[0].Ok == true;
            var iterationResult = new BackgroundSchedulerIterationResult(
                Outcome: succeeded
                    ? BackgroundSchedulerIterationOutcomeKind.Completed
                    : BackgroundSchedulerIterationOutcomeKind.RequeuedAfterToolFailure,
                ThreadId: claim.ThreadId,
                ItemId: claim.ItemId,
                ToolName: claim.ToolCall.Name,
                Reason: succeeded
                    ? "background_scheduler_completed"
                    : "background_scheduler_requeued_after_tool_failure",
                OutputCount: outputs.Count,
                FailureDetail: succeeded ? string.Empty : (outputs[0].ErrorCode ?? outputs[0].Error ?? string.Empty).Trim());
            RememberBackgroundSchedulerIterationResult(iterationResult);
            return iterationResult;
        } catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            _ = TryReleaseScheduledBackgroundWorkReplayCandidate(claim.ThreadId, claim.ItemId);
            throw;
        } catch (Exception ex) {
            var released = TryReleaseScheduledBackgroundWorkReplayCandidate(claim.ThreadId, claim.ItemId);
            var result = new BackgroundSchedulerIterationResult(
                Outcome: BackgroundSchedulerIterationOutcomeKind.ReleasedAfterException,
                ThreadId: claim.ThreadId,
                ItemId: claim.ItemId,
                ToolName: claim.ToolCall.Name,
                Reason: released ? "background_scheduler_released_after_exception" : "background_scheduler_exception_release_failed",
                OutputCount: 0,
                FailureDetail: ex.Message.Trim());
            RememberBackgroundSchedulerIterationResult(result);
            return result;
        }
    }

    private bool TryClaimScheduledBackgroundWorkExecution(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ScheduledBackgroundWorkClaim claim) {
        claim = default;
        if (!TryBuildScheduledBackgroundWorkReplayCandidate(
                toolDefinitions,
                mutatingToolHintsByName,
                out var threadId,
                out var toolCall,
                out var itemId,
                out var reason)) {
            return false;
        }

        claim = new ScheduledBackgroundWorkClaim(threadId, itemId, toolCall, reason);
        return true;
    }

    private void RememberBackgroundSchedulerIterationResult(BackgroundSchedulerIterationResult result, long? utcTicks = null) {
        if (result.Outcome == BackgroundSchedulerIterationOutcomeKind.NoWorkReady) {
            return;
        }

        var recordedTicks = utcTicks.GetValueOrDefault(DateTime.UtcNow.Ticks);
        lock (_backgroundSchedulerTelemetryLock) {
            _backgroundSchedulerLastOutcome = NormalizeBackgroundSchedulerOutcome(result.Outcome);
            _backgroundSchedulerLastOutcomeUtcTicks = recordedTicks;
            _backgroundSchedulerRecentActivity.Insert(0, BuildBackgroundSchedulerActivity(result, recordedTicks));
            if (_backgroundSchedulerRecentActivity.Count > MaxBackgroundSchedulerRecentActivity) {
                _backgroundSchedulerRecentActivity.RemoveRange(
                    MaxBackgroundSchedulerRecentActivity,
                    _backgroundSchedulerRecentActivity.Count - MaxBackgroundSchedulerRecentActivity);
            }

            switch (result.Outcome) {
                case BackgroundSchedulerIterationOutcomeKind.Completed:
                    _backgroundSchedulerLastSuccessUtcTicks = recordedTicks;
                    _backgroundSchedulerCompletedExecutionCount++;
                    _backgroundSchedulerConsecutiveFailureCount = 0;
                    _backgroundSchedulerPausedUntilUtcTicks = 0;
                    _backgroundSchedulerPauseReason = string.Empty;
                    break;
                case BackgroundSchedulerIterationOutcomeKind.RequeuedAfterToolFailure:
                    _backgroundSchedulerLastFailureUtcTicks = recordedTicks;
                    _backgroundSchedulerRequeuedExecutionCount++;
                    _backgroundSchedulerConsecutiveFailureCount++;
                    break;
                case BackgroundSchedulerIterationOutcomeKind.ReleasedAfterEmptyOutput:
                case BackgroundSchedulerIterationOutcomeKind.ReleasedAfterException:
                    _backgroundSchedulerLastFailureUtcTicks = recordedTicks;
                    _backgroundSchedulerReleasedExecutionCount++;
                    _backgroundSchedulerConsecutiveFailureCount++;
                    break;
            }

            TryArmBackgroundSchedulerAutoPauseNoLock(result, recordedTicks);
        }
    }

    internal async Task RunBackgroundSchedulerDaemonAsync(CancellationToken cancellationToken) {
        if (!_options.EnableBackgroundSchedulerDaemon) {
            return;
        }

        var bootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
        if (bootstrapTask is null) {
            bootstrapTask = Task.Run(() => RebuildToolingCore(clearRoutingCaches: false), CancellationToken.None);
            _startupToolingBootstrapTask = bootstrapTask;
        }

        await bootstrapTask.ConfigureAwait(false);

        var idleDelay = TimeSpan.FromSeconds(Math.Clamp(
            _options.BackgroundSchedulerPollSeconds,
            1,
            3600));
        var burstLimit = Math.Clamp(_options.BackgroundSchedulerBurstLimit, 1, 32);

        Trace.WriteLine($"[background-scheduler-daemon] status=started poll_seconds={Math.Max(1, (int)idleDelay.TotalSeconds)} burst_limit={burstLimit}");

        while (!cancellationToken.IsCancellationRequested) {
            if (TryGetBackgroundSchedulerPauseDelay(out var pauseDelay, out var pauseReason)) {
                Trace.WriteLine(BuildBackgroundSchedulerPauseTraceLine(pauseDelay, pauseReason));
                await Task.Delay(pauseDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var processedAnyWork = false;
            for (var i = 0; i < burstLimit && !cancellationToken.IsCancellationRequested; i++) {
                if (TryGetBackgroundSchedulerPauseDelay(out pauseDelay, out pauseReason)) {
                    Trace.WriteLine(BuildBackgroundSchedulerPauseTraceLine(pauseDelay, pauseReason));
                    break;
                }

                var result = await RunBackgroundSchedulerIterationAsync(
                        executor: ExecuteBackgroundScheduledToolAsync,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (result.Outcome == BackgroundSchedulerIterationOutcomeKind.NoWorkReady) {
                    break;
                }

                processedAnyWork = true;
                Trace.WriteLine(BuildBackgroundSchedulerDaemonTraceLine(result));
                if (TryGetBackgroundSchedulerPauseDelay(out pauseDelay, out pauseReason)) {
                    Trace.WriteLine(BuildBackgroundSchedulerPauseTraceLine(pauseDelay, pauseReason));
                    break;
                }
            }

            var delay = TryGetBackgroundSchedulerPauseDelay(out var activePauseDelay, out _)
                ? activePauseDelay
                : processedAnyWork
                    ? BackgroundSchedulerBusyDelay
                    : idleDelay;
            if (delay <= TimeSpan.Zero) {
                continue;
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task<IReadOnlyList<ToolOutputDto>> ExecuteBackgroundScheduledToolAsync(string threadId, ToolCall call, CancellationToken cancellationToken) {
        return ExecuteBackgroundScheduledToolCoreAsync(threadId, call, cancellationToken);
    }

    private async Task<IReadOnlyList<ToolOutputDto>> ExecuteBackgroundScheduledToolCoreAsync(string threadId, ToolCall call, CancellationToken cancellationToken) {
        var output = await ExecuteToolAsync(
                threadId,
                userRequest: "ix:background-scheduler-daemon",
                call,
                _options.ToolTimeoutSeconds,
                cancellationToken)
            .ConfigureAwait(false);
        return new[] { output };
    }

    private static string BuildBackgroundSchedulerDaemonTraceLine(BackgroundSchedulerIterationResult result) {
        var failureDetail = result.FailureDetail.Length == 0 ? string.Empty : $" failure='{result.FailureDetail}'";
        return $"[background-scheduler-daemon] outcome={result.Outcome} thread='{result.ThreadId}' item='{result.ItemId}' tool='{result.ToolName}' reason='{result.Reason}' outputs='{result.OutputCount}'{failureDetail}";
    }

    private static string BuildBackgroundSchedulerPauseTraceLine(TimeSpan pauseDelay, string pauseReason) {
        var normalizedReason = NormalizeBackgroundSchedulerActivityText(pauseReason, maxLength: 160);
        var reasonSuffix = normalizedReason.Length == 0 ? string.Empty : $" reason='{normalizedReason}'";
        return $"[background-scheduler-daemon] status=paused remaining_seconds='{Math.Max(1, (int)Math.Ceiling(pauseDelay.TotalSeconds))}'{reasonSuffix}";
    }

    private SessionCapabilityBackgroundSchedulerDto SetBackgroundSchedulerManualPause(bool paused, int? pauseSeconds, string? reason) {
        _backgroundSchedulerControlState.SetManualPause(
            paused,
            pauseSeconds,
            reason ?? string.Empty);

        return BuildBackgroundSchedulerSummary();
    }

    private static string NormalizeBackgroundSchedulerOutcome(BackgroundSchedulerIterationOutcomeKind outcome) {
        return outcome switch {
            BackgroundSchedulerIterationOutcomeKind.Completed => "completed",
            BackgroundSchedulerIterationOutcomeKind.RequeuedAfterToolFailure => "requeued_after_tool_failure",
            BackgroundSchedulerIterationOutcomeKind.ReleasedAfterEmptyOutput => "released_after_empty_output",
            BackgroundSchedulerIterationOutcomeKind.ReleasedAfterException => "released_after_exception",
            _ => "no_work_ready"
        };
    }

    private void TryArmBackgroundSchedulerAutoPauseNoLock(BackgroundSchedulerIterationResult result, long recordedTicks) {
        if (!_options.EnableBackgroundSchedulerDaemon || _options.BackgroundSchedulerFailureThreshold <= 0) {
            return;
        }

        var sharedPauseState = _backgroundSchedulerControlState.GetSnapshot(recordedTicks);
        if (sharedPauseState.ManualPauseActive || sharedPauseState.ScheduledPauseActive) {
            return;
        }

        if (result.Outcome == BackgroundSchedulerIterationOutcomeKind.Completed) {
            return;
        }

        var threshold = Math.Max(1, _options.BackgroundSchedulerFailureThreshold);
        if (_backgroundSchedulerConsecutiveFailureCount <= 0 || _backgroundSchedulerConsecutiveFailureCount % threshold != 0) {
            return;
        }

        var pauseSeconds = Math.Clamp(
            _options.BackgroundSchedulerFailurePauseSeconds,
            1,
            3600);
        _backgroundSchedulerPausedUntilUtcTicks = recordedTicks + TimeSpan.FromSeconds(pauseSeconds).Ticks;
        _backgroundSchedulerPauseReason = NormalizeBackgroundSchedulerPauseReason(result);
    }

    private bool TryGetBackgroundSchedulerPauseDelay(out TimeSpan delay, out string pauseReason) {
        var nowTicks = DateTime.UtcNow.Ticks;
        var manualPauseState = _backgroundSchedulerControlState.GetSnapshot(nowTicks);
        if (manualPauseState.ManualPauseActive || manualPauseState.ScheduledPauseActive) {
            pauseReason = manualPauseState.PauseReason;
            delay = manualPauseState.PausedUntilUtcTicks > 0
                ? TimeSpan.FromTicks(Math.Max(1L, manualPauseState.PausedUntilUtcTicks - nowTicks))
                : BackgroundSchedulerManualPausePollingDelay;
            return true;
        }

        lock (_backgroundSchedulerTelemetryLock) {
            NormalizeBackgroundSchedulerPauseStateNoLock(nowTicks);
            if (_backgroundSchedulerPausedUntilUtcTicks <= 0) {
                delay = TimeSpan.Zero;
                pauseReason = string.Empty;
                return false;
            }

            pauseReason = _backgroundSchedulerPauseReason;
            delay = TimeSpan.FromTicks(Math.Max(1L, _backgroundSchedulerPausedUntilUtcTicks - nowTicks));
            return true;
        }
    }

    private void NormalizeBackgroundSchedulerPauseStateNoLock(long nowTicks) {
        if (_backgroundSchedulerPausedUntilUtcTicks <= 0 || nowTicks <= 0 || _backgroundSchedulerPausedUntilUtcTicks > nowTicks) {
            return;
        }

        _backgroundSchedulerPausedUntilUtcTicks = 0;
        _backgroundSchedulerPauseReason = string.Empty;
    }

    private static string NormalizeBackgroundSchedulerPauseReason(BackgroundSchedulerIterationResult result) {
        var outcome = NormalizeBackgroundSchedulerOutcome(result.Outcome);
        var toolName = NormalizeBackgroundSchedulerActivityText(result.ToolName, maxLength: 80);
        if (toolName.Length == 0) {
            return "consecutive_failure_threshold_reached:" + outcome;
        }

        return "consecutive_failure_threshold_reached:" + outcome + ":" + toolName;
    }

    private bool IsBackgroundSchedulerPackAllowed(
        ThreadBackgroundWorkItem item,
        ToolDefinition toolDefinition,
        out string effectivePackId,
        out string reason) {
        effectivePackId = ResolveBackgroundSchedulerTargetPackId(item, toolDefinition);
        var blockedPackIds = _backgroundSchedulerControlState.GetBlockedPackIds(DateTime.UtcNow.Ticks);
        if (effectivePackId.Length > 0 && blockedPackIds.Contains(effectivePackId, StringComparer.OrdinalIgnoreCase)) {
            reason = "background_work_target_pack_blocked";
            return false;
        }

        var allowedPackIds = NormalizeBackgroundSchedulerPackIds(_options.BackgroundSchedulerAllowedPackIds);
        if (allowedPackIds.Length > 0 && !allowedPackIds.Contains(effectivePackId, StringComparer.OrdinalIgnoreCase)) {
            reason = "background_work_target_pack_not_allowed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private bool IsBackgroundSchedulerThreadAllowed(string? threadId, out string reason) {
        var effectiveThreadId = NormalizeBackgroundSchedulerThreadId(threadId);
        var blockedThreadIds = _backgroundSchedulerControlState.GetBlockedThreadIds(DateTime.UtcNow.Ticks);
        if (effectiveThreadId.Length > 0 && blockedThreadIds.Contains(effectiveThreadId, StringComparer.Ordinal)) {
            reason = "background_work_thread_blocked";
            return false;
        }

        var allowedThreadIds = NormalizeBackgroundSchedulerThreadIds(_options.BackgroundSchedulerAllowedThreadIds);
        if (allowedThreadIds.Length > 0 && !allowedThreadIds.Contains(effectiveThreadId, StringComparer.Ordinal)) {
            reason = "background_work_thread_not_allowed";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static SessionCapabilityBackgroundSchedulerActivityDto BuildBackgroundSchedulerActivity(
        BackgroundSchedulerIterationResult result,
        long recordedUtcTicks) {
        return new SessionCapabilityBackgroundSchedulerActivityDto {
            RecordedUtcTicks = Math.Max(0, recordedUtcTicks),
            Outcome = NormalizeBackgroundSchedulerOutcome(result.Outcome),
            ThreadId = NormalizeBackgroundSchedulerActivityText(result.ThreadId, maxLength: 120),
            ItemId = NormalizeBackgroundSchedulerActivityText(result.ItemId, maxLength: 160),
            ToolName = NormalizeBackgroundSchedulerActivityText(result.ToolName, maxLength: 120),
            Reason = NormalizeBackgroundSchedulerActivityText(result.Reason, maxLength: 160),
            OutputCount = Math.Max(0, result.OutputCount),
            FailureDetail = NormalizeBackgroundSchedulerActivityText(result.FailureDetail, MaxBackgroundSchedulerActivityDetailLength)
        };
    }

    private SessionCapabilityBackgroundSchedulerThreadSummaryDto BuildBackgroundSchedulerThreadSummary(
        string threadId,
        ThreadBackgroundWorkSnapshot snapshot,
        IReadOnlyList<ToolDefinition> toolDefinitions) {
        var dependencySummary = BuildBackgroundWorkDependencySummary(snapshot.Items);
        var dependencyRecoverySummary = BuildBackgroundWorkDependencyRecoverySummary(snapshot.Items, toolDefinitions);
        var dependencyRecoveryReason = ResolveBackgroundWorkDependencyRecoveryReason(dependencyRecoverySummary);
        var dependencyNextAction = ResolveBackgroundWorkDependencyNextAction(dependencyRecoverySummary);
        var continuationHint = BuildBackgroundSchedulerContinuationHint(
            threadId,
            dependencySummary.BlockedItemCount,
            dependencyRecoverySummary);
        return new SessionCapabilityBackgroundSchedulerThreadSummaryDto {
            ThreadId = NormalizeBackgroundSchedulerActivityText(threadId, maxLength: 120),
            QueuedItemCount = Math.Max(0, snapshot.QueuedCount),
            DependencyBlockedItemCount = Math.Max(0, dependencySummary.BlockedItemCount),
            ReadyItemCount = Math.Max(0, snapshot.ReadyCount),
            RunningItemCount = Math.Max(0, snapshot.RunningCount),
            CompletedItemCount = Math.Max(0, snapshot.CompletedCount),
            PendingReadOnlyItemCount = Math.Max(0, snapshot.PendingReadOnlyCount),
            PendingUnknownItemCount = Math.Max(0, snapshot.PendingUnknownCount),
            RecentEvidenceTools = NormalizeDistinctStrings(
                (snapshot.RecentEvidenceTools ?? Array.Empty<string>())
                .Select(static toolName => NormalizeBackgroundSchedulerActivityText(toolName, maxLength: 80))
                .Where(static toolName => toolName.Length > 0),
                MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyHelperToolNames = NormalizeDistinctStrings(
                dependencySummary.HelperToolNames
                    .Select(static toolName => NormalizeBackgroundSchedulerActivityText(toolName, maxLength: 80))
                    .Where(static toolName => toolName.Length > 0),
                MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyRecoveryReason = NormalizeBackgroundSchedulerActivityText(dependencyRecoveryReason, maxLength: 80),
            DependencyNextAction = NormalizeBackgroundSchedulerActivityText(dependencyNextAction, maxLength: 80),
            ContinuationHint = continuationHint,
            DependencyRetryCooldownHelperToolNames = NormalizeDistinctStrings(
                dependencyRecoverySummary.RetryCooldownHelperToolNames
                    .Select(static toolName => NormalizeBackgroundSchedulerActivityText(toolName, maxLength: 80))
                    .Where(static toolName => toolName.Length > 0),
                MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyAuthenticationHelperToolNames = NormalizeDistinctStrings(
                dependencyRecoverySummary.AuthenticationHelperToolNames
                    .Select(static toolName => NormalizeBackgroundSchedulerActivityText(toolName, maxLength: 80))
                    .Where(static toolName => toolName.Length > 0),
                MaxBackgroundSchedulerRecentEvidenceTools),
            DependencyAuthenticationArgumentNames = NormalizeDistinctStrings(
                dependencyRecoverySummary.AuthenticationArgumentNames
                    .Select(static argumentName => NormalizeBackgroundSchedulerActivityText(argumentName, maxLength: 64))
                    .Where(static argumentName => argumentName.Length > 0),
                4),
            DependencySetupHelperToolNames = NormalizeDistinctStrings(
                dependencyRecoverySummary.SetupHelperToolNames
                    .Select(static toolName => NormalizeBackgroundSchedulerActivityText(toolName, maxLength: 80))
                    .Where(static toolName => toolName.Length > 0),
                MaxBackgroundSchedulerRecentEvidenceTools)
        };
    }

    private static SessionCapabilityBackgroundSchedulerContinuationHintDto? BuildBackgroundSchedulerContinuationHint(
        string threadId,
        int dependencyBlockedItemCount,
        BackgroundWorkDependencyRecoverySummary summary) {
        if (dependencyBlockedItemCount <= 0) {
            return null;
        }

        var normalizedThreadId = NormalizeBackgroundSchedulerActivityText(threadId, maxLength: 120);
        var helperToolNames = NormalizeDistinctStrings(
            summary.HelperToolNames
                .Select(static toolName => NormalizeBackgroundSchedulerActivityText(toolName, maxLength: 80))
                .Where(static toolName => toolName.Length > 0),
            MaxBackgroundSchedulerRecentEvidenceTools);
        var inputArgumentNames = NormalizeDistinctStrings(
            summary.AuthenticationArgumentNames
                .Select(static argumentName => NormalizeBackgroundSchedulerActivityText(argumentName, maxLength: 64))
                .Where(static argumentName => argumentName.Length > 0),
            4);

        var recoveryReason = ResolveBackgroundWorkDependencyRecoveryReason(summary);
        var nextAction = ResolveBackgroundWorkDependencyNextAction(summary);
        if (recoveryReason.Length == 0) {
            recoveryReason = "background_prerequisite_pending";
        }

        if (nextAction.Length == 0) {
            nextAction = "wait_for_prerequisites";
        }

        return new SessionCapabilityBackgroundSchedulerContinuationHintDto {
            ThreadId = normalizedThreadId,
            NextAction = NormalizeBackgroundSchedulerActivityText(nextAction, maxLength: 80),
            RecoveryReason = NormalizeBackgroundSchedulerActivityText(recoveryReason, maxLength: 80),
            HelperToolNames = helperToolNames,
            InputArgumentNames = inputArgumentNames,
            SuggestedRequests = BuildBackgroundSchedulerContinuationRequests(
                normalizedThreadId,
                nextAction,
                helperToolNames,
                inputArgumentNames),
            StatusSummary = BuildBackgroundSchedulerContinuationHintStatusSummary(nextAction, helperToolNames, inputArgumentNames)
        };
    }

    private static SessionCapabilityBackgroundSchedulerContinuationRequestDto[] BuildBackgroundSchedulerContinuationRequests(
        string threadId,
        string? nextAction,
        string[] helperToolNames,
        string[] inputArgumentNames) {
        var normalizedThreadId = NormalizeBackgroundSchedulerActivityText(threadId, maxLength: 120);
        var normalizedNextAction = NormalizeBackgroundSchedulerActivityText(nextAction, maxLength: 80);
        var normalizedHelperToolNames = helperToolNames ?? Array.Empty<string>();
        var normalizedInputArgumentNames = inputArgumentNames ?? Array.Empty<string>();

        if (string.Equals(normalizedNextAction, "request_runtime_auth_context", StringComparison.OrdinalIgnoreCase)) {
            return new[] {
                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                    RequestKind = "list_profiles",
                    Purpose = "discover_runtime_profiles"
                },
                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                    RequestKind = "set_profile",
                    Purpose = "apply_runtime_auth_context",
                    RequiredArgumentNames = new[] { "profileName" },
                    SatisfiesInputArgumentNames = normalizedInputArgumentNames,
                    SuggestedArguments = new[] {
                        new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
                            Name = "newThread",
                            Value = "false",
                            ValueKind = "boolean"
                        }
                    }
                }
            };
        }

        if (string.Equals(normalizedNextAction, "wait_for_helper_retry", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedNextAction, "wait_for_prerequisites", StringComparison.OrdinalIgnoreCase)) {
            var purpose = string.Equals(normalizedNextAction, "wait_for_helper_retry", StringComparison.OrdinalIgnoreCase)
                ? "refresh_helper_retry_status"
                : "refresh_blocked_thread_status";
            return new[] {
                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                    RequestKind = "get_background_scheduler_status",
                    Purpose = purpose,
                    SuggestedArguments = BuildBackgroundSchedulerStatusRequestTemplateArguments(normalizedThreadId)
                }
            };
        }

        if (string.Equals(normalizedNextAction, "request_setup_context", StringComparison.OrdinalIgnoreCase)) {
            return new[] {
                new SessionCapabilityBackgroundSchedulerContinuationRequestDto {
                    RequestKind = "get_background_scheduler_status",
                    Purpose = "refresh_setup_context_status",
                    SuggestedArguments = BuildBackgroundSchedulerStatusRequestTemplateArguments(normalizedThreadId)
                }
            };
        }

        return Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestDto>();
    }

    private static SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto[] BuildBackgroundSchedulerStatusRequestTemplateArguments(string normalizedThreadId) {
        var arguments = new List<SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto>(5);
        if (normalizedThreadId.Length > 0) {
            arguments.Add(new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
                Name = "threadId",
                Value = normalizedThreadId,
                ValueKind = "string"
            });
        }

        arguments.Add(new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
            Name = "includeRecentActivity",
            Value = "true",
            ValueKind = "boolean"
        });
        arguments.Add(new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
            Name = "includeThreadSummaries",
            Value = "true",
            ValueKind = "boolean"
        });
        arguments.Add(new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
            Name = "maxRecentActivity",
            Value = "8",
            ValueKind = "number"
        });
        arguments.Add(new SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto {
            Name = "maxThreadSummaries",
            Value = "1",
            ValueKind = "number"
        });
        return arguments.ToArray();
    }

    private static string BuildBackgroundSchedulerContinuationHintStatusSummary(
        string? nextAction,
        string[] helperToolNames,
        string[] inputArgumentNames) {
        var normalizedNextAction = NormalizeBackgroundSchedulerActivityText(nextAction, maxLength: 80);
        var normalizedHelperToolNames = helperToolNames ?? Array.Empty<string>();
        var normalizedInputArgumentNames = inputArgumentNames ?? Array.Empty<string>();

        if (string.Equals(normalizedNextAction, "request_runtime_auth_context", StringComparison.OrdinalIgnoreCase)) {
            return normalizedInputArgumentNames.Length > 0
                ? "Waiting on runtime auth context: " + string.Join(", ", normalizedInputArgumentNames) + "."
                : "Waiting on runtime auth context for blocked background work.";
        }

        if (string.Equals(normalizedNextAction, "request_setup_context", StringComparison.OrdinalIgnoreCase)) {
            return normalizedHelperToolNames.Length > 0
                ? "Waiting on setup context for: " + string.Join(", ", normalizedHelperToolNames) + "."
                : "Waiting on setup context for blocked background work.";
        }

        if (string.Equals(normalizedNextAction, "wait_for_helper_retry", StringComparison.OrdinalIgnoreCase)) {
            return normalizedHelperToolNames.Length > 0
                ? "Waiting on helper retry: " + string.Join(", ", normalizedHelperToolNames) + "."
                : "Waiting on prerequisite helper retry.";
        }

        return normalizedHelperToolNames.Length > 0
            ? "Waiting on prerequisites: " + string.Join(", ", normalizedHelperToolNames) + "."
            : "Waiting on prerequisite helpers.";
    }

    private static string BuildBackgroundSchedulerActivitySummary(SessionCapabilityBackgroundSchedulerActivityDto activity) {
        ArgumentNullException.ThrowIfNull(activity);

        var summary = new List<string>(4) {
            activity.Outcome
        };
        if (activity.ToolName.Length > 0) {
            summary.Add("tool=" + activity.ToolName);
        }

        if (activity.ThreadId.Length > 0) {
            summary.Add("thread=" + activity.ThreadId);
        }

        if (activity.FailureDetail.Length > 0) {
            summary.Add("failure=" + activity.FailureDetail);
        }

        return string.Join(" ", summary);
    }

    private static string BuildBackgroundSchedulerThreadSummaryText(SessionCapabilityBackgroundSchedulerThreadSummaryDto summary) {
        ArgumentNullException.ThrowIfNull(summary);

        var builder = new List<string>(6) {
            summary.ThreadId
        };
        builder.Add("ready=" + summary.ReadyItemCount);
        builder.Add("running=" + summary.RunningItemCount);
        builder.Add("queued=" + summary.QueuedItemCount);
        if (summary.DependencyBlockedItemCount > 0) {
            builder.Add("blocked_dep=" + summary.DependencyBlockedItemCount);
        }
        if (summary.PendingReadOnlyItemCount > 0) {
            builder.Add("pending_ro=" + summary.PendingReadOnlyItemCount);
        }

        if (summary.DependencyHelperToolNames.Length > 0) {
            builder.Add("waiting_on=" + string.Join(",", summary.DependencyHelperToolNames));
        }

        if (summary.DependencyRecoveryReason.Length > 0) {
            builder.Add("blocked_reason=" + summary.DependencyRecoveryReason);
        }

        if (summary.DependencyNextAction.Length > 0) {
            builder.Add("next_action=" + summary.DependencyNextAction);
        }

        if (summary.DependencyAuthenticationArgumentNames.Length > 0) {
            builder.Add("auth_args=" + string.Join(",", summary.DependencyAuthenticationArgumentNames));
        }

        if (summary.DependencySetupHelperToolNames.Length > 0) {
            builder.Add("setup=" + string.Join(",", summary.DependencySetupHelperToolNames));
        }

        if (summary.RecentEvidenceTools.Length > 0) {
            builder.Add("evidence=" + string.Join(",", summary.RecentEvidenceTools));
        }

        return string.Join(" ", builder);
    }

    private static string NormalizeBackgroundSchedulerActivityText(string? value, int maxLength) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (maxLength > 0 && normalized.Length > maxLength) {
            return normalized[..maxLength].TrimEnd();
        }

        return normalized;
    }

    private static string ResolveBackgroundSchedulerTargetPackId(ThreadBackgroundWorkItem item, ToolDefinition toolDefinition) {
        var targetPackId = ToolPackBootstrap.NormalizePackId(item.TargetPackId);
        if (targetPackId.Length > 0) {
            return targetPackId;
        }

        return ToolPackBootstrap.NormalizePackId(toolDefinition.Routing?.PackId);
    }

    private static string[] NormalizeBackgroundSchedulerPackIds(IEnumerable<string> packIds) {
        return NormalizeDistinctStrings(
            (packIds ?? Array.Empty<string>())
            .Select(static packId => ToolPackBootstrap.NormalizePackId(packId))
            .Where(static packId => packId.Length > 0),
            maxItems: 0);
    }

    private static string NormalizeBackgroundSchedulerThreadId(string? threadId) {
        return (threadId ?? string.Empty).Trim();
    }

    private static string[] NormalizeBackgroundSchedulerThreadIds(IEnumerable<string> threadIds) {
        return NormalizeDistinctStrings(
            (threadIds ?? Array.Empty<string>())
            .Select(static threadId => NormalizeBackgroundSchedulerThreadId(threadId))
            .Where(static threadId => threadId.Length > 0),
            maxItems: 0);
    }

    private bool TryReleaseScheduledBackgroundWorkReplayCandidate(string threadId, string itemId) {
        var normalizedThreadId = NormalizeBackgroundSchedulerThreadId(threadId);
        var normalizedItemId = (itemId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedItemId.Length == 0) {
            return false;
        }

        if (!TryGetThreadBackgroundWorkItem(normalizedThreadId, normalizedItemId, out var item)
            || !string.Equals(item.State, BackgroundWorkStateRunning, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return TrySetThreadBackgroundWorkItemState(normalizedThreadId, normalizedItemId, BackgroundWorkStateReady, executionCallId: item.LastExecutionCallId);
    }

    private string[] EnumerateTrackedBackgroundWorkThreadIds() {
        var threadIds = new HashSet<string>(StringComparer.Ordinal);

        lock (_threadBackgroundWorkLock) {
            var nowUtc = DateTime.UtcNow;
            foreach (var pair in _threadBackgroundWorkSeenUtcTicksByThreadId) {
                if (pair.Value <= 0
                    || !TryGetUtcDateTimeFromTicks(pair.Value, out var seenUtc)
                    || seenUtc > nowUtc
                    || nowUtc - seenUtc > ThreadBackgroundWorkContextMaxAge
                    || !_threadBackgroundWorkByThreadId.TryGetValue(pair.Key, out var snapshot)
                    || IsEmptyBackgroundWorkSnapshot(snapshot)) {
                    continue;
                }

                threadIds.Add(pair.Key);
            }
        }

        foreach (var persisted in LoadAllPersistedThreadBackgroundWorkSnapshots()) {
            if (persisted.ThreadId.Length == 0 || IsEmptyBackgroundWorkSnapshot(persisted.Snapshot)) {
                continue;
            }

            threadIds.Add(persisted.ThreadId);
        }

        return threadIds
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
    }

    private void RememberBackgroundSchedulerTick(long? utcTicks = null) {
        var resolvedTicks = utcTicks.GetValueOrDefault(DateTime.UtcNow.Ticks);
        if (resolvedTicks <= 0) {
            return;
        }

        Interlocked.Exchange(ref _backgroundSchedulerLastTickUtcTicks, resolvedTicks);
    }
}
