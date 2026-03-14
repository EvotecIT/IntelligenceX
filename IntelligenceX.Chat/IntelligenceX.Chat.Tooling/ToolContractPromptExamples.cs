using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared contract-backed prompt examples derived from orchestration metadata.
/// </summary>
public static class ToolContractPromptExamples {
    /// <summary>
    /// Builds compact representative examples that describe what the current tool set can do.
    /// </summary>
    public static IReadOnlyList<string> BuildRepresentativeExamples(IReadOnlyList<ToolOrchestrationCatalogEntry> entries) {
        ArgumentNullException.ThrowIfNull(entries);

        var examples = ToolRepresentativeExamples.CollectDeclaredExamples(
            entries,
            static entry => entry.RepresentativeExamples);
        if (examples.Count > 0 || entries.Count == 0) {
            return examples;
        }

        ToolRepresentativeExamples.AppendFallbackExamples(
            examples,
            entries,
            (static entry => IsDirectoryScopeExampleCandidate(entry), ToolRepresentativeExamples.DirectoryScopeFallbackExample),
            (static entry => IsEventEvidenceExampleCandidate(entry), ToolRepresentativeExamples.EventEvidenceFallbackExample),
            (static entry => IsHostDiagnosticsExampleCandidate(entry), ToolRepresentativeExamples.HostDiagnosticsFallbackExample),
            (static entry => entry.IsSetupAware || entry.IsEnvironmentDiscoverTool, ToolRepresentativeExamples.SetupAwareFallbackExample));

        return examples;
    }

    /// <summary>
    /// Builds human-friendly cross-pack target names from handoff edges.
    /// </summary>
    public static IReadOnlyList<string> BuildCrossPackTargetPackDisplayNames(IReadOnlyList<ToolOrchestrationCatalogEntry> entries) {
        ArgumentNullException.ThrowIfNull(entries);

        return ToolRepresentativeExamples.CollectTargetDisplayNames(
            entries,
            static entry => ExtractTargetPackIds(entry.HandoffEdges),
            static packId => ToolPackMetadataNormalizer.NormalizePackId(packId),
            static normalizedPackId => ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPackId, fallbackName: null));
    }

    private static IReadOnlyList<string> ExtractTargetPackIds(IReadOnlyList<ToolOrchestrationHandoffEdge> handoffEdges) {
        if (handoffEdges is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var packIds = new string[handoffEdges.Count];
        for (var i = 0; i < handoffEdges.Count; i++) {
            packIds[i] = handoffEdges[i].TargetPackId;
        }

        return packIds;
    }

    private static bool IsDirectoryScopeExampleCandidate(ToolOrchestrationCatalogEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);

        return ToolRepresentativeExamples.IsDirectoryScopeFallbackCandidate(
            entry.IsEnvironmentDiscoverTool,
            entry.Scope,
            entry.SupportsTargetScoping,
            entry.TargetScopeArguments);
    }

    private static bool IsEventEvidenceExampleCandidate(ToolOrchestrationCatalogEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);

        return ToolRepresentativeExamples.IsEventEvidenceFallbackCandidate(
            entry.Entity,
            entry.SupportsRemoteHostTargeting,
            entry.SupportsRemoteExecution,
            entry.ExecutionScope);
    }

    private static bool IsHostDiagnosticsExampleCandidate(ToolOrchestrationCatalogEntry entry) {
        ArgumentNullException.ThrowIfNull(entry);

        return ToolRepresentativeExamples.IsHostDiagnosticsFallbackCandidate(
            entry.Scope,
            entry.Entity,
            entry.SupportsRemoteHostTargeting,
            entry.SupportsRemoteExecution,
            entry.ExecutionScope);
    }
}
