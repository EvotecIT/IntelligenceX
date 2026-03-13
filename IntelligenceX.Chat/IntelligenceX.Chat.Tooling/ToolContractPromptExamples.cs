using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Shared contract-backed prompt examples derived from orchestration metadata.
/// </summary>
public static class ToolContractPromptExamples {
    /// <summary>
    /// Builds compact representative examples that describe what the current tool set can do.
    /// </summary>
    public static IReadOnlyList<string> BuildRepresentativeExamples(IReadOnlyList<ToolOrchestrationCatalogEntry> entries) {
        var examples = new List<string>();
        if (entries is null || entries.Count == 0) {
            return examples;
        }

        if (HasMatchingEntry(entries, static entry =>
                string.Equals(ToolPackMetadataNormalizer.NormalizePackId(entry.PackId), "active_directory", StringComparison.Ordinal)
                && (entry.IsEnvironmentDiscoverTool
                    || entry.SupportsTargetScoping
                    || ContainsArgument(entry.TargetScopeArguments, "domain_controller")
                    || ContainsArgument(entry.TargetScopeArguments, "search_base_dn")))) {
            examples.Add("discover Active Directory environment scope, search directory objects, and target a specific domain controller or base DN");
        }

        if (HasMatchingEntry(entries, static entry =>
                string.Equals(ToolPackMetadataNormalizer.NormalizePackId(entry.PackId), "eventlog", StringComparison.Ordinal)
                && (entry.SupportsRemoteHostTargeting
                    || string.Equals(entry.ExecutionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase)))) {
            examples.Add("inspect Windows event logs and summarize recurring failures on this machine or a reachable host");
        }

        if (HasMatchingEntry(entries, static entry =>
                string.Equals(ToolPackMetadataNormalizer.NormalizePackId(entry.PackId), "system", StringComparison.Ordinal)
                && (entry.SupportsRemoteHostTargeting
                    || string.Equals(entry.ExecutionScope, "local_or_remote", StringComparison.OrdinalIgnoreCase)))) {
            examples.Add("collect system inventory plus CPU, memory, and disk health locally or on reachable machines");
        }

        if (examples.Count < 4 && HasMatchingEntry(entries, static entry => entry.IsSetupAware || entry.IsEnvironmentDiscoverTool)) {
            examples.Add("use built-in setup or environment-discovery helpers before deeper checks when target context is incomplete");
        }

        return examples;
    }

    /// <summary>
    /// Builds human-friendly cross-pack target names from handoff edges.
    /// </summary>
    public static IReadOnlyList<string> BuildCrossPackTargetPackDisplayNames(IReadOnlyList<ToolOrchestrationCatalogEntry> entries) {
        var names = new List<string>();
        if (entries is null || entries.Count == 0) {
            return names;
        }

        for (var i = 0; i < entries.Count; i++) {
            var handoffEdges = entries[i].HandoffEdges;
            if (handoffEdges.Count == 0) {
                continue;
            }

            for (var j = 0; j < handoffEdges.Count; j++) {
                var normalizedPackId = ToolPackMetadataNormalizer.NormalizePackId(handoffEdges[j].TargetPackId);
                if (normalizedPackId.Length == 0) {
                    continue;
                }

                var displayName = ToolPackMetadataNormalizer.ResolveDisplayName(normalizedPackId, fallbackName: null);
                if (displayName.Length > 0 && !names.Contains(displayName, StringComparer.OrdinalIgnoreCase)) {
                    names.Add(displayName);
                }
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    private static bool HasMatchingEntry(IReadOnlyList<ToolOrchestrationCatalogEntry> entries, Func<ToolOrchestrationCatalogEntry, bool> predicate) {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(predicate);

        for (var i = 0; i < entries.Count; i++) {
            if (predicate(entries[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsArgument(IReadOnlyList<string>? values, string expected) {
        if (values is not { Count: > 0 } || string.IsNullOrWhiteSpace(expected)) {
            return false;
        }

        for (var i = 0; i < values.Count; i++) {
            if (string.Equals((values[i] ?? string.Empty).Trim(), expected, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }
}
