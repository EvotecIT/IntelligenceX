using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    internal IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(bool runtimeIntrospectionMode = false) {
        return BuildCapabilitySelfKnowledgeLines(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogRoutingCatalog,
            _toolCatalogCapabilitySnapshot,
            runtimeIntrospectionMode);
    }

    internal static IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        bool runtimeIntrospectionMode = false) {
        return BuildCapabilitySelfKnowledgeLines(
            sessionPolicy,
            toolCatalogPacks: null,
            toolCatalogRoutingCatalog: null,
            toolCatalogCapabilitySnapshot: null,
            runtimeIntrospectionMode: runtimeIntrospectionMode);
    }

    internal static IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks,
        SessionRoutingCatalogDiagnosticsDto? toolCatalogRoutingCatalog,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot = null,
        bool runtimeIntrospectionMode = false) {
        var lines = new List<string>();
        var snapshot = sessionPolicy?.CapabilitySnapshot ?? toolCatalogCapabilitySnapshot;
        var effectivePacks = ResolveCapabilityPacks(sessionPolicy, toolCatalogPacks);
        var routingCatalog = sessionPolicy?.RoutingCatalog ?? toolCatalogRoutingCatalog;
        var enabledPackNames = BuildEnabledPackDisplayNames(effectivePacks);
        if (enabledPackNames.Count > 0) {
            lines.Add("Areas you can help with here include " + string.Join(", ", enabledPackNames) + ".");
        }

        if (snapshot is not null) {
            if (snapshot.ToolingAvailable) {
                lines.Add("You can actively use live session tools when the user wants checks, investigation, or data gathering.");
            } else {
                lines.Add("Tooling is not currently available in this session, so answers should stay conversational and reasoning-based.");
            }

            if (!string.IsNullOrWhiteSpace(snapshot.RemoteReachabilityMode)) {
                lines.Add("Remote reachability right now is " + DescribeReachabilityMode(snapshot.RemoteReachabilityMode) + ".");
            }

            if (snapshot.Autonomy is not null) {
                var remoteCapablePackNames = BuildPackDisplayNamesForIds(effectivePacks, snapshot.Autonomy.RemoteCapablePackIds);
                if (remoteCapablePackNames.Count > 0) {
                    lines.Add("Remote-ready capability areas currently include " + string.Join(", ", remoteCapablePackNames) + ".");
                }

                var crossPackTargetNames = BuildPackDisplayNamesForIds(effectivePacks, snapshot.Autonomy.CrossPackTargetPackIds);
                if (crossPackTargetNames.Count > 0) {
                    lines.Add("Cross-pack follow-up pivots are live into " + string.Join(", ", crossPackTargetNames) + " when the workflow calls for it.");
                }

                if (snapshot.Autonomy.SetupAwareToolCount > 0
                    || snapshot.Autonomy.HandoffAwareToolCount > 0
                    || snapshot.Autonomy.RecoveryAwareToolCount > 0) {
                    lines.Add(
                        "Prefer live contract-guided setup, handoff, and recovery flows when available instead of narrating unsupported manual steps.");
                }
            }

            if (snapshot.Autonomy is null && effectivePacks.Count > 0) {
                var remoteCapablePackNames = BuildRemoteCapablePackDisplayNames(effectivePacks);
                if (remoteCapablePackNames.Count > 0) {
                    lines.Add("Remote-ready capability areas currently include " + string.Join(", ", remoteCapablePackNames) + ".");
                }

                var crossPackTargetNames = BuildCrossPackTargetDisplayNames(effectivePacks);
                if (crossPackTargetNames.Count > 0) {
                    lines.Add("Cross-pack follow-up pivots are live into " + string.Join(", ", crossPackTargetNames) + " when the workflow calls for it.");
                }

                if (HasContractGuidedPackAutonomy(effectivePacks)) {
                    lines.Add(
                        "Prefer live contract-guided setup, handoff, and recovery flows when available instead of narrating unsupported manual steps.");
                }
            }

            var routingReadinessHighlights = NormalizeRoutingAutonomyHighlights(routingCatalog?.AutonomyReadinessHighlights);
            if (routingReadinessHighlights.Count > 0) {
                lines.Add("Routing autonomy right now includes " + string.Join("; ", routingReadinessHighlights) + ".");
            }

            if (snapshot.ParityMissingCapabilityCount > 0) {
                lines.Add($"There are {snapshot.ParityMissingCapabilityCount} upstream read-only capability gaps still not surfaced through chat, so do not promise them as live tools yet.");
            } else if (snapshot.ParityAttentionCount > 0) {
                lines.Add("Some upstream capability families are intentionally governed or still gated, so keep promises anchored to the live registered tools above.");
            }
        } else {
            if (enabledPackNames.Count == 0) {
                lines.Add("Session capabilities are still loading, so avoid pretending to have tools you cannot verify.");
            } else {
                lines.Add("You can actively use live session tools when the user wants checks, investigation, or data gathering.");

                var remoteCapablePackNames = BuildRemoteCapablePackDisplayNames(effectivePacks);
                if (remoteCapablePackNames.Count > 0) {
                    lines.Add("Remote-ready capability areas currently include " + string.Join(", ", remoteCapablePackNames) + ".");
                }

                var crossPackTargetNames = BuildCrossPackTargetDisplayNames(effectivePacks);
                if (crossPackTargetNames.Count > 0) {
                    lines.Add("Cross-pack follow-up pivots are live into " + string.Join(", ", crossPackTargetNames) + " when the workflow calls for it.");
                }

                if (HasContractGuidedPackAutonomy(effectivePacks)) {
                    lines.Add(
                        "Prefer live contract-guided setup, handoff, and recovery flows when available instead of narrating unsupported manual steps.");
                }
            }

            var routingReadinessHighlights = NormalizeRoutingAutonomyHighlights(routingCatalog?.AutonomyReadinessHighlights);
            if (routingReadinessHighlights.Count > 0) {
                lines.Add("Routing autonomy right now includes " + string.Join("; ", routingReadinessHighlights) + ".");
            }
        }

        if (runtimeIntrospectionMode) {
            if (enabledPackNames.Count == 0) {
                lines.Add("If tooling details are still sparse, answer with only confirmed runtime or model facts and say the rest is still loading.");
            }

            lines.Add("For runtime self-report, mention only the live tooling or capability areas that are relevant to the user's scope.");
            lines.Add("Keep this section practical and concise; exact runtime/model/tool limits belong in the runtime capability handshake.");
        } else {
            AddGenericCapabilityGuidance(lines, enabledPackNames);
            lines.Add("For explicit capability questions, lead with a few practical examples that are genuinely live in this session, then invite the user's task.");
            lines.Add("When asked what you can do, answer with useful examples and invite the task instead of listing internal identifiers or protocol details.");
        }

        return lines;
    }

    private static IReadOnlyList<ToolPackInfoDto> ResolveCapabilityPacks(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks) {
        if (sessionPolicy?.Packs is { Length: > 0 } sessionPacks) {
            return sessionPacks;
        }

        return toolCatalogPacks is { Count: > 0 }
            ? toolCatalogPacks
            : Array.Empty<ToolPackInfoDto>();
    }

    private static List<string> BuildEnabledPackDisplayNames(IReadOnlyList<ToolPackInfoDto>? packs) {
        var names = new List<string>();
        if (packs is not { Count: > 0 }) {
            return names;
        }

        for (var i = 0; i < packs.Count; i++) {
            var pack = packs[i];
            if (!pack.Enabled) {
                continue;
            }

            var displayName = (pack.Name ?? string.Empty).Trim();
            if (displayName.Length == 0) {
                displayName = ToolPackMetadataNormalizer.ResolveDisplayName(pack.Id, pack.Name);
            }

            if (displayName.Length > 0 && !ContainsIgnoreCase(names, displayName)) {
                names.Add(displayName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static void AddGenericCapabilityGuidance(List<string> lines, IReadOnlyList<string> enabledPackNames) {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(enabledPackNames);

        if (enabledPackNames.Count == 0) {
            lines.Add("Keep capability claims narrow until the session policy finishes loading and you can name the enabled areas confidently.");
            lines.Add("Concrete examples you can mention: only tasks that are clearly confirmed by the current session policy or recent runtime evidence.");
            return;
        }

        lines.Add("Use the enabled capability areas above as the source of truth for what is live in this session.");
        lines.Add("Concrete examples you can mention: a few practical tasks grounded in the enabled areas above, phrased in the user's language and scope.");

        if (enabledPackNames.Count == 1) {
            lines.Add("If you need a concrete anchor, start from the single enabled area above instead of inventing broader capability claims.");
            return;
        }

        lines.Add("Prefer the enabled areas that best match the user's request instead of listing every area in the session.");
    }

    private static List<string> BuildPackDisplayNamesForIds(IReadOnlyList<ToolPackInfoDto>? packs, IReadOnlyList<string>? packIds) {
        var names = new List<string>();
        if (packIds is not { Count: > 0 }) {
            return names;
        }

        for (var i = 0; i < packIds.Count; i++) {
            var normalizedPackId = NormalizeRuntimePackId(packIds[i]);
            if (normalizedPackId.Length == 0) {
                continue;
            }

            var displayName = ResolvePackDisplayName(packs, normalizedPackId);
            if (displayName.Length > 0 && !ContainsIgnoreCase(names, displayName)) {
                names.Add(displayName);
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static string ResolvePackDisplayName(IReadOnlyList<ToolPackInfoDto>? packs, string normalizedPackId) {
        if (packs is { Count: > 0 }) {
            for (var i = 0; i < packs.Count; i++) {
                var pack = packs[i];
                if (!string.Equals(ToolPackMetadataNormalizer.NormalizePackId(pack.Id), normalizedPackId, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                return ToolPackMetadataNormalizer.ResolveDisplayName(pack.Id, pack.Name);
            }
        }

        return normalizedPackId;
    }

    private static List<string> BuildRemoteCapablePackDisplayNames(IReadOnlyList<ToolPackInfoDto>? packs) {
        var packIds = new List<string>();
        if (packs is not { Count: > 0 }) {
            return packIds;
        }

        for (var i = 0; i < packs.Count; i++) {
            var pack = packs[i];
            if (!pack.Enabled || pack.AutonomySummary?.RemoteCapableTools <= 0) {
                continue;
            }

            var normalizedPackId = NormalizeRuntimePackId(pack.Id);
            if (normalizedPackId.Length > 0 && !ContainsIgnoreCase(packIds, normalizedPackId)) {
                packIds.Add(normalizedPackId);
            }
        }

        return BuildPackDisplayNamesForIds(packs, packIds);
    }

    private static List<string> BuildCrossPackTargetDisplayNames(IReadOnlyList<ToolPackInfoDto>? packs) {
        var targetPackIds = new List<string>();
        if (packs is not { Count: > 0 }) {
            return targetPackIds;
        }

        for (var i = 0; i < packs.Count; i++) {
            var pack = packs[i];
            if (!pack.Enabled) {
                continue;
            }

            var targets = pack.AutonomySummary?.CrossPackTargetPacks;
            if (targets is not { Length: > 0 }) {
                continue;
            }

            for (var j = 0; j < targets.Length; j++) {
                var normalizedTargetId = NormalizeRuntimePackId(targets[j]);
                if (normalizedTargetId.Length > 0 && !ContainsIgnoreCase(targetPackIds, normalizedTargetId)) {
                    targetPackIds.Add(normalizedTargetId);
                }
            }
        }

        return BuildPackDisplayNamesForIds(packs, targetPackIds);
    }

    private static bool HasContractGuidedPackAutonomy(IReadOnlyList<ToolPackInfoDto>? packs) {
        if (packs is not { Count: > 0 }) {
            return false;
        }

        for (var i = 0; i < packs.Count; i++) {
            var autonomySummary = packs[i].AutonomySummary;
            if (autonomySummary is null) {
                continue;
            }

            if (autonomySummary.SetupAwareTools > 0
                || autonomySummary.HandoffAwareTools > 0
                || autonomySummary.RecoveryAwareTools > 0) {
                return true;
            }
        }

        return false;
    }

    private static string DescribeReachabilityMode(string? mode) {
        var normalized = (mode ?? string.Empty).Trim();
        if (normalized.Equals("remote_capable", StringComparison.OrdinalIgnoreCase)) {
            return "remote-capable";
        }

        if (normalized.Equals("local_only", StringComparison.OrdinalIgnoreCase)) {
            return "local-only";
        }

        return normalized.Length == 0 ? "unknown" : normalized;
    }

    private static bool ContainsIgnoreCase(IReadOnlyList<string> values, string candidate) {
        ArgumentNullException.ThrowIfNull(values);
        var normalizedCandidate = (candidate ?? string.Empty).Trim();
        if (normalizedCandidate.Length == 0) {
            return false;
        }

        for (var i = 0; i < values.Count; i++) {
            if (string.Equals((values[i] ?? string.Empty).Trim(), normalizedCandidate, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static List<string> NormalizeRoutingAutonomyHighlights(IReadOnlyList<string>? values) {
        var normalized = new List<string>();
        if (values is not { Count: > 0 }) {
            return normalized;
        }

        for (var i = 0; i < values.Count; i++) {
            var candidate = (values[i] ?? string.Empty).Trim();
            if (candidate.Length == 0 || ContainsIgnoreCase(normalized, candidate)) {
                continue;
            }

            normalized.Add(candidate);
        }

        return normalized;
    }
}
