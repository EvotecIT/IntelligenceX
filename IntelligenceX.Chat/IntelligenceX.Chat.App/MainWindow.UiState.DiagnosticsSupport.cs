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
                readyItemCount = item.ReadyItemCount,
                runningItemCount = item.RunningItemCount,
                completedItemCount = item.CompletedItemCount,
                pendingReadOnlyItemCount = item.PendingReadOnlyItemCount,
                pendingUnknownItemCount = item.PendingUnknownItemCount,
                recentEvidenceTools = item.RecentEvidenceTools ?? Array.Empty<string>()
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
            failureThreshold = scheduler.FailureThreshold,
            failurePauseSeconds = scheduler.FailurePauseSeconds,
            trackedThreadCount = scheduler.TrackedThreadCount,
            readyThreadCount = scheduler.ReadyThreadCount,
            runningThreadCount = scheduler.RunningThreadCount,
            queuedItemCount = scheduler.QueuedItemCount,
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

    private static string BuildBackgroundSchedulerSummaryText(SessionCapabilityBackgroundSchedulerDto scheduler) {
        ArgumentNullException.ThrowIfNull(scheduler);

        if (scheduler.Paused) {
            var reason = string.IsNullOrWhiteSpace(scheduler.PauseReason) ? "paused" : scheduler.PauseReason;
            return $"Paused: {reason}";
        }

        if (scheduler.ActiveMaintenanceWindowSpecs is { Length: > 0 }) {
            return $"Scoped maintenance active for {scheduler.ActiveMaintenanceWindowSpecs.Length} window(s).";
        }

        if (scheduler.ReadyItemCount > 0 || scheduler.RunningItemCount > 0) {
            return $"Ready={scheduler.ReadyItemCount}, running={scheduler.RunningItemCount}, tracked_threads={scheduler.TrackedThreadCount}.";
        }

        if (!scheduler.DaemonEnabled) {
            return "Background scheduler daemon is disabled.";
        }

        return "Background scheduler is idle.";
    }
}
