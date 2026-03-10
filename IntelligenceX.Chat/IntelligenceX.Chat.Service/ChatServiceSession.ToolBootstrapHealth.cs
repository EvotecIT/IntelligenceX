using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private string[] OrderBootstrapToolNamesByHealth(
        IEnumerable<string> toolNames,
        IReadOnlySet<string>? suppressedToolNames = null,
        IReadOnlySet<string>? excludedToolNames = null) {
        if (toolNames is null) {
            return Array.Empty<string>();
        }

        var ranked = new List<(string ToolName, double Score)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in toolNames) {
            var toolName = (candidate ?? string.Empty).Trim();
            if (toolName.Length == 0
                || !seen.Add(toolName)
                || excludedToolNames?.Contains(toolName) == true
                || suppressedToolNames?.Contains(toolName) == true) {
                continue;
            }

            ranked.Add((toolName, ReadToolRoutingAdjustment(toolName)));
        }

        if (ranked.Count == 0) {
            return Array.Empty<string>();
        }

        return ranked
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(static candidate => candidate.ToolName)
            .ToArray();
    }

    private bool TryResolvePreferredPackPreflightDefinition(
        PackPreflightCatalog catalog,
        string packId,
        string role,
        IReadOnlySet<string> explicitRoundPreflightNames,
        IReadOnlySet<string> rememberedPreflightTools,
        IReadOnlySet<string> suppressedPreflightTools,
        IReadOnlySet<string> alreadySelectedToolNames,
        out ToolDefinition definition) {
        definition = default!;

        var normalizedPackId = (packId ?? string.Empty).Trim();
        var normalizedRole = (role ?? string.Empty).Trim();
        if (normalizedPackId.Length == 0 || normalizedRole.Length == 0) {
            return false;
        }

        var excludedToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var explicitToolName in explicitRoundPreflightNames) {
            excludedToolNames.Add(explicitToolName);
        }
        foreach (var rememberedToolName in rememberedPreflightTools) {
            excludedToolNames.Add(rememberedToolName);
        }
        foreach (var selectedToolName in alreadySelectedToolNames) {
            excludedToolNames.Add(selectedToolName);
        }

        var candidateToolNames = _toolOrchestrationCatalog
            .GetByPackAndRole(normalizedPackId, normalizedRole)
            .Select(static entry => entry.ToolName);

        var orderedCandidateToolNames = OrderBootstrapToolNamesByHealth(
            candidateToolNames,
            suppressedPreflightTools,
            excludedToolNames);
        for (var i = 0; i < orderedCandidateToolNames.Length; i++) {
            if (!catalog.DefinitionsByToolName.TryGetValue(orderedCandidateToolNames[i], out var candidateDefinition)) {
                continue;
            }

            if (string.Equals(normalizedRole, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase)
                && ToolDefinitionHasRequiredArguments(candidateDefinition)) {
                continue;
            }

            definition = candidateDefinition;
            return true;
        }

        return false;
    }
}
