using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        var enabledPackIds = ResolveWorkingMemoryCapabilityEnabledPackIds(Array.Empty<string>());
        var routingFamilies = ResolveWorkingMemoryCapabilityRoutingFamilies(Array.Empty<string>());
        var skills = ResolveWorkingMemoryCapabilitySkills(Array.Empty<string>());
        var registeredToolCount = Math.Max(0, _routingCatalogDiagnostics.TotalTools);
        var remoteReachabilityMode = ResolveHelloRemoteReachabilityMode();
        var allowedRootCount = Math.Max(0, _options.AllowedRoots.Count);

        var warning = new StringBuilder(256);
        warning.Append(StartupCapabilityHandshakePrefix);
        warning.Append("marker='").Append(CapabilitySnapshotMarker).Append('\'');
        warning.Append(" enabled_pack_count='").Append(enabledPackIds.Length).Append('\'');
        warning.Append(" registered_tools='").Append(registeredToolCount).Append('\'');
        warning.Append(" allowed_roots='").Append(allowedRootCount).Append('\'');
        warning.Append(" remote_reachability_mode='").Append(remoteReachabilityMode).Append('\'');
        warning.Append(" skills_marker='").Append(SkillsSnapshotMarker).Append('\'');
        warning.Append(" skill_count='").Append(skills.Length).Append('\'');

        if (enabledPackIds.Length > 0) {
            warning.Append(" enabled_packs='").Append(string.Join(",", enabledPackIds)).Append('\'');
        }

        if (routingFamilies.Length > 0) {
            warning.Append(" routing_families='").Append(string.Join(",", routingFamilies)).Append('\'');
        }

        if (skills.Length > 0) {
            warning.Append(" skills='").Append(string.Join(",", skills)).Append('\'');
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
