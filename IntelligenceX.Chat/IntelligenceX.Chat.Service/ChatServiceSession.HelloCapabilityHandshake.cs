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
        warning.Append(" remote_reachability_mode='").Append(snapshot.RemoteReachabilityMode ?? "none").Append('\'');
        warning.Append(" autonomy_remote_capable_tools='").Append(snapshot.Autonomy?.RemoteCapableToolCount ?? 0).Append('\'');
        warning.Append(" autonomy_cross_pack_handoff_tools='").Append(snapshot.Autonomy?.CrossPackHandoffToolCount ?? 0).Append('\'');
        warning.Append(" skills_marker='").Append(SkillsSnapshotMarker).Append('\'');
        warning.Append(" skill_count='").Append(snapshot.Skills.Length).Append('\'');
        warning.Append(" parity_engine_count='").Append(snapshot.ParityEntries.Length).Append('\'');
        warning.Append(" parity_attention_count='").Append(snapshot.ParityAttentionCount).Append('\'');
        warning.Append(" parity_missing_readonly_capabilities='").Append(snapshot.ParityMissingCapabilityCount).Append('\'');

        if (snapshot.EnabledPackIds.Length > 0) {
            warning.Append(" enabled_packs='").Append(string.Join(",", snapshot.EnabledPackIds)).Append('\'');
        }

        if (snapshot.EnabledPluginIds.Length > 0) {
            warning.Append(" enabled_plugins='").Append(string.Join(",", snapshot.EnabledPluginIds)).Append('\'');
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

        if (snapshot.Skills.Length > 0) {
            warning.Append(" skills='").Append(string.Join(",", snapshot.Skills)).Append('\'');
        }

        if (snapshot.HealthyTools.Length > 0) {
            warning.Append(" healthy_tools='").Append(string.Join(",", snapshot.HealthyTools)).Append('\'');
        }
        if (snapshot.Autonomy?.RemoteCapablePackIds is { Length: > 0 }) {
            warning.Append(" autonomy_remote_capable_packs='").Append(string.Join(",", snapshot.Autonomy.RemoteCapablePackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.CrossPackReadyPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_cross_pack_ready_packs='").Append(string.Join(",", snapshot.Autonomy.CrossPackReadyPackIds)).Append('\'');
        }
        if (snapshot.Autonomy?.CrossPackTargetPackIds is { Length: > 0 }) {
            warning.Append(" autonomy_cross_pack_targets='").Append(string.Join(",", snapshot.Autonomy.CrossPackTargetPackIds)).Append('\'');
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
