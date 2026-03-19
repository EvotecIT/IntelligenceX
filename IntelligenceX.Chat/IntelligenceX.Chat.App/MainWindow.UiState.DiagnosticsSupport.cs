using System;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static object? BuildRoutingCatalogState(SessionRoutingCatalogDiagnosticsDto? routingCatalog) {
        if (routingCatalog is null) {
            return null;
        }

        return new {
            totalTools = routingCatalog.TotalTools,
            routingAwareTools = routingCatalog.RoutingAwareTools,
            explicitRoutingTools = routingCatalog.ExplicitRoutingTools,
            inferredRoutingTools = routingCatalog.InferredRoutingTools,
            missingRoutingContractTools = routingCatalog.MissingRoutingContractTools,
            missingPackIdTools = routingCatalog.MissingPackIdTools,
            missingRoleTools = routingCatalog.MissingRoleTools,
            setupAwareTools = routingCatalog.SetupAwareTools,
            handoffAwareTools = routingCatalog.HandoffAwareTools,
            recoveryAwareTools = routingCatalog.RecoveryAwareTools,
            remoteCapableTools = routingCatalog.RemoteCapableTools,
            crossPackHandoffTools = routingCatalog.CrossPackHandoffTools,
            domainFamilyTools = routingCatalog.DomainFamilyTools,
            expectedDomainFamilyMissingTools = routingCatalog.ExpectedDomainFamilyMissingTools,
            domainFamilyMissingActionTools = routingCatalog.DomainFamilyMissingActionTools,
            actionWithoutFamilyTools = routingCatalog.ActionWithoutFamilyTools,
            familyActionConflictFamilies = routingCatalog.FamilyActionConflictFamilies,
            isHealthy = routingCatalog.IsHealthy,
            isExplicitRoutingReady = routingCatalog.IsExplicitRoutingReady,
            familyActions = Array.ConvertAll(
                routingCatalog.FamilyActions,
                static item => new {
                    family = item.Family,
                    actionId = item.ActionId,
                    toolCount = item.ToolCount
                }),
            autonomyReadinessHighlights = routingCatalog.AutonomyReadinessHighlights ?? Array.Empty<string>()
        };
    }

    private static object? BuildCapabilitySnapshotState(SessionCapabilitySnapshotDto? capabilitySnapshot) {
        if (capabilitySnapshot is null) {
            return null;
        }

        return new {
            registeredTools = capabilitySnapshot.RegisteredTools,
            enabledPackCount = capabilitySnapshot.EnabledPackCount,
            pluginCount = capabilitySnapshot.PluginCount,
            enabledPluginCount = capabilitySnapshot.EnabledPluginCount,
            toolingAvailable = capabilitySnapshot.ToolingAvailable,
            allowedRootCount = capabilitySnapshot.AllowedRootCount,
            enabledPackIds = capabilitySnapshot.EnabledPackIds ?? Array.Empty<string>(),
            enabledPluginIds = capabilitySnapshot.EnabledPluginIds ?? Array.Empty<string>(),
            routingFamilies = capabilitySnapshot.RoutingFamilies ?? Array.Empty<string>(),
            familyActions = Array.ConvertAll(
                capabilitySnapshot.FamilyActions ?? Array.Empty<SessionRoutingFamilyActionSummaryDto>(),
                static item => new {
                    family = item.Family,
                    actionId = item.ActionId,
                    toolCount = item.ToolCount
                }),
            skills = capabilitySnapshot.Skills ?? Array.Empty<string>(),
            healthyTools = capabilitySnapshot.HealthyTools ?? Array.Empty<string>(),
            remoteReachabilityMode = capabilitySnapshot.RemoteReachabilityMode,
            autonomy = capabilitySnapshot.Autonomy is null ? null : new {
                remoteCapableToolCount = capabilitySnapshot.Autonomy.RemoteCapableToolCount,
                setupAwareToolCount = capabilitySnapshot.Autonomy.SetupAwareToolCount,
                handoffAwareToolCount = capabilitySnapshot.Autonomy.HandoffAwareToolCount,
                recoveryAwareToolCount = capabilitySnapshot.Autonomy.RecoveryAwareToolCount,
                crossPackHandoffToolCount = capabilitySnapshot.Autonomy.CrossPackHandoffToolCount,
                remoteCapablePackIds = capabilitySnapshot.Autonomy.RemoteCapablePackIds ?? Array.Empty<string>(),
                crossPackReadyPackIds = capabilitySnapshot.Autonomy.CrossPackReadyPackIds ?? Array.Empty<string>(),
                crossPackTargetPackIds = capabilitySnapshot.Autonomy.CrossPackTargetPackIds ?? Array.Empty<string>()
            },
            backgroundScheduler = BuildBackgroundSchedulerState(capabilitySnapshot.BackgroundScheduler),
            parityAttentionCount = capabilitySnapshot.ParityAttentionCount,
            parityMissingCapabilityCount = capabilitySnapshot.ParityMissingCapabilityCount
        };
    }

    private static object? BuildBackgroundSchedulerState(SessionCapabilityBackgroundSchedulerDto? scheduler) {
        if (scheduler is null) {
            return null;
        }

        var activeMaintenanceWindows = Array.ConvertAll(
            scheduler.ActiveMaintenanceWindows ?? Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>(),
            static item => new {
                spec = item.Spec,
                day = item.Day,
                startTimeLocal = item.StartTimeLocal,
                durationMinutes = item.DurationMinutes,
                packId = item.PackId,
                threadId = item.ThreadId,
                scoped = item.Scoped
            });
        var maintenanceWindows = Array.ConvertAll(
            scheduler.MaintenanceWindows ?? Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>(),
            static item => new {
                spec = item.Spec,
                day = item.Day,
                startTimeLocal = item.StartTimeLocal,
                durationMinutes = item.DurationMinutes,
                packId = item.PackId,
                threadId = item.ThreadId,
                scoped = item.Scoped
            });
        var blockedPackSuppressions = Array.ConvertAll(
            scheduler.BlockedPackSuppressions ?? Array.Empty<SessionCapabilityBackgroundSchedulerSuppressionDto>(),
            static item => new {
                id = item.Id,
                mode = item.Mode,
                temporary = item.Temporary,
                expiresUtcTicks = item.ExpiresUtcTicks
            });
        var blockedThreadSuppressions = Array.ConvertAll(
            scheduler.BlockedThreadSuppressions ?? Array.Empty<SessionCapabilityBackgroundSchedulerSuppressionDto>(),
            static item => new {
                id = item.Id,
                mode = item.Mode,
                temporary = item.Temporary,
                expiresUtcTicks = item.ExpiresUtcTicks
            });
        var recentActivity = Array.ConvertAll(
            scheduler.RecentActivity ?? Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>(),
            static item => new {
                recordedUtcTicks = item.RecordedUtcTicks,
                outcome = item.Outcome,
                threadId = item.ThreadId,
                itemId = item.ItemId,
                toolName = item.ToolName,
                reason = item.Reason,
                outputCount = item.OutputCount,
                failureDetail = item.FailureDetail
            });
        var threadSummaries = Array.ConvertAll(
            scheduler.ThreadSummaries ?? Array.Empty<SessionCapabilityBackgroundSchedulerThreadSummaryDto>(),
            static item => new {
                threadId = item.ThreadId,
                queuedItemCount = item.QueuedItemCount,
                dependencyBlockedItemCount = item.DependencyBlockedItemCount,
                readyItemCount = item.ReadyItemCount,
                runningItemCount = item.RunningItemCount,
                completedItemCount = item.CompletedItemCount,
                pendingReadOnlyItemCount = item.PendingReadOnlyItemCount,
                pendingUnknownItemCount = item.PendingUnknownItemCount,
                recentEvidenceTools = item.RecentEvidenceTools ?? Array.Empty<string>(),
                dependencyHelperToolNames = item.DependencyHelperToolNames ?? Array.Empty<string>(),
                dependencyRecoveryReason = item.DependencyRecoveryReason,
                dependencyNextAction = item.DependencyNextAction,
                dependencyRetryCooldownHelperToolNames = item.DependencyRetryCooldownHelperToolNames ?? Array.Empty<string>(),
                dependencyAuthenticationHelperToolNames = item.DependencyAuthenticationHelperToolNames ?? Array.Empty<string>(),
                dependencyAuthenticationArgumentNames = item.DependencyAuthenticationArgumentNames ?? Array.Empty<string>(),
                dependencySetupHelperToolNames = item.DependencySetupHelperToolNames ?? Array.Empty<string>(),
                continuationHint = item.ContinuationHint is null ? null : new {
                    threadId = item.ContinuationHint.ThreadId,
                    nextAction = item.ContinuationHint.NextAction,
                    recoveryReason = item.ContinuationHint.RecoveryReason,
                    helperToolNames = item.ContinuationHint.HelperToolNames ?? Array.Empty<string>(),
                    inputArgumentNames = item.ContinuationHint.InputArgumentNames ?? Array.Empty<string>(),
                    suggestedRequests = (item.ContinuationHint.SuggestedRequests ?? Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestDto>())
                        .Select(static request => new {
                            requestKind = request.RequestKind,
                            purpose = request.Purpose,
                            requiredArgumentNames = request.RequiredArgumentNames ?? Array.Empty<string>(),
                            satisfiesInputArgumentNames = request.SatisfiesInputArgumentNames ?? Array.Empty<string>(),
                            suggestedArguments = (request.SuggestedArguments ?? Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto>())
                                .Select(static argument => new {
                                    name = argument.Name,
                                    value = argument.Value,
                                    valueKind = argument.ValueKind
                                })
                                .ToArray()
                        })
                        .ToArray(),
                    statusSummary = item.ContinuationHint.StatusSummary
                },
                statusSummary = BuildBackgroundSchedulerThreadSummaryText(item)
            });

        return new {
            scopeThreadId = scheduler.ScopeThreadId,
            daemonEnabled = scheduler.DaemonEnabled,
            autoPauseEnabled = scheduler.AutoPauseEnabled,
            manualPauseActive = scheduler.ManualPauseActive,
            scheduledPauseActive = scheduler.ScheduledPauseActive,
            paused = scheduler.Paused,
            pausedUntilUtcTicks = scheduler.PausedUntilUtcTicks,
            pauseReason = scheduler.PauseReason,
            adaptiveIdleActive = scheduler.AdaptiveIdleActive,
            lastAdaptiveIdleUtcTicks = scheduler.LastAdaptiveIdleUtcTicks,
            lastAdaptiveIdleDelaySeconds = scheduler.LastAdaptiveIdleDelaySeconds,
            lastAdaptiveIdleReason = scheduler.LastAdaptiveIdleReason,
            failureThreshold = scheduler.FailureThreshold,
            failurePauseSeconds = scheduler.FailurePauseSeconds,
            trackedThreadCount = scheduler.TrackedThreadCount,
            readyThreadCount = scheduler.ReadyThreadCount,
            runningThreadCount = scheduler.RunningThreadCount,
            dependencyBlockedThreadCount = scheduler.DependencyBlockedThreadCount,
            queuedItemCount = scheduler.QueuedItemCount,
            dependencyBlockedItemCount = scheduler.DependencyBlockedItemCount,
            dependencyHelperToolNames = scheduler.DependencyHelperToolNames ?? Array.Empty<string>(),
            dependencyRecoveryReason = scheduler.DependencyRecoveryReason,
            dependencyNextAction = scheduler.DependencyNextAction,
            dependencyRetryCooldownHelperToolNames = scheduler.DependencyRetryCooldownHelperToolNames ?? Array.Empty<string>(),
            dependencyAuthenticationHelperToolNames = scheduler.DependencyAuthenticationHelperToolNames ?? Array.Empty<string>(),
            dependencyAuthenticationArgumentNames = scheduler.DependencyAuthenticationArgumentNames ?? Array.Empty<string>(),
            dependencySetupHelperToolNames = scheduler.DependencySetupHelperToolNames ?? Array.Empty<string>(),
            readyItemCount = scheduler.ReadyItemCount,
            runningItemCount = scheduler.RunningItemCount,
            completedItemCount = scheduler.CompletedItemCount,
            pendingReadOnlyItemCount = scheduler.PendingReadOnlyItemCount,
            pendingUnknownItemCount = scheduler.PendingUnknownItemCount,
            completedExecutionCount = scheduler.CompletedExecutionCount,
            requeuedExecutionCount = scheduler.RequeuedExecutionCount,
            releasedExecutionCount = scheduler.ReleasedExecutionCount,
            consecutiveFailureCount = scheduler.ConsecutiveFailureCount,
            lastOutcome = scheduler.LastOutcome,
            lastSchedulerTickUtcTicks = scheduler.LastSchedulerTickUtcTicks,
            lastOutcomeUtcTicks = scheduler.LastOutcomeUtcTicks,
            lastSuccessUtcTicks = scheduler.LastSuccessUtcTicks,
            lastFailureUtcTicks = scheduler.LastFailureUtcTicks,
            maintenanceWindowSpecs = scheduler.MaintenanceWindowSpecs ?? Array.Empty<string>(),
            activeMaintenanceWindowSpecs = scheduler.ActiveMaintenanceWindowSpecs ?? Array.Empty<string>(),
            maintenanceWindows,
            activeMaintenanceWindows,
            allowedPackIds = scheduler.AllowedPackIds ?? Array.Empty<string>(),
            blockedPackIds = scheduler.BlockedPackIds ?? Array.Empty<string>(),
            blockedPackSuppressions,
            allowedThreadIds = scheduler.AllowedThreadIds ?? Array.Empty<string>(),
            blockedThreadIds = scheduler.BlockedThreadIds ?? Array.Empty<string>(),
            blockedThreadSuppressions,
            readyThreadIds = scheduler.ReadyThreadIds ?? Array.Empty<string>(),
            runningThreadIds = scheduler.RunningThreadIds ?? Array.Empty<string>(),
            recentActivity,
            threadSummaries,
            statusSummary = BuildBackgroundSchedulerSummaryText(scheduler)
        };
    }

    private object? BuildPublishedBackgroundSchedulerRuntimeState(SessionCapabilityBackgroundSchedulerDto? scheduler) {
        if (scheduler is null) {
            return null;
        }

        var activeMaintenanceWindows = Array.ConvertAll(
            scheduler.ActiveMaintenanceWindows ?? Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>(),
            static item => new {
                spec = item.Spec,
                day = item.Day,
                startTimeLocal = item.StartTimeLocal,
                durationMinutes = item.DurationMinutes,
                packId = item.PackId,
                threadId = item.ThreadId,
                scoped = item.Scoped
            });
        var maintenanceWindows = Array.ConvertAll(
            scheduler.MaintenanceWindows ?? Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>(),
            static item => new {
                spec = item.Spec,
                day = item.Day,
                startTimeLocal = item.StartTimeLocal,
                durationMinutes = item.DurationMinutes,
                packId = item.PackId,
                threadId = item.ThreadId,
                scoped = item.Scoped
            });
        var blockedPackSuppressions = Array.ConvertAll(
            scheduler.BlockedPackSuppressions ?? Array.Empty<SessionCapabilityBackgroundSchedulerSuppressionDto>(),
            static item => new {
                id = item.Id,
                mode = item.Mode,
                temporary = item.Temporary,
                expiresUtcTicks = item.ExpiresUtcTicks
            });
        var blockedThreadSuppressions = Array.ConvertAll(
            scheduler.BlockedThreadSuppressions ?? Array.Empty<SessionCapabilityBackgroundSchedulerSuppressionDto>(),
            static item => new {
                id = item.Id,
                mode = item.Mode,
                temporary = item.Temporary,
                expiresUtcTicks = item.ExpiresUtcTicks
            });
        var recentActivity = Array.ConvertAll(
            scheduler.RecentActivity ?? Array.Empty<SessionCapabilityBackgroundSchedulerActivityDto>(),
            static item => new {
                recordedUtcTicks = item.RecordedUtcTicks,
                outcome = item.Outcome,
                threadId = item.ThreadId,
                itemId = item.ItemId,
                toolName = item.ToolName,
                reason = item.Reason,
                outputCount = item.OutputCount,
                failureDetail = item.FailureDetail
            });
        var threadSummaries = Array.ConvertAll(
            scheduler.ThreadSummaries ?? Array.Empty<SessionCapabilityBackgroundSchedulerThreadSummaryDto>(),
            item => {
                var continuationCommand = BuildPublishedBackgroundSchedulerContinuationCommand(scheduler, item);
                return new {
                    threadId = item.ThreadId,
                    queuedItemCount = item.QueuedItemCount,
                    dependencyBlockedItemCount = item.DependencyBlockedItemCount,
                    readyItemCount = item.ReadyItemCount,
                    runningItemCount = item.RunningItemCount,
                    completedItemCount = item.CompletedItemCount,
                    pendingReadOnlyItemCount = item.PendingReadOnlyItemCount,
                    pendingUnknownItemCount = item.PendingUnknownItemCount,
                    recentEvidenceTools = item.RecentEvidenceTools ?? Array.Empty<string>(),
                    dependencyHelperToolNames = item.DependencyHelperToolNames ?? Array.Empty<string>(),
                    dependencyRecoveryReason = item.DependencyRecoveryReason,
                    dependencyNextAction = item.DependencyNextAction,
                    dependencyRetryCooldownHelperToolNames = item.DependencyRetryCooldownHelperToolNames ?? Array.Empty<string>(),
                    dependencyAuthenticationHelperToolNames = item.DependencyAuthenticationHelperToolNames ?? Array.Empty<string>(),
                    dependencyAuthenticationArgumentNames = item.DependencyAuthenticationArgumentNames ?? Array.Empty<string>(),
                    dependencySetupHelperToolNames = item.DependencySetupHelperToolNames ?? Array.Empty<string>(),
                    continuationHint = item.ContinuationHint is null ? null : new {
                        threadId = item.ContinuationHint.ThreadId,
                        nextAction = item.ContinuationHint.NextAction,
                        recoveryReason = item.ContinuationHint.RecoveryReason,
                        helperToolNames = item.ContinuationHint.HelperToolNames ?? Array.Empty<string>(),
                        inputArgumentNames = item.ContinuationHint.InputArgumentNames ?? Array.Empty<string>(),
                        suggestedRequests = (item.ContinuationHint.SuggestedRequests ?? Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestDto>())
                            .Select(static request => new {
                                requestKind = request.RequestKind,
                                purpose = request.Purpose,
                                requiredArgumentNames = request.RequiredArgumentNames ?? Array.Empty<string>(),
                                satisfiesInputArgumentNames = request.SatisfiesInputArgumentNames ?? Array.Empty<string>(),
                                suggestedArguments = (request.SuggestedArguments ?? Array.Empty<SessionCapabilityBackgroundSchedulerContinuationRequestArgumentDto>())
                                    .Select(static argument => new {
                                        name = argument.Name,
                                        value = argument.Value,
                                        valueKind = argument.ValueKind
                                    })
                                    .ToArray()
                            })
                            .ToArray(),
                        statusSummary = item.ContinuationHint.StatusSummary
                    },
                    continuationCommand,
                    statusSummary = BuildBackgroundSchedulerThreadSummaryText(item)
                };
            });

        return new {
            scopeThreadId = scheduler.ScopeThreadId,
            daemonEnabled = scheduler.DaemonEnabled,
            autoPauseEnabled = scheduler.AutoPauseEnabled,
            manualPauseActive = scheduler.ManualPauseActive,
            scheduledPauseActive = scheduler.ScheduledPauseActive,
            paused = scheduler.Paused,
            pausedUntilUtcTicks = scheduler.PausedUntilUtcTicks,
            pauseReason = scheduler.PauseReason,
            adaptiveIdleActive = scheduler.AdaptiveIdleActive,
            lastAdaptiveIdleUtcTicks = scheduler.LastAdaptiveIdleUtcTicks,
            lastAdaptiveIdleDelaySeconds = scheduler.LastAdaptiveIdleDelaySeconds,
            lastAdaptiveIdleReason = scheduler.LastAdaptiveIdleReason,
            failureThreshold = scheduler.FailureThreshold,
            failurePauseSeconds = scheduler.FailurePauseSeconds,
            trackedThreadCount = scheduler.TrackedThreadCount,
            readyThreadCount = scheduler.ReadyThreadCount,
            runningThreadCount = scheduler.RunningThreadCount,
            dependencyBlockedThreadCount = scheduler.DependencyBlockedThreadCount,
            queuedItemCount = scheduler.QueuedItemCount,
            dependencyBlockedItemCount = scheduler.DependencyBlockedItemCount,
            dependencyHelperToolNames = scheduler.DependencyHelperToolNames ?? Array.Empty<string>(),
            dependencyRecoveryReason = scheduler.DependencyRecoveryReason,
            dependencyNextAction = scheduler.DependencyNextAction,
            dependencyRetryCooldownHelperToolNames = scheduler.DependencyRetryCooldownHelperToolNames ?? Array.Empty<string>(),
            dependencyAuthenticationHelperToolNames = scheduler.DependencyAuthenticationHelperToolNames ?? Array.Empty<string>(),
            dependencyAuthenticationArgumentNames = scheduler.DependencyAuthenticationArgumentNames ?? Array.Empty<string>(),
            dependencySetupHelperToolNames = scheduler.DependencySetupHelperToolNames ?? Array.Empty<string>(),
            readyItemCount = scheduler.ReadyItemCount,
            runningItemCount = scheduler.RunningItemCount,
            completedItemCount = scheduler.CompletedItemCount,
            pendingReadOnlyItemCount = scheduler.PendingReadOnlyItemCount,
            pendingUnknownItemCount = scheduler.PendingUnknownItemCount,
            lastSchedulerTickUtcTicks = scheduler.LastSchedulerTickUtcTicks,
            lastOutcomeUtcTicks = scheduler.LastOutcomeUtcTicks,
            lastSuccessUtcTicks = scheduler.LastSuccessUtcTicks,
            lastFailureUtcTicks = scheduler.LastFailureUtcTicks,
            readyThreadIds = scheduler.ReadyThreadIds ?? Array.Empty<string>(),
            runningThreadIds = scheduler.RunningThreadIds ?? Array.Empty<string>(),
            blockedPackIds = scheduler.BlockedPackIds ?? Array.Empty<string>(),
            blockedPackSuppressions,
            blockedThreadIds = scheduler.BlockedThreadIds ?? Array.Empty<string>(),
            blockedThreadSuppressions,
            activeMaintenanceWindowSpecs = scheduler.ActiveMaintenanceWindowSpecs ?? Array.Empty<string>(),
            activeMaintenanceWindows,
            maintenanceWindows,
            recentActivity,
            threadSummaries,
            statusSummary = BuildBackgroundSchedulerSummaryText(scheduler)
        };
    }

    private object? BuildPublishedBackgroundSchedulerContinuationCommand(
        SessionCapabilityBackgroundSchedulerDto scheduler,
        SessionCapabilityBackgroundSchedulerThreadSummaryDto summary) {
        if (BuildBackgroundSchedulerContinuationPlan(
                scheduler,
                summary.ThreadId,
                _appProfileName,
                _serviceProfileNames,
                _serviceActiveProfileName) is not object plan) {
            return null;
        }

        var planType = plan.GetType();
        var missingArgumentNames = planType.GetProperty("MissingArgumentNames")?.GetValue(plan) as string[] ?? Array.Empty<string>();
        var threadId = planType.GetProperty("ThreadId")?.GetValue(plan) as string ?? summary.ThreadId;
        var nextAction = planType.GetProperty("NextAction")?.GetValue(plan) as string ?? string.Empty;
        var recoveryReason = planType.GetProperty("RecoveryReason")?.GetValue(plan) as string ?? string.Empty;
        var profileName = planType.GetProperty("ProfileName")?.GetValue(plan) as string;
        var statusSummary = planType.GetProperty("StatusSummary")?.GetValue(plan) as string ?? string.Empty;

        return new {
            command = "scheduler_continue_thread",
            threadId,
            enabled = missingArgumentNames.Length == 0,
            missingArgumentNames,
            nextAction,
            recoveryReason,
            profileName,
            statusSummary
        };
    }

    private static string BuildBackgroundSchedulerSummaryText(SessionCapabilityBackgroundSchedulerDto scheduler) {
        ArgumentNullException.ThrowIfNull(scheduler);

        if (scheduler.Paused && scheduler.ScheduledPauseActive) {
            var activeWindows = scheduler.ActiveMaintenanceWindows ?? Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>();
            var globalCount = activeWindows.Count(static item => !item.Scoped);
            if (globalCount <= 0) {
                globalCount = CountActiveMaintenanceSpecsByScope(scheduler.ActiveMaintenanceWindowSpecs, scoped: false);
            }

            if (globalCount > 0) {
                return $"Global maintenance active for {globalCount} window(s).";
            }
        }

        if (scheduler.Paused) {
            var reason = string.IsNullOrWhiteSpace(scheduler.PauseReason) ? "paused" : scheduler.PauseReason;
            return $"Paused: {reason}";
        }

        if (scheduler.ActiveMaintenanceWindowSpecs is { Length: > 0 }) {
            var activeWindows = scheduler.ActiveMaintenanceWindows ?? Array.Empty<SessionCapabilityBackgroundSchedulerMaintenanceWindowDto>();
            var scopedCount = activeWindows.Count(static item => item.Scoped);
            if (scopedCount <= 0) {
                scopedCount = CountActiveMaintenanceSpecsByScope(scheduler.ActiveMaintenanceWindowSpecs, scoped: true);
            }

            if (scopedCount > 0) {
                return $"Scoped maintenance active for {scopedCount} window(s).";
            }
        }

        if (scheduler.ReadyItemCount > 0 || scheduler.RunningItemCount > 0) {
            return $"Ready={scheduler.ReadyItemCount}, running={scheduler.RunningItemCount}, tracked_threads={scheduler.TrackedThreadCount}.";
        }

        if (scheduler.DependencyBlockedItemCount > 0) {
            if (string.Equals(scheduler.DependencyNextAction, "request_runtime_auth_context", StringComparison.OrdinalIgnoreCase)) {
                var authArgs = scheduler.DependencyAuthenticationArgumentNames ?? Array.Empty<string>();
                return authArgs.Length > 0
                    ? $"Waiting on runtime auth context: {string.Join(", ", authArgs)}."
                    : "Waiting on runtime auth context for blocked background work.";
            }

            if (string.Equals(scheduler.DependencyNextAction, "request_setup_context", StringComparison.OrdinalIgnoreCase)) {
                var setupHelpers = scheduler.DependencySetupHelperToolNames ?? Array.Empty<string>();
                return setupHelpers.Length > 0
                    ? $"Waiting on setup context for: {string.Join(", ", setupHelpers)}."
                    : "Waiting on setup context for blocked background work.";
            }

            if (string.Equals(scheduler.DependencyNextAction, "wait_for_helper_retry", StringComparison.OrdinalIgnoreCase)) {
                var retryHelpers = scheduler.DependencyRetryCooldownHelperToolNames ?? Array.Empty<string>();
                return retryHelpers.Length > 0
                    ? $"Waiting on helper retry: {string.Join(", ", retryHelpers)}."
                    : "Waiting on prerequisite helper retry.";
            }

            var helperTools = scheduler.DependencyHelperToolNames ?? Array.Empty<string>();
            return helperTools.Length > 0
                ? $"Waiting on prerequisites: {string.Join(", ", helperTools)}."
                : "Waiting on prerequisite helpers.";
        }

        if (!scheduler.DaemonEnabled) {
            return "Background scheduler daemon is disabled.";
        }

        if (scheduler.AdaptiveIdleActive && scheduler.LastAdaptiveIdleDelaySeconds > 0) {
            var reason = scheduler.LastAdaptiveIdleReason ?? string.Empty;
            return string.IsNullOrWhiteSpace(reason)
                ? $"Background scheduler is adaptively idle ({scheduler.LastAdaptiveIdleDelaySeconds}s poll)."
                : $"Background scheduler is adaptively idle ({scheduler.LastAdaptiveIdleDelaySeconds}s poll): {reason}";
        }

        return "Background scheduler is idle.";
    }

    private static string BuildBackgroundSchedulerThreadSummaryText(SessionCapabilityBackgroundSchedulerThreadSummaryDto summary) {
        ArgumentNullException.ThrowIfNull(summary);

        var helperReuseSuffix = BuildBackgroundSchedulerHelperReuseSummaryText(
            summary.ReusedHelperItemCount,
            summary.ReusedHelperToolNames,
            summary.ReusedHelperPolicyNames,
            summary.ReusedHelperFreshestAgeSeconds,
            summary.ReusedHelperOldestAgeSeconds,
            summary.ReusedHelperFreshestTtlSeconds,
            summary.ReusedHelperOldestTtlSeconds);
        if (summary.ReadyItemCount > 0 || summary.RunningItemCount > 0) {
            return helperReuseSuffix.Length > 0
                ? $"Ready={summary.ReadyItemCount}, running={summary.RunningItemCount}, queued={summary.QueuedItemCount}.{helperReuseSuffix}"
                : $"Ready={summary.ReadyItemCount}, running={summary.RunningItemCount}, queued={summary.QueuedItemCount}.";
        }

        if (summary.DependencyBlockedItemCount > 0) {
            if (string.Equals(summary.DependencyNextAction, "request_runtime_auth_context", StringComparison.OrdinalIgnoreCase)) {
                var authArgs = summary.DependencyAuthenticationArgumentNames ?? Array.Empty<string>();
                var status = authArgs.Length > 0
                    ? $"Waiting on runtime auth context: {string.Join(", ", authArgs)}."
                    : "Waiting on runtime auth context for blocked follow-up work.";
                return helperReuseSuffix.Length > 0
                    ? status + helperReuseSuffix
                    : status;
            }

            if (string.Equals(summary.DependencyNextAction, "request_setup_context", StringComparison.OrdinalIgnoreCase)) {
                var setupHelpers = summary.DependencySetupHelperToolNames ?? Array.Empty<string>();
                var status = setupHelpers.Length > 0
                    ? $"Waiting on setup context for: {string.Join(", ", setupHelpers)}."
                    : "Waiting on setup context for blocked follow-up work.";
                return helperReuseSuffix.Length > 0
                    ? status + helperReuseSuffix
                    : status;
            }

            if (string.Equals(summary.DependencyNextAction, "wait_for_helper_retry", StringComparison.OrdinalIgnoreCase)) {
                var retryHelpers = summary.DependencyRetryCooldownHelperToolNames ?? Array.Empty<string>();
                var status = retryHelpers.Length > 0
                    ? $"Waiting on helper retry: {string.Join(", ", retryHelpers)}."
                    : "Waiting on prerequisite helper retry.";
                return helperReuseSuffix.Length > 0
                    ? status + helperReuseSuffix
                    : status;
            }

            var helperTools = summary.DependencyHelperToolNames ?? Array.Empty<string>();
            var blockedStatus = helperTools.Length > 0
                ? $"Waiting on prerequisites: {string.Join(", ", helperTools)}."
                : "Waiting on prerequisite helpers.";
            return helperReuseSuffix.Length > 0
                ? blockedStatus + helperReuseSuffix
                : blockedStatus;
        }

        if (summary.QueuedItemCount > 0) {
            return helperReuseSuffix.Length > 0
                ? $"Queued={summary.QueuedItemCount}, completed={summary.CompletedItemCount}.{helperReuseSuffix}"
                : $"Queued={summary.QueuedItemCount}, completed={summary.CompletedItemCount}.";
        }

        return helperReuseSuffix.Length > 0
            ? $"Completed={summary.CompletedItemCount}.{helperReuseSuffix}"
            : $"Completed={summary.CompletedItemCount}.";
    }

    private static string BuildBackgroundSchedulerHelperReuseSummaryText(
        int reusedHelperItemCount,
        string[]? reusedHelperToolNames,
        string[]? reusedHelperPolicyNames,
        int? reusedHelperFreshestAgeSeconds,
        int? reusedHelperOldestAgeSeconds,
        int? reusedHelperFreshestTtlSeconds,
        int? reusedHelperOldestTtlSeconds) {
        if (reusedHelperItemCount <= 0) {
            return string.Empty;
        }

        var normalizedHelperToolNames = reusedHelperToolNames ?? Array.Empty<string>();
        var normalizedPolicyNames = reusedHelperPolicyNames ?? Array.Empty<string>();
        var prefix = normalizedHelperToolNames.Length > 0
            ? " Reused fresh prerequisite evidence: " + string.Join(", ", normalizedHelperToolNames)
            : reusedHelperItemCount == 1
                ? " Reused fresh prerequisite evidence for 1 helper"
                : " Reused fresh prerequisite evidence for " + reusedHelperItemCount + " helpers";
        var ageSummary = BuildBackgroundSchedulerHelperReuseAgeSummary(reusedHelperFreshestAgeSeconds, reusedHelperOldestAgeSeconds);
        var windowSummary = BuildBackgroundSchedulerHelperReuseAgeSummary(reusedHelperFreshestTtlSeconds, reusedHelperOldestTtlSeconds);
        var suffixParts = new List<string>(3);
        if (ageSummary.Length > 0) {
            suffixParts.Add(ageSummary);
        }

        if (windowSummary.Length > 0) {
            suffixParts.Add("window " + windowSummary);
        }

        if (normalizedPolicyNames.Length > 0) {
            suffixParts.Add("policy " + string.Join(", ", normalizedPolicyNames));
        }

        return suffixParts.Count > 0
            ? prefix + " (" + string.Join(", ", suffixParts) + ")."
            : prefix + ".";
    }

    private static string BuildBackgroundSchedulerHelperReuseAgeSummary(int? freshestAgeSeconds, int? oldestAgeSeconds) {
        if (!freshestAgeSeconds.HasValue || !oldestAgeSeconds.HasValue) {
            return string.Empty;
        }

        var normalizedFreshestAgeSeconds = Math.Max(0, freshestAgeSeconds.Value);
        var normalizedOldestAgeSeconds = Math.Max(normalizedFreshestAgeSeconds, oldestAgeSeconds.Value);
        return normalizedFreshestAgeSeconds == normalizedOldestAgeSeconds
            ? FormatBackgroundSchedulerHelperReuseAge(normalizedOldestAgeSeconds) + " old"
            : FormatBackgroundSchedulerHelperReuseAge(normalizedFreshestAgeSeconds)
              + "-"
              + FormatBackgroundSchedulerHelperReuseAge(normalizedOldestAgeSeconds)
              + " old";
    }

    private static string FormatBackgroundSchedulerHelperReuseAge(int ageSeconds) {
        var normalizedAgeSeconds = Math.Max(0, ageSeconds);
        if (normalizedAgeSeconds < 60) {
            return normalizedAgeSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "s";
        }

        if (normalizedAgeSeconds < 3600) {
            return (normalizedAgeSeconds / 60).ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
        }

        return (normalizedAgeSeconds / 3600).ToString(System.Globalization.CultureInfo.InvariantCulture) + "h";
    }

    private static int CountActiveMaintenanceSpecsByScope(string[]? specs, bool scoped) {
        if (specs is not { Length: > 0 }) {
            return 0;
        }

        return specs.Count(spec => IsScopedMaintenanceWindowSpec(spec) == scoped);
    }

    private static bool IsScopedMaintenanceWindowSpec(string? spec) {
        if (string.IsNullOrWhiteSpace(spec)) {
            return false;
        }

        return spec.Contains(";pack=", StringComparison.OrdinalIgnoreCase)
            || spec.Contains(";thread=", StringComparison.OrdinalIgnoreCase);
    }
}
