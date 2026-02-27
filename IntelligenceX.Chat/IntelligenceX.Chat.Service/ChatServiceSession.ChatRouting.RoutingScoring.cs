using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private static IReadOnlyList<ToolDefinition> SelectDeterministicToolSubset(IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (definitions.Count == 0 || limit <= 0) {
            return Array.Empty<ToolDefinition>();
        }

        var uniqueDefinitions = new List<ToolDefinition>(definitions.Count);
        var seenToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var name = (definition.Name ?? string.Empty).Trim();
            if (name.Length == 0 || !seenToolNames.Add(name)) {
                continue;
            }

            uniqueDefinitions.Add(definition);
        }

        if (uniqueDefinitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        if (limit >= uniqueDefinitions.Count) {
            return uniqueDefinitions;
        }

        // Deterministic but less registration-order-biased fallback:
        // round-robin one tool per family (prefix) before filling remaining slots.
        var familyOrder = new List<string>(uniqueDefinitions.Count);
        var toolsByFamily = new Dictionary<string, Queue<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < uniqueDefinitions.Count; i++) {
            var definition = uniqueDefinitions[i];
            var family = ResolveDeterministicSubsetFamilyKey(definition.Name);
            if (!toolsByFamily.TryGetValue(family, out var queue)) {
                queue = new Queue<ToolDefinition>();
                toolsByFamily[family] = queue;
                familyOrder.Add(family);
            }

            queue.Enqueue(definition);
        }

        var selected = new List<ToolDefinition>(limit);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (selected.Count < limit) {
            var addedInPass = false;
            for (var familyIndex = 0; familyIndex < familyOrder.Count && selected.Count < limit; familyIndex++) {
                var family = familyOrder[familyIndex];
                if (!toolsByFamily.TryGetValue(family, out var queue) || queue.Count == 0) {
                    continue;
                }

                var candidate = queue.Dequeue();
                var candidateName = (candidate.Name ?? string.Empty).Trim();
                if (candidateName.Length == 0 || !selectedNames.Add(candidateName)) {
                    continue;
                }

                selected.Add(candidate);
                addedInPass = true;
            }

            if (!addedInPass) {
                break;
            }
        }

        if (selected.Count < limit) {
            for (var i = 0; i < uniqueDefinitions.Count && selected.Count < limit; i++) {
                var definition = uniqueDefinitions[i];
                var name = (definition.Name ?? string.Empty).Trim();
                if (name.Length == 0 || !selectedNames.Add(name)) {
                    continue;
                }

                selected.Add(definition);
            }
        }

        return selected.Count == 0 ? Array.Empty<ToolDefinition>() : selected;
    }

    private static string ResolveDeterministicSubsetFamilyKey(string? toolName) {
        const string packInfoSuffix = "_pack_info";
        const string environmentDiscoverSuffix = "_environment_discover";

        var normalized = (toolName ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.EndsWith(packInfoSuffix, StringComparison.OrdinalIgnoreCase)
            && normalized.Length > packInfoSuffix.Length) {
            return normalized[..^packInfoSuffix.Length].ToLowerInvariant();
        }

        if (normalized.EndsWith(environmentDiscoverSuffix, StringComparison.OrdinalIgnoreCase)
            && normalized.Length > environmentDiscoverSuffix.Length) {
            return normalized[..^environmentDiscoverSuffix.Length].ToLowerInvariant();
        }

        var separator = normalized.IndexOf('_');
        if (separator > 0) {
            return normalized[..separator].ToLowerInvariant();
        }

        return normalized.ToLowerInvariant();
    }

    private static List<ToolRoutingInsight> BuildRoutingInsights(IReadOnlyList<ToolScore> scored, IReadOnlyList<ToolDefinition> selectedDefs) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        for (var i = 0; i < scored.Count; i++) {
            var toolScore = scored[i];
            if (!selectedNames.Contains(toolScore.Definition.Name)) {
                continue;
            }

            var confidenceValue = Math.Clamp(toolScore.Score / maxScore, 0d, 1d);
            var confidence = confidenceValue >= 0.72d ? "high" : confidenceValue >= 0.45d ? "medium" : "low";
            var reasons = new List<string>();
            if (toolScore.DirectNameMatch) {
                reasons.Add("direct name match");
            }
            if (toolScore.TokenHits > 0) {
                reasons.Add("token match");
            }
            if (toolScore.Adjustment > 0.2d) {
                reasons.Add("recent tool success");
            } else if (toolScore.Adjustment < -0.2d) {
                reasons.Add("recent tool failures");
            }

            if (reasons.Count == 0) {
                reasons.Add("general relevance");
            }

            insights.Add(new ToolRoutingInsight(
                ToolName: toolScore.Definition.Name,
                Confidence: confidence,
                Score: Math.Round(toolScore.Score, 3),
                Reason: string.Join(", ", reasons),
                Strategy: ToolRoutingInsightStrategy.WeightedHeuristic));
        }

        insights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (insights.Count > 12) {
            insights.RemoveRange(12, insights.Count - 12);
        }

        return insights;
    }

    private static string[] TokenizeRoutingTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>(Math.Min(12, maxTokens));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                if (!inToken) {
                    inToken = true;
                    tokenStart = i;
                }
                continue;
            }

            if (!inToken) {
                continue;
            }

            var token = normalized.Substring(tokenStart, i - tokenStart).Normalize(NormalizationForm.FormKC).Trim();
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var lower = token.ToLowerInvariant();
            var hasNonAscii = false;
            for (var t = 0; t < lower.Length; t++) {
                if (lower[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (lower.Length < minLen) {
                continue;
            }

            if (seen.Add(lower)) {
                tokens.Add(lower);
                if (tokens.Count >= maxTokens) {
                    break;
                }
            }
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static string BuildToolRoutingSearchText(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var sb = new StringBuilder(256);
        sb.Append(definition.Name);
        if (!string.IsNullOrWhiteSpace(definition.Description)) {
            sb.Append(' ').Append(definition.Description!.Trim());
        }

        if (definition.Tags.Count > 0) {
            for (var i = 0; i < definition.Tags.Count; i++) {
                var tag = (definition.Tags[i] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }
                sb.Append(' ').Append(tag);
            }
        }

        if (definition.Aliases.Count > 0) {
            for (var i = 0; i < definition.Aliases.Count; i++) {
                var alias = definition.Aliases[i];
                if (alias is null || string.IsNullOrWhiteSpace(alias.Name)) {
                    continue;
                }
                sb.Append(' ').Append(alias.Name.Trim());
            }
        }

        var schemaArguments = ExtractToolSchemaPropertyNames(definition, maxCount: 12, out var hasTableViewProjection);
        for (var i = 0; i < schemaArguments.Length; i++) {
            sb.Append(' ').Append(schemaArguments[i]);
        }

        var requiredArguments = ExtractToolSchemaRequiredNames(definition, maxCount: 8);
        if (requiredArguments.Length > 0) {
            sb.Append(" required");
            for (var i = 0; i < requiredArguments.Length; i++) {
                sb.Append(' ').Append(requiredArguments[i]);
            }
        }

        if (hasTableViewProjection) {
            sb.Append(" table view projection columns sort_by sort_direction top");
        }

        return sb.ToString();
    }

    private static string[] ExtractToolSchemaPropertyNames(ToolDefinition definition, int maxCount, out bool hasTableViewProjection) {
        hasTableViewProjection = false;
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var properties = definition.Parameters.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        hasTableViewProjection = HasTableViewProjectionArguments(properties);

        var names = new List<string>(Math.Min(maxCount, properties.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties) {
            var name = NormalizeToolSchemaToken(kv.Key);
            if (name.Length == 0 || !seen.Add(name)) {
                continue;
            }

            names.Add(name);
            if (names.Count >= maxCount) {
                break;
            }
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static string[] ExtractToolSchemaRequiredNames(ToolDefinition definition, int maxCount) {
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var required = definition.Parameters.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(Math.Min(maxCount, required.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < required.Count && names.Count < maxCount; i++) {
            var value = NormalizeToolSchemaToken(required[i]?.AsString());
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }

            names.Add(value);
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static bool HasTableViewProjectionArguments(JsonObject properties) {
        return properties.TryGetValue("columns", out _)
               || properties.TryGetValue("sort_by", out _)
               || properties.TryGetValue("sort_direction", out _)
               || properties.TryGetValue("top", out _);
    }

    private static string NormalizeToolSchemaToken(string? token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '-') {
                sb.Append(c);
            } else if (char.IsWhiteSpace(c)) {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }

}
