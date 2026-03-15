using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private static bool HasMatchingTool(IReadOnlyList<ToolDefinitionDto> tools, Func<ToolDefinitionDto, bool> predicate) {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(predicate);

        for (var i = 0; i < tools.Count; i++) {
            if (predicate(tools[i])) {
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

    private static bool HasCategoryToken(ToolDefinitionDto tool, params string[] tokens) {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(tokens);

        var category = (tool.Category ?? string.Empty).Trim();
        if (category.Length == 0) {
            return false;
        }

        for (var i = 0; i < tokens.Length; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (token.Length == 0) {
                continue;
            }

            if (category.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static bool HasRoutingScope(ToolDefinitionDto tool, params string[] tokens) {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(tokens);

        return HasNormalizedToken(tool.RoutingScope, tokens);
    }

    private static bool HasRoutingEntity(ToolDefinitionDto tool, params string[] tokens) {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(tokens);

        return HasNormalizedToken(tool.RoutingEntity, tokens);
    }

    private static bool HasNormalizedToken(string? value, IReadOnlyList<string> tokens) {
        var normalizedValue = (value ?? string.Empty).Trim();
        if (normalizedValue.Length == 0 || tokens is not { Count: > 0 }) {
            return false;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (token.Length == 0) {
                continue;
            }

            if (string.Equals(normalizedValue, token, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> BuildEnabledPackIdSet(IReadOnlyList<ToolPackInfoDto>? packs) {
        var enabledPackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (packs is not { Count: > 0 }) {
            return enabledPackIds;
        }

        for (var i = 0; i < packs.Count; i++) {
            var pack = packs[i];
            if (!pack.Enabled) {
                continue;
            }

            var normalizedPackId = NormalizeRuntimePackId(pack.Id);
            if (normalizedPackId.Length > 0) {
                enabledPackIds.Add(normalizedPackId);
            }
        }

        return enabledPackIds;
    }

    private static List<string> BuildCrossPackTargetNamesFromTools(
        IReadOnlyList<ToolDefinitionDto> tools,
        IReadOnlyList<ToolPackInfoDto>? packs) {
        return ToolRepresentativeExamples.CollectTargetDisplayNames(
            tools,
            static tool => tool.HandoffTargetPackIds,
            packId => NormalizeRuntimePackId(packId),
            normalizedPackId => ResolvePackDisplayName(packs, normalizedPackId));
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
