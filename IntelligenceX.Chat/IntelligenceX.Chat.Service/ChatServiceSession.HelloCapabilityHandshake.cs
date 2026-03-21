using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string StartupCapabilityHandshakePrefix = "[startup] capability_handshake ";

    private string[] BuildHelloStartupWarnings(Task startupToolingBootstrapTask) {
        var warnings = new List<string>(_startupWarnings.Length + 2);
        warnings.AddRange(_startupWarnings);
        if (!startupToolingBootstrapTask.IsCompletedSuccessfully) {
            if (!startupToolingBootstrapTask.IsCompleted) {
                warnings.Add("[startup] Tool bootstrap in progress. Tool metadata may be incomplete.");
            } else if (startupToolingBootstrapTask.IsFaulted) {
                var detail = (startupToolingBootstrapTask.Exception?.GetBaseException().Message ?? "Tool bootstrap failed.").Trim();
                if (detail.Length == 0) {
                    detail = "Tool bootstrap failed.";
                }

                warnings.Add("[startup] Tool bootstrap failed: " + detail);
            } else if (startupToolingBootstrapTask.IsCanceled) {
                warnings.Add("[startup] Tool bootstrap canceled before completion.");
            }
        }

        warnings.Add(BuildHelloCapabilityHandshakeWarning());
        return NormalizeDistinctStrings(warnings, maxItems: 64);
    }

    private string BuildHelloCapabilityHandshakeWarning() {
        var snapshot = BuildRuntimeCapabilitySnapshot();

        var warning = new StringBuilder(256);
        warning.Append(StartupCapabilityHandshakePrefix);
        warning.Append("marker='").Append(CapabilitySnapshotMarker).Append('\'');
        warning.Append(" enabled_pack_count='").Append(snapshot.EnabledPackCount).Append('\'');
        warning.Append(" plugin_count='").Append(snapshot.PluginCount).Append('\'');
        warning.Append(" enabled_plugin_count='").Append(snapshot.EnabledPluginCount).Append('\'');
        warning.Append(" registered_tools='").Append(snapshot.RegisteredTools).Append('\'');
        warning.Append(" allowed_roots='").Append(snapshot.AllowedRootCount).Append('\'');
        warning.Append(" tooling_available='").Append(snapshot.ToolingAvailable ? "true" : "false").Append('\'');
        warning.Append(" dangerous_tools_enabled='").Append(snapshot.DangerousToolsEnabled ? "true" : "false").Append('\'');
        warning.Append(" remote_reachability_mode='").Append(snapshot.RemoteReachabilityMode ?? "none").Append('\'');
        warning.Append(" autonomy_local_capable_tools='").Append(snapshot.Autonomy?.LocalCapableToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_remote_capable_tools='").Append(snapshot.Autonomy?.RemoteCapableToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_target_scoped_tools='").Append(snapshot.Autonomy?.TargetScopedToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_remote_host_targeting_tools='").Append(snapshot.Autonomy?.RemoteHostTargetingToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_environment_discover_tools='").Append(snapshot.Autonomy?.EnvironmentDiscoverToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_write_capable_tools='").Append(snapshot.Autonomy?.WriteCapableToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_governed_write_tools='").Append(snapshot.Autonomy?.GovernedWriteToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_auth_required_tools='").Append(snapshot.Autonomy?.AuthenticationRequiredToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_probe_capable_tools='").Append(snapshot.Autonomy?.ProbeCapableToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_cross_pack_handoff_tools='").Append(snapshot.Autonomy?.CrossPackHandoffToolCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_daemon_enabled='").Append(snapshot.BackgroundScheduler?.DaemonEnabled == true ? "true" : "false").Append('\'');
        warning.Append(" background_scheduler_auto_pause_enabled='").Append(snapshot.BackgroundScheduler?.AutoPauseEnabled == true ? "true" : "false").Append('\'');
        warning.Append(" background_scheduler_failure_threshold='").Append(snapshot.BackgroundScheduler?.FailureThreshold ?? 0).Append('\'');
        warning.Append(" background_scheduler_failure_pause_seconds='").Append(snapshot.BackgroundScheduler?.FailurePauseSeconds ?? 0).Append('\'');
        warning.Append(" background_scheduler_paused='").Append(snapshot.BackgroundScheduler?.Paused == true ? "true" : "false").Append('\'');
        warning.Append(" background_scheduler_ready_items='").Append(snapshot.BackgroundScheduler?.ReadyItemCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_running_items='").Append(snapshot.BackgroundScheduler?.RunningItemCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_tracked_threads='").Append(snapshot.BackgroundScheduler?.TrackedThreadCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_dependency_blocked_items='").Append(snapshot.BackgroundScheduler?.DependencyBlockedItemCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_dependency_blocked_threads='").Append(snapshot.BackgroundScheduler?.DependencyBlockedThreadCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_completed_executions='").Append(snapshot.BackgroundScheduler?.CompletedExecutionCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_requeued_executions='").Append(snapshot.BackgroundScheduler?.RequeuedExecutionCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_released_executions='").Append(snapshot.BackgroundScheduler?.ReleasedExecutionCount ?? 0).Append('\'');
        warning.Append(" background_scheduler_consecutive_failures='").Append(snapshot.BackgroundScheduler?.ConsecutiveFailureCount ?? 0).Append('\'');
        warning.Append(" skills_marker='").Append(SkillsSnapshotMarker).Append('\'');
        warning.Append(" skill_count='").Append(snapshot.Skills.Length).Append('\'');
        warning.Append(" parity_engine_count='").Append(snapshot.ParityEntries.Length).Append('\'');
        warning.Append(" parity_attention_count='").Append(snapshot.ParityAttentionCount).Append('\'');
        warning.Append(" parity_missing_readonly_capabilities='").Append(snapshot.ParityMissingCapabilityCount).Append('\'');
        if (snapshot.ToolingSnapshot is not null) {
            warning.Append(" tooling_snapshot_source='").Append(snapshot.ToolingSnapshot.Source ?? "unknown").Append('\'');
            warning.Append(" tooling_snapshot_pack_count='").Append(snapshot.ToolingSnapshot.Packs.Length).Append('\'');
            warning.Append(" tooling_snapshot_plugin_count='").Append(snapshot.ToolingSnapshot.Plugins.Length).Append('\'');
            var toolingPackDetails = BuildCapabilitySnapshotToolingPackDetails(snapshot.ToolingSnapshot, maxItems: 3);
            if (toolingPackDetails.Length > 0) {
                warning.Append(" tooling_snapshot_packs='").Append(string.Join("|", toolingPackDetails)).Append('\'');
            }

            var toolingPluginDetails = BuildCapabilitySnapshotToolingPluginDetails(snapshot.ToolingSnapshot, maxItems: 3);
            if (toolingPluginDetails.Length > 0) {
                warning.Append(" tooling_snapshot_plugins='").Append(string.Join("|", toolingPluginDetails)).Append('\'');
            }
        }

        if (snapshot.EnabledPackIds.Length > 0) {
            warning.Append(" enabled_packs='").Append(string.Join(",", snapshot.EnabledPackIds)).Append('\'');
        }

        if (snapshot.EnabledPluginIds.Length > 0) {
            warning.Append(" enabled_plugins='").Append(string.Join(",", snapshot.EnabledPluginIds)).Append('\'');
        }

        if (snapshot.DangerousPackIds.Length > 0) {
            warning.Append(" dangerous_packs='").Append(string.Join(",", snapshot.DangerousPackIds)).Append('\'');
        }

        if (snapshot.EnabledPackEngineIds.Length > 0) {
            warning.Append(" enabled_pack_engines='").Append(string.Join(",", snapshot.EnabledPackEngineIds)).Append('\'');
        }

        if (snapshot.EnabledCapabilityTags.Length > 0) {
            warning.Append(" enabled_capability_tags='").Append(string.Join(",", snapshot.EnabledCapabilityTags)).Append('\'');
        }

        if (snapshot.RoutingFamilies.Length > 0) {
            warning.Append(" routing_families='").Append(string.Join(",", snapshot.RoutingFamilies)).Append('\'');
        }

        if (snapshot.RepresentativeExamples.Length > 0) {
            warning.Append(" representative_examples='").Append(string.Join("|", snapshot.RepresentativeExamples)).Append('\'');
        }

        if (snapshot.CrossPackTargetPackDisplayNames.Length > 0) {
            warning.Append(" cross_pack_followup_targets='").Append(string.Join(",", snapshot.CrossPackTargetPackDisplayNames)).Append('\'');
        }

        if (snapshot.Skills.Length > 0) {
            warning.Append(" skills='").Append(string.Join(",", snapshot.Skills)).Append('\'');
        }

        if (snapshot.HealthyTools.Length > 0) {
            warning.Append(" healthy_tools='").Append(string.Join(",", snapshot.HealthyTools)).Append('\'');
        }
        if (snapshot.Autonomy?.LocalCapablePackIds is { Length: > 0 }) {
            warning.Append(" autonomy_local_capable_packs='").Append(string.Join(",", snapshot.Autonomy.LocalCapablePackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.RemoteCapablePackIds is { Length: > 0 }) {
            warning.Append(" autonomy_remote_capable_packs='").Append(string.Join(",", snapshot.Autonomy.RemoteCapablePackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.TargetScopedPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_target_scoped_packs='").Append(string.Join(",", snapshot.Autonomy.TargetScopedPackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.RemoteHostTargetingPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_remote_host_targeting_packs='").Append(string.Join(",", snapshot.Autonomy.RemoteHostTargetingPackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.EnvironmentDiscoverPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_environment_discover_packs='").Append(string.Join(",", snapshot.Autonomy.EnvironmentDiscoverPackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.WriteCapablePackIds is { Length: > 0 }) {
            warning.Append(" autonomy_write_capable_packs='").Append(string.Join(",", snapshot.Autonomy.WriteCapablePackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.GovernedWritePackIds is { Length: > 0 }) {
            warning.Append(" autonomy_governed_write_packs='").Append(string.Join(",", snapshot.Autonomy.GovernedWritePackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.AuthenticationRequiredPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_auth_required_packs='").Append(string.Join(",", snapshot.Autonomy.AuthenticationRequiredPackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.ProbeCapablePackIds is { Length: > 0 }) {
            warning.Append(" autonomy_probe_capable_packs='").Append(string.Join(",", snapshot.Autonomy.ProbeCapablePackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.CrossPackReadyPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_cross_pack_ready_packs='").Append(string.Join(",", snapshot.Autonomy.CrossPackReadyPackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.CrossPackTargetPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_cross_pack_targets='").Append(string.Join(",", snapshot.Autonomy.CrossPackTargetPackIds)).Append('\'');
        }
        if (snapshot.BackgroundScheduler is not null) {
            warning.Append(" background_scheduler_persistent_queue='").Append(snapshot.BackgroundScheduler.SupportsPersistentQueue ? "true" : "false").Append('\'');
            warning.Append(" background_scheduler_readonly_autoreplay='").Append(snapshot.BackgroundScheduler.SupportsReadOnlyAutoReplay ? "true" : "false").Append('\'');
            warning.Append(" background_scheduler_cross_thread='").Append(snapshot.BackgroundScheduler.SupportsCrossThreadScheduling ? "true" : "false").Append('\'');
            warning.Append(" background_scheduler_manual_pause_active='").Append(snapshot.BackgroundScheduler.ManualPauseActive ? "true" : "false").Append('\'');
            warning.Append(" background_scheduler_scheduled_pause_active='").Append(snapshot.BackgroundScheduler.ScheduledPauseActive ? "true" : "false").Append('\'');
            warning.Append(" background_scheduler_adaptive_idle_active='").Append(snapshot.BackgroundScheduler.AdaptiveIdleActive ? "true" : "false").Append('\'');
            if (!string.IsNullOrWhiteSpace(snapshot.BackgroundScheduler.LastOutcome)) {
                warning.Append(" background_scheduler_last_outcome='").Append(snapshot.BackgroundScheduler.LastOutcome).Append('\'');
            }
            if (snapshot.BackgroundScheduler.LastAdaptiveIdleUtcTicks > 0) {
                warning.Append(" background_scheduler_last_adaptive_idle_utc_ticks='").Append(snapshot.BackgroundScheduler.LastAdaptiveIdleUtcTicks).Append('\'');
            }
            if (snapshot.BackgroundScheduler.LastAdaptiveIdleDelaySeconds > 0) {
                warning.Append(" background_scheduler_last_adaptive_idle_delay_seconds='").Append(snapshot.BackgroundScheduler.LastAdaptiveIdleDelaySeconds).Append('\'');
            }
            if (!string.IsNullOrWhiteSpace(snapshot.BackgroundScheduler.LastAdaptiveIdleReason)) {
                warning.Append(" background_scheduler_last_adaptive_idle_reason='").Append(snapshot.BackgroundScheduler.LastAdaptiveIdleReason).Append('\'');
            }
            if (snapshot.BackgroundScheduler.PausedUntilUtcTicks > 0) {
                warning.Append(" background_scheduler_paused_until_utc_ticks='").Append(snapshot.BackgroundScheduler.PausedUntilUtcTicks).Append('\'');
            }
            if (!string.IsNullOrWhiteSpace(snapshot.BackgroundScheduler.PauseReason)) {
                warning.Append(" background_scheduler_pause_reason='").Append(snapshot.BackgroundScheduler.PauseReason).Append('\'');
            }
            if (snapshot.BackgroundScheduler.MaintenanceWindowSpecs.Length > 0) {
                warning.Append(" background_scheduler_maintenance_windows='").Append(string.Join(",", snapshot.BackgroundScheduler.MaintenanceWindowSpecs)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.ActiveMaintenanceWindowSpecs.Length > 0) {
                warning.Append(" background_scheduler_active_maintenance_windows='").Append(string.Join(",", snapshot.BackgroundScheduler.ActiveMaintenanceWindowSpecs)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.AllowedPackIds.Length > 0) {
                warning.Append(" background_scheduler_allowed_packs='").Append(string.Join(",", snapshot.BackgroundScheduler.AllowedPackIds)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.BlockedPackIds.Length > 0) {
                warning.Append(" background_scheduler_blocked_packs='").Append(string.Join(",", snapshot.BackgroundScheduler.BlockedPackIds)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.AllowedThreadIds.Length > 0) {
                warning.Append(" background_scheduler_allowed_threads='").Append(string.Join(",", snapshot.BackgroundScheduler.AllowedThreadIds)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.BlockedThreadIds.Length > 0) {
                warning.Append(" background_scheduler_blocked_threads='").Append(string.Join(",", snapshot.BackgroundScheduler.BlockedThreadIds)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.ReadyThreadIds.Length > 0) {
                warning.Append(" background_scheduler_ready_threads='").Append(string.Join(",", snapshot.BackgroundScheduler.ReadyThreadIds)).Append('\'');
            }
            if (snapshot.BackgroundScheduler.RecentActivity.Length > 0) {
                warning.Append(" background_scheduler_recent_activity='").Append(string.Join(
                    "|",
                    snapshot.BackgroundScheduler.RecentActivity
                        .Take(2)
                        .Select(BuildBackgroundSchedulerActivitySummary))).Append('\'');
            }
            if (snapshot.BackgroundScheduler.ThreadSummaries.Length > 0) {
                warning.Append(" background_scheduler_thread_summaries='").Append(string.Join(
                    "|",
                    snapshot.BackgroundScheduler.ThreadSummaries
                        .Take(2)
                        .Select(BuildBackgroundSchedulerThreadSummaryText))).Append('\'');
            }
        }
        var parityAttention = ToolCapabilityParityInventoryBuilder.BuildAttentionSummaries(snapshot.ParityEntries, maxItems: 3);
        if (parityAttention.Count > 0) {
            warning.Append(" parity_attention='").Append(string.Join("|", parityAttention)).Append('\'');
        }

        return warning.ToString();
    }

    private string ResolveHelloRemoteReachabilityMode() {
        if (_toolOrchestrationCatalog is { Count: > 0 }) {
            foreach (var entry in _toolOrchestrationCatalog.EntriesByToolName.Values) {
                if (entry is not null && IsRemoteReachabilityCandidate(entry)) {
                    return "remote_capable";
                }
            }

            return "local_only";
        }

        if (_routingCatalogDiagnostics.RemoteCapableTools > 0) {
            return "remote_capable";
        }

        if (_routingCatalogDiagnostics.TotalTools <= 0) {
            return "none";
        }

        return "local_only";
    }

    private static bool IsRemoteReachabilityCandidate(ToolOrchestrationCatalogEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.SupportsRemoteExecution
               || entry.SupportsRemoteHostTargeting
               || ToolExecutionScopes.IsRemoteCapable(entry.ExecutionScope);
    }
}
