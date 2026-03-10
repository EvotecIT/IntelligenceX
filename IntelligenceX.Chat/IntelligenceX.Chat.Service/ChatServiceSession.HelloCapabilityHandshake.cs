using System;
using System.Collections.Generic;
using System.Linq;
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

        if (snapshot.RoutingFamilies.Length > 0) {
            warning.Append(" routing_families='").Append(string.Join(",", snapshot.RoutingFamilies)).Append('\'');
        }

        if (snapshot.Skills.Length > 0) {
            warning.Append(" skills='").Append(string.Join(",", snapshot.Skills)).Append('\'');
        }

        if (snapshot.HealthyTools.Length > 0) {
            warning.Append(" healthy_tools='").Append(string.Join(",", snapshot.HealthyTools)).Append('\'');
        }
        var parityAttention = ToolCapabilityParityInventoryBuilder.BuildAttentionSummaries(snapshot.ParityEntries, maxItems: 3);
        if (parityAttention.Count > 0) {
            warning.Append(" parity_attention='").Append(string.Join("|", parityAttention)).Append('\'');
        }

        return warning.ToString();
    }

    private string ResolveHelloRemoteReachabilityMode() {
        if (_routingCatalogDiagnostics.TotalTools <= 0) {
            return "none";
        }

        var definitions = _registry.GetDefinitions();
        if (definitions.Count == 0) {
            return "none";
        }

        var hasRemoteCapableScope = definitions.Any(static definition =>
            HasToolScopeTag(definition.Tags, "domain")
            || HasToolScopeTag(definition.Tags, "network")
            || HasToolScopeTag(definition.Tags, "external"));
        return hasRemoteCapableScope
            ? "remote_capable"
            : "local_only";
    }

    private static bool HasToolScopeTag(IReadOnlyList<string>? tags, string scope) {
        if (tags is null || tags.Count == 0) {
            return false;
        }

        var expected = "scope:" + (scope ?? string.Empty).Trim();
        if (expected.Length <= "scope:".Length) {
            return false;
        }

        for (var i = 0; i < tags.Count; i++) {
            if (string.Equals((tags[i] ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
