using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.App;

internal sealed record ToolCatalogExecutionSummary {
    public int ExecutionAwareToolCount { get; init; }
    public int LocalOnlyToolCount { get; init; }
    public int RemoteOnlyToolCount { get; init; }
    public int LocalOrRemoteToolCount { get; init; }
    public string[] LocalOnlyPackIds { get; init; } = Array.Empty<string>();
    public string[] RemoteCapablePackIds { get; init; } = Array.Empty<string>();
}

public sealed partial class MainWindow {
    internal IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(bool runtimeIntrospectionMode = false) {
        return BuildCapabilitySelfKnowledgeLines(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogPlugins,
            _toolCatalogRoutingCatalog,
            _toolCatalogCapabilitySnapshot,
            BuildToolCatalogExecutionSummary(),
            _toolCatalogDefinitions.Count == 0 ? null : _toolCatalogDefinitions.Values,
            runtimeIntrospectionMode,
            RuntimeSelfReportDetectionSource.None);
    }

    internal static IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        bool runtimeIntrospectionMode = false,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource = RuntimeSelfReportDetectionSource.None) {
        return BuildCapabilitySelfKnowledgeLines(
            sessionPolicy,
            toolCatalogPacks: null,
            toolCatalogPlugins: null,
            toolCatalogRoutingCatalog: null,
            toolCatalogCapabilitySnapshot: null,
            toolCatalogExecutionSummary: null,
            toolCatalogTools: null,
            runtimeIntrospectionMode: runtimeIntrospectionMode,
            runtimeSelfReportDetectionSource: runtimeSelfReportDetectionSource);
    }

    internal static IReadOnlyList<string> BuildCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks,
        IReadOnlyList<PluginInfoDto>? toolCatalogPlugins = null,
        SessionRoutingCatalogDiagnosticsDto? toolCatalogRoutingCatalog = null,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot = null,
        ToolCatalogExecutionSummary? toolCatalogExecutionSummary = null,
        IReadOnlyCollection<ToolDefinitionDto>? toolCatalogTools = null,
        bool runtimeIntrospectionMode = false,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource = RuntimeSelfReportDetectionSource.None) {
        var lines = new List<string>();
        var toolingMetadata = RuntimeToolingMetadataResolver.Resolve(
            sessionPolicy,
            toolCatalogPacks,
            toolCatalogPlugins,
            toolCatalogCapabilitySnapshot);
        var snapshot = toolingMetadata.CapabilitySnapshot;
        var effectivePacks = toolingMetadata.Packs;
        var effectivePlugins = toolingMetadata.Plugins;
        var routingCatalog = sessionPolicy?.RoutingCatalog ?? toolCatalogRoutingCatalog;
        var narrowRuntimeIntrospectionContext = ShouldNarrowRuntimeIntrospectionCapabilitySelfKnowledge(
            runtimeIntrospectionMode,
            runtimeSelfReportDetectionSource);
        var enabledPackNames = BuildEnabledPackDisplayNames(effectivePacks);
        if (enabledPackNames.Count > 0) {
            lines.Add("Areas you can help with here include " + string.Join(", ", enabledPackNames) + ".");
        }

        if (snapshot is not null) {
            lines.Add(ToolCapabilityGuidanceText.BuildToolingAvailabilityLine(snapshot.ToolingAvailable));

            if (!string.IsNullOrWhiteSpace(snapshot.RemoteReachabilityMode)) {
                lines.Add("Remote reachability right now is " + DescribeReachabilityMode(snapshot.RemoteReachabilityMode) + ".");
            }

            if (!narrowRuntimeIntrospectionContext) {
                AddExecutionLocalityGuidance(lines, effectivePacks, toolCatalogExecutionSummary);
            }

            if (!narrowRuntimeIntrospectionContext && snapshot.Autonomy is not null) {
                var remoteCapablePackNames = BuildPackDisplayNamesForIds(effectivePacks, snapshot.Autonomy.RemoteCapablePackIds);
                if (remoteCapablePackNames.Count > 0) {
                    lines.Add(ToolCapabilityGuidanceText.BuildRemoteReadyAreasLine(remoteCapablePackNames));
                }

                var crossPackTargetNames = BuildPackDisplayNamesForIds(effectivePacks, snapshot.Autonomy.CrossPackTargetPackIds);
                if (crossPackTargetNames.Count > 0) {
                    lines.Add(ToolRepresentativeExamples.BuildCrossPackAvailabilityLine(crossPackTargetNames, "live"));
                }

                if (snapshot.Autonomy.SetupAwareToolCount > 0
                    || snapshot.Autonomy.HandoffAwareToolCount > 0
                    || snapshot.Autonomy.RecoveryAwareToolCount > 0) {
                    lines.Add(ToolCapabilityGuidanceText.BuildContractGuidedAutonomyLine());
                }
            }

            if (!narrowRuntimeIntrospectionContext && snapshot.Autonomy is null && effectivePacks.Length > 0) {
                var remoteCapablePackNames = BuildRemoteCapablePackDisplayNames(effectivePacks);
                if (remoteCapablePackNames.Count > 0) {
                    lines.Add(ToolCapabilityGuidanceText.BuildRemoteReadyAreasLine(remoteCapablePackNames));
                }

                var crossPackTargetNames = BuildCrossPackTargetDisplayNames(effectivePacks);
                if (crossPackTargetNames.Count > 0) {
                    lines.Add(ToolRepresentativeExamples.BuildCrossPackAvailabilityLine(crossPackTargetNames, "live"));
                }

                if (HasContractGuidedPackAutonomy(effectivePacks)) {
                    lines.Add(ToolCapabilityGuidanceText.BuildContractGuidedAutonomyLine());
                }
            }

            if (!narrowRuntimeIntrospectionContext) {
                var routingReadinessHighlights = NormalizeRoutingAutonomyHighlights(routingCatalog?.AutonomyReadinessHighlights);
                if (routingReadinessHighlights.Count > 0) {
                    lines.Add("Routing autonomy right now includes " + string.Join("; ", routingReadinessHighlights) + ".");
                }

                AddPluginSourceGuidance(lines, effectivePlugins, effectivePacks, runtimeIntrospectionMode);
                AddDeferredWorkAffordanceGuidance(lines, snapshot, runtimeIntrospectionMode);

                if (snapshot.ParityMissingCapabilityCount > 0) {
                    lines.Add($"There are {snapshot.ParityMissingCapabilityCount} upstream read-only capability gaps still not surfaced through chat, so do not promise them as live tools yet.");
                } else if (snapshot.ParityAttentionCount > 0) {
                    lines.Add("Some upstream capability families are intentionally governed or still gated, so keep promises anchored to the live registered tools above.");
                }
            }
        } else {
            if (enabledPackNames.Count == 0) {
                lines.Add("Session capabilities are still loading, so avoid pretending to have tools you cannot verify.");
            } else {
                lines.Add(ToolCapabilityGuidanceText.BuildToolingAvailabilityLine(toolingAvailable: true));
                if (!narrowRuntimeIntrospectionContext) {
                    AddExecutionLocalityGuidance(lines, effectivePacks, toolCatalogExecutionSummary);
                }

                if (!narrowRuntimeIntrospectionContext) {
                    var remoteCapablePackNames = BuildRemoteCapablePackDisplayNames(effectivePacks);
                    if (remoteCapablePackNames.Count > 0) {
                        lines.Add(ToolCapabilityGuidanceText.BuildRemoteReadyAreasLine(remoteCapablePackNames));
                    }

                    var crossPackTargetNames = BuildCrossPackTargetDisplayNames(effectivePacks);
                    if (crossPackTargetNames.Count > 0) {
                        lines.Add(ToolRepresentativeExamples.BuildCrossPackAvailabilityLine(crossPackTargetNames, "live"));
                    }

                    if (HasContractGuidedPackAutonomy(effectivePacks)) {
                        lines.Add(ToolCapabilityGuidanceText.BuildContractGuidedAutonomyLine());
                    }
                }
            }

            if (!narrowRuntimeIntrospectionContext) {
                var routingReadinessHighlights = NormalizeRoutingAutonomyHighlights(routingCatalog?.AutonomyReadinessHighlights);
                if (routingReadinessHighlights.Count > 0) {
                    lines.Add("Routing autonomy right now includes " + string.Join("; ", routingReadinessHighlights) + ".");
                }

                AddPluginSourceGuidance(lines, effectivePlugins, effectivePacks, runtimeIntrospectionMode);
                AddDeferredWorkAffordanceGuidance(lines, snapshot, runtimeIntrospectionMode);
            }
        }

        var representativeExamples = runtimeIntrospectionMode
            ? Array.Empty<string>()
            : BuildRepresentativeCapabilityExamples(effectivePacks, toolCatalogTools).ToArray();
        if (representativeExamples.Length > 0) {
            lines.Add("Concrete examples you can mention: " + string.Join("; ", representativeExamples) + ".");
        }

        if (runtimeIntrospectionMode) {
            if (enabledPackNames.Count == 0) {
                lines.Add("If tooling details are still sparse, answer with only confirmed runtime or model facts and say the rest is still loading.");
            }

            if (narrowRuntimeIntrospectionContext) {
                lines.Add("For lexical-fallback runtime self-report, stay anchored to the confirmed enabled areas, tooling availability, and reachability above until the user asks for deeper runtime provenance.");
            }

            lines.Add("For runtime self-report, mention only the live tooling or capability areas that are relevant to the user's scope.");
            lines.Add("Keep this section practical and concise; exact runtime/model/tool limits belong in the runtime capability handshake.");
        } else {
            AddGenericCapabilityGuidance(lines, enabledPackNames, representativeExamples.Length > 0);
            lines.Add("For explicit capability questions, lead with a few practical examples that are genuinely live in this session, then invite the user's task.");
            lines.Add("When asked what you can do, answer with useful examples and invite the task instead of listing internal identifiers or protocol details.");
        }

        return lines;
    }

    private static bool ShouldNarrowRuntimeIntrospectionCapabilitySelfKnowledge(
        bool runtimeIntrospectionMode,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource) {
        return runtimeIntrospectionMode
               && runtimeSelfReportDetectionSource == RuntimeSelfReportDetectionSource.LexicalFallback;
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

    private static void AddGenericCapabilityGuidance(List<string> lines, IReadOnlyList<string> enabledPackNames, bool hasRepresentativeExamples) {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(enabledPackNames);

        if (enabledPackNames.Count == 0) {
            lines.Add("Keep capability claims narrow until the session policy finishes loading and you can name the enabled areas confidently.");
            lines.Add("Concrete examples you can mention: only tasks that are clearly confirmed by the current session policy or recent runtime evidence.");
            return;
        }

        lines.Add("Use the enabled capability areas above as the source of truth for what is live in this session.");
        if (!hasRepresentativeExamples) {
            lines.Add("Concrete examples you can mention: a few practical tasks grounded in the enabled areas above, phrased in the user's language and scope.");
        } else {
            lines.Add("Keep those examples grounded in the live tool contracts above instead of improvising broader capability claims.");
        }

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
                if (!PackMatchesRuntimeId(pack, normalizedPackId)) {
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

    private ToolCatalogExecutionSummary? BuildToolCatalogExecutionSummary() {
        if (_toolStates.Count == 0) {
            return null;
        }

        var executionAwareToolCount = 0;
        var localOnlyToolCount = 0;
        var remoteOnlyToolCount = 0;
        var localOrRemoteToolCount = 0;
        var localOnlyPackIds = new List<string>();
        var remoteCapablePackIds = new List<string>();

        foreach (var pair in _toolStates) {
            if (!pair.Value) {
                continue;
            }

            var toolName = (pair.Key ?? string.Empty).Trim();
            if (toolName.Length == 0) {
                continue;
            }

            if (_toolExecutionAwareness.TryGetValue(toolName, out var isExecutionAware) && isExecutionAware) {
                executionAwareToolCount++;
            } else {
                continue;
            }

            var supportsLocalExecution = _toolSupportsLocalExecution.TryGetValue(toolName, out var localEnabled) && localEnabled;
            var supportsRemoteExecution = _toolSupportsRemoteExecution.TryGetValue(toolName, out var remoteEnabled) && remoteEnabled;
            var scope = _toolExecutionScopes.TryGetValue(toolName, out var explicitScope)
                ? explicitScope
                : ResolveToolExecutionScope(null, supportsLocalExecution, supportsRemoteExecution);

            if (string.Equals(scope, "remote_only", StringComparison.OrdinalIgnoreCase)) {
                remoteOnlyToolCount++;
                AddExecutionPackId(remoteCapablePackIds, ResolveToolPackId(toolName));
                continue;
            }

            if (string.Equals(scope, "local_or_remote", StringComparison.OrdinalIgnoreCase)) {
                localOrRemoteToolCount++;
                AddExecutionPackId(remoteCapablePackIds, ResolveToolPackId(toolName));
                continue;
            }

            localOnlyToolCount++;
            AddExecutionPackId(localOnlyPackIds, ResolveToolPackId(toolName));
        }

        if (executionAwareToolCount <= 0
            && localOnlyToolCount <= 0
            && remoteOnlyToolCount <= 0
            && localOrRemoteToolCount <= 0) {
            return null;
        }

        return new ToolCatalogExecutionSummary {
            ExecutionAwareToolCount = executionAwareToolCount,
            LocalOnlyToolCount = localOnlyToolCount,
            RemoteOnlyToolCount = remoteOnlyToolCount,
            LocalOrRemoteToolCount = localOrRemoteToolCount,
            LocalOnlyPackIds = localOnlyPackIds.Count == 0 ? Array.Empty<string>() : localOnlyPackIds.ToArray(),
            RemoteCapablePackIds = remoteCapablePackIds.Count == 0 ? Array.Empty<string>() : remoteCapablePackIds.ToArray()
        };
    }

    private static void AddExecutionPackId(List<string> target, string? packId) {
        var normalizedPackId = NormalizeRuntimePackId(packId);
        if (normalizedPackId.Length == 0 || ContainsIgnoreCase(target, normalizedPackId)) {
            return;
        }

        target.Add(normalizedPackId);
    }

    private static void AddExecutionLocalityGuidance(
        List<string> lines,
        IReadOnlyList<ToolPackInfoDto>? packs,
        ToolCatalogExecutionSummary? executionSummary) {
        if (executionSummary is null) {
            return;
        }

        var localOnlyPackNames = BuildPackDisplayNamesForIds(packs, executionSummary.LocalOnlyPackIds);
        var remoteCapablePackNames = BuildPackDisplayNamesForIds(packs, executionSummary.RemoteCapablePackIds);
        var hasLocalOnly = executionSummary.LocalOnlyToolCount > 0;
        var hasRemoteCapable = executionSummary.RemoteOnlyToolCount > 0 || executionSummary.LocalOrRemoteToolCount > 0;

        if (hasLocalOnly && hasRemoteCapable) {
            lines.Add("Execution locality is mixed across the live tool catalog, so keep host-target claims aligned to the chosen tool instead of assuming every enabled area is remote-ready.");
        } else if (hasLocalOnly) {
            lines.Add("Enabled tooling in this catalog is currently local-only, so do not imply remote execution unless a tool explicitly exposes remote reach.");
        }

        if (localOnlyPackNames.Count > 0) {
            lines.Add("Capability areas with local-only tools currently include " + string.Join(", ", localOnlyPackNames) + ".");
        }

        if (remoteCapablePackNames.Count > 0) {
            lines.Add("Capability areas with explicit remote-ready tools currently include " + string.Join(", ", remoteCapablePackNames) + ".");
        }

        if (executionSummary.ExecutionAwareToolCount > 0) {
            lines.Add("Prefer declared execution locality from execution-aware tools over name or category heuristics when deciding whether a task can run locally or remotely.");
        }
    }

    internal static string ResolveExecutionLocalityMode(ToolCatalogExecutionSummary? executionSummary) {
        if (executionSummary is null) {
            return "unknown";
        }

        var hasLocalOnly = executionSummary.LocalOnlyToolCount > 0;
        var hasRemoteCapable = executionSummary.RemoteOnlyToolCount > 0 || executionSummary.LocalOrRemoteToolCount > 0;
        if (hasLocalOnly && hasRemoteCapable) {
            return "mixed";
        }

        if (hasRemoteCapable) {
            return "remote_ready";
        }

        if (hasLocalOnly) {
            return "local_only";
        }

        return executionSummary.ExecutionAwareToolCount > 0 ? "execution_aware_unspecified" : "unknown";
    }

    internal static string DescribeExecutionLocalitySummary(ToolCatalogExecutionSummary? executionSummary, bool compact = false) {
        var mode = ResolveExecutionLocalityMode(executionSummary);
        if (executionSummary is null) {
            return compact ? "unknown:catalog_execution_unavailable." : "unknown (execution locality is still loading from the live tool catalog).";
        }

        if (compact) {
            return mode switch {
                "mixed" => "mixed:local_and_remote_tools.",
                "remote_ready" => "remote_ready:execution_aware_tools.",
                "local_only" => "local_only:execution_aware_tools.",
                "execution_aware_unspecified" => "unknown:execution_scope_unspecified.",
                _ => "unknown:catalog_execution_unavailable."
            };
        }

        var suffix = "execution-aware tools="
                     + executionSummary.ExecutionAwareToolCount.ToString()
                     + ", local-only="
                     + executionSummary.LocalOnlyToolCount.ToString()
                     + ", remote-only="
                     + executionSummary.RemoteOnlyToolCount.ToString()
                     + ", local-or-remote="
                     + executionSummary.LocalOrRemoteToolCount.ToString()
                     + ".";
        return mode switch {
            "mixed" => "mixed locality across enabled tools (" + suffix,
            "remote_ready" => "remote-ready across enabled tools (" + suffix,
            "local_only" => "local-only across enabled tools (" + suffix,
            "execution_aware_unspecified" => "execution-aware tools are present but locality is still underspecified (" + suffix,
            _ => "unknown (execution locality is still loading from the live tool catalog)."
        };
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

    private static void AddDeferredWorkAffordanceGuidance(
        List<string> lines,
        SessionCapabilitySnapshotDto? snapshot,
        bool runtimeIntrospectionMode) {
        ArgumentNullException.ThrowIfNull(lines);
        if (snapshot?.DeferredWorkAffordances is not { Length: > 0 } affordances) {
            return;
        }

        var displayNames = BuildDeferredWorkAffordanceDisplayNames(affordances);
        if (displayNames.Count == 0) {
            return;
        }

        if (runtimeIntrospectionMode) {
            lines.Add("Deferred follow-up affordances currently registered: " + string.Join(", ", displayNames) + ".");
            return;
        }

        lines.Add("Deferred follow-up work currently registered includes " + string.Join(", ", displayNames) + ".");
        if (HasBackgroundCapableDeferredWork(affordances)) {
            lines.Add("Some of those deferred affordances explicitly support background follow-up, so longer reporting-style work can continue beyond a single answer when the runtime chooses it.");
        }

        var representativeExamples = BuildDeferredWorkRepresentativeExamples(affordances, maxItems: 3);
        if (representativeExamples.Count > 0) {
            lines.Add("Deferred follow-up examples you can mention: " + string.Join("; ", representativeExamples) + ".");
        }
    }

    private static void AddPluginSourceGuidance(
        List<string> lines,
        IReadOnlyList<PluginInfoDto>? plugins,
        IReadOnlyList<ToolPackInfoDto>? packs,
        bool runtimeIntrospectionMode) {
        ArgumentNullException.ThrowIfNull(lines);
        var summaries = BuildPluginSourceSummaries(
            plugins,
            packs,
            runtimeIntrospectionMode ? 3 : 2,
            includeDisabledPlugins: runtimeIntrospectionMode);
        if (summaries.Count == 0) {
            return;
        }

        lines.Add(
            (runtimeIntrospectionMode
                ? "Registered tool sources currently visible include "
                : "Registered tool sources currently active include ")
            + string.Join("; ", summaries)
            + ".");
    }

    private static List<string> BuildPluginSourceSummaries(
        IReadOnlyList<PluginInfoDto>? plugins,
        IReadOnlyList<ToolPackInfoDto>? packs,
        int maxItems,
        bool includeDisabledPlugins) {
        var summaries = new List<string>();
        if (plugins is not { Count: > 0 }) {
            return summaries;
        }

        var orderedPlugins = plugins
            .Where(plugin => includeDisabledPlugins || plugin.Enabled)
            .OrderByDescending(static plugin => plugin.Enabled)
            .ThenBy(static plugin => plugin.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static plugin => plugin.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < orderedPlugins.Length; i++) {
            if (maxItems > 0 && summaries.Count >= maxItems) {
                break;
            }

            var plugin = orderedPlugins[i];
            var displayName = ResolvePluginDisplayName(plugin);
            if (displayName.Length == 0) {
                continue;
            }

            var coverage = BuildPackDisplayNamesForIds(packs, plugin.PackIds);
            var summary = displayName + " from " + DescribePluginOrigin(plugin.Origin, plugin.SourceKind);
            if (coverage.Count > 0) {
                summary += " covering " + string.Join(", ", coverage);
            }
            if (!plugin.Enabled) {
                summary += " (disabled)";
            }

            if (!ContainsIgnoreCase(summaries, summary)) {
                summaries.Add(summary);
            }
        }

        return summaries;
    }

    private static string ResolvePluginDisplayName(PluginInfoDto plugin) {
        var displayName = (plugin.Name ?? string.Empty).Trim();
        if (displayName.Length > 0) {
            return displayName;
        }

        return ToolPackMetadataNormalizer.ResolveDisplayName(plugin.Id, fallbackName: plugin.Id);
    }

    private static string DescribePluginOrigin(string? origin, ToolPackSourceKind sourceKind) {
        var normalizedOrigin = (origin ?? string.Empty).Trim();
        if (normalizedOrigin.Equals("folder", StringComparison.OrdinalIgnoreCase)
            || normalizedOrigin.Equals("plugin_folder", StringComparison.OrdinalIgnoreCase)) {
            return "a plugin folder";
        }

        if (normalizedOrigin.Equals("builtin", StringComparison.OrdinalIgnoreCase)) {
            return "the built-in runtime";
        }

        if (normalizedOrigin.Length > 0) {
            return normalizedOrigin.Replace('_', ' ');
        }

        return sourceKind switch {
            ToolPackSourceKind.Builtin => "the built-in runtime",
            ToolPackSourceKind.ClosedSource => "a closed-source plugin source",
            _ => "a registered plugin source"
        };
    }

    private static bool HasBackgroundCapableDeferredWork(
        IReadOnlyList<SessionCapabilityDeferredWorkAffordanceDto> affordances) {
        for (var i = 0; i < affordances.Count; i++) {
            if (affordances[i].SupportsBackgroundExecution) {
                return true;
            }
        }

        return false;
    }

    private static List<string> BuildDeferredWorkAffordanceDisplayNames(
        IReadOnlyList<SessionCapabilityDeferredWorkAffordanceDto> affordances) {
        var names = new List<string>();
        for (var i = 0; i < affordances.Count; i++) {
            var displayName = ResolveDeferredWorkAffordanceDisplayName(affordances[i]);
            if (displayName.Length == 0 || ContainsIgnoreCase(names, displayName)) {
                continue;
            }

            names.Add(displayName);
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static List<string> BuildDeferredWorkRepresentativeExamples(
        IReadOnlyList<SessionCapabilityDeferredWorkAffordanceDto> affordances,
        int maxItems) {
        var examples = new List<string>();
        for (var i = 0; i < affordances.Count; i++) {
            var affordanceExamples = affordances[i].RepresentativeExamples ?? Array.Empty<string>();
            for (var j = 0; j < affordanceExamples.Length; j++) {
                var example = (affordanceExamples[j] ?? string.Empty).Trim();
                if (example.Length == 0 || ContainsIgnoreCase(examples, example)) {
                    continue;
                }

                examples.Add(example);
                if (examples.Count >= maxItems) {
                    return examples;
                }
            }
        }

        return examples;
    }

    private static string ResolveDeferredWorkAffordanceDisplayName(SessionCapabilityDeferredWorkAffordanceDto affordance) {
        var displayName = (affordance.DisplayName ?? string.Empty).Trim();
        if (displayName.Length > 0) {
            return displayName;
        }

        var capabilityId = (affordance.CapabilityId ?? string.Empty).Trim();
        if (capabilityId.Length == 0) {
            return string.Empty;
        }

        var tokens = capabilityId.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) {
            return capabilityId;
        }

        for (var i = 0; i < tokens.Length; i++) {
            tokens[i] = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tokens[i].ToLowerInvariant());
        }

        return string.Join(" ", tokens);
    }

    private static List<string> BuildRepresentativeCapabilityExamples(
        IReadOnlyList<ToolPackInfoDto>? packs,
        IReadOnlyCollection<ToolDefinitionDto>? toolCatalogTools) {
        if (toolCatalogTools is null || toolCatalogTools.Count == 0) {
            return new List<string>();
        }

        var enabledPackIds = BuildEnabledPackIdSet(packs);
        var requireEnabledPack = enabledPackIds.Count > 0;
        var tools = new List<ToolDefinitionDto>(toolCatalogTools.Count);
        foreach (var tool in toolCatalogTools) {
            if (tool is null || string.IsNullOrWhiteSpace(tool.Name)) {
                continue;
            }

            var normalizedPackId = NormalizeRuntimePackId(tool.PackId);
            if (requireEnabledPack && normalizedPackId.Length > 0 && !enabledPackIds.Contains(normalizedPackId)) {
                continue;
            }

            tools.Add(tool);
        }

        if (tools.Count == 0) {
            return new List<string>();
        }

        var examples = ToolRepresentativeExamples.CollectDeclaredExamples(
            tools,
            static tool => tool.RepresentativeExamples);
        if (examples.Count > 0) {
            return examples;
        }

        ToolRepresentativeExamples.AppendFallbackExamples(
            examples,
            tools,
            (static tool =>
                    ToolRepresentativeExamples.IsDirectoryScopeFallbackCandidate(
                        tool.IsEnvironmentDiscoverTool,
                        scope: null,
                        tool.SupportsTargetScoping,
                        tool.TargetScopeArguments),
                ToolRepresentativeExamples.DirectoryScopeFallbackExample),
            (static tool =>
                    (HasRoutingEntity(tool, "event")
                     || HasCategoryToken(tool, "event"))
                    && ToolRepresentativeExamples.IsEventEvidenceFallbackCandidate(
                        entity: tool.RoutingEntity,
                        tool.SupportsRemoteHostTargeting,
                        tool.SupportsRemoteExecution,
                        tool.ExecutionScope),
                ToolRepresentativeExamples.EventEvidenceFallbackExample),
            (static tool =>
                    ((HasRoutingScope(tool, "host") && HasRoutingEntity(tool, "host"))
                     || HasCategoryToken(tool, "system", "host"))
                    && ToolRepresentativeExamples.IsHostDiagnosticsFallbackCandidate(
                        scope: tool.RoutingScope,
                        entity: tool.RoutingEntity,
                        tool.SupportsRemoteHostTargeting,
                        tool.SupportsRemoteExecution,
                        tool.ExecutionScope),
                ToolRepresentativeExamples.HostDiagnosticsFallbackExample));

        var crossPackTargets = BuildCrossPackTargetNamesFromTools(tools, packs);
        if (crossPackTargets.Count > 0) {
            ToolRepresentativeExamples.TryAddExample(
                examples,
                ToolRepresentativeExamples.BuildCrossPackPivotExample(crossPackTargets));
        }

        ToolRepresentativeExamples.AppendFallbackExamples(
            examples,
            tools,
            (static tool => tool.IsSetupAware || tool.IsEnvironmentDiscoverTool, ToolRepresentativeExamples.SetupAwareFallbackExample),
            (static tool => tool.IsPackInfoTool, ToolRepresentativeExamples.PackInfoFallbackExample));

        return examples;
    }

}
