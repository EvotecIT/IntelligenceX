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

    private static List<ToolRoutingInsight> BuildRoutingInsights(
        IReadOnlyList<ToolScore> scored,
        IReadOnlyList<ToolDefinition> selectedDefs,
        WeightedRoutingSelectionDiagnostics selectionDiagnostics) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        var ambiguityReason = BuildWeightedRoutingAmbiguityReason(selectionDiagnostics);
        var emittedAmbiguityReason = false;
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

            if (!emittedAmbiguityReason && ambiguityReason.Length > 0) {
                reasons.Add("ambiguous top-score cluster");
                reasons.Add(ambiguityReason);
                emittedAmbiguityReason = true;
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

    private static string BuildWeightedRoutingAmbiguityReason(WeightedRoutingSelectionDiagnostics diagnostics) {
        if (!diagnostics.AmbiguityWidened) {
            return string.Empty;
        }

        return $"{WeightedRoutingAmbiguityMarker} baseline={diagnostics.BaselineMinSelection} effective={diagnostics.EffectiveMinSelection} cluster={diagnostics.AmbiguousClusterSize} second_ratio={diagnostics.SecondScoreRatio:0.###}";
    }

    private static string[] TokenizeRoutingTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>(Math.Min(12, maxTokens));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tokenBeforePrevious = string.Empty;
        var previousToken = string.Empty;
        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && (char.IsLetterOrDigit(ch) || ch is '_' or '-');
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
            var allowShortAsciiCandidate = string.Equals(previousToken, "pack", StringComparison.Ordinal);
            if (TryAddRoutingTokenCandidate(tokens, seen, lower, maxTokens, allowShortAsciiCandidate)) {
                break;
            }

            var separatorNormalized = NormalizeRoutingSeparatorToken(lower);
            if (TryAddRoutingTokenCandidate(tokens, seen, separatorNormalized, maxTokens, allowShortAsciiCandidate)) {
                break;
            }

            var compact = NormalizeCompactToken(lower.AsSpan());
            if (TryAddRoutingTokenCandidate(tokens, seen, compact, maxTokens, allowShortAsciiCandidate)) {
                break;
            }

            if (TryAddCompoundPackRoutingTokenCandidates(tokens, seen, tokenBeforePrevious, previousToken, lower, maxTokens)) {
                break;
            }

            tokenBeforePrevious = previousToken;
            previousToken = lower;
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static bool TryAddCompoundPackRoutingTokenCandidates(
        List<string> tokens,
        HashSet<string> seen,
        string tokenBeforePrevious,
        string previousToken,
        string currentToken,
        int maxTokens) {
        if (TryAddKnownCompoundPackRoutingTokenCandidate(tokens, seen, previousToken, currentToken, maxTokens)) {
            return true;
        }

        if (TryAddKnownCompoundPackRoutingTokenCandidate(tokens, seen, tokenBeforePrevious, previousToken, currentToken, maxTokens)) {
            return true;
        }

        return false;
    }

    private static bool TryAddKnownCompoundPackRoutingTokenCandidate(
        List<string> tokens,
        HashSet<string> seen,
        string firstToken,
        string secondToken,
        int maxTokens) {
        if (string.IsNullOrWhiteSpace(firstToken) || string.IsNullOrWhiteSpace(secondToken)) {
            return false;
        }

        var combined = $"{firstToken}_{secondToken}";
        return TryAddKnownCompoundPackRoutingTokenCandidate(tokens, seen, combined, maxTokens);
    }

    private static bool TryAddKnownCompoundPackRoutingTokenCandidate(
        List<string> tokens,
        HashSet<string> seen,
        string firstToken,
        string secondToken,
        string thirdToken,
        int maxTokens) {
        if (string.IsNullOrWhiteSpace(firstToken)
            || string.IsNullOrWhiteSpace(secondToken)
            || string.IsNullOrWhiteSpace(thirdToken)) {
            return false;
        }

        var combined = $"{firstToken}_{secondToken}_{thirdToken}";
        return TryAddKnownCompoundPackRoutingTokenCandidate(tokens, seen, combined, maxTokens);
    }

    private static bool TryAddKnownCompoundPackRoutingTokenCandidate(
        List<string> tokens,
        HashSet<string> seen,
        string combined,
        int maxTokens) {
        var compact = NormalizeCompactToken(combined.AsSpan());
        if (!ToolSelectionMetadata.IsKnownCompoundPackRoutingCompact(compact)) {
            return false;
        }

        var separatorNormalized = NormalizeRoutingSeparatorToken(combined);
        if (TryAddRoutingTokenCandidate(tokens, seen, separatorNormalized, maxTokens, allowShortAsciiCandidate: false)) {
            return true;
        }

        return TryAddRoutingTokenCandidate(tokens, seen, compact, maxTokens, allowShortAsciiCandidate: false);
    }

    private static bool TryAddRoutingTokenCandidate(List<string> tokens, HashSet<string> seen, string candidate, int maxTokens, bool allowShortAsciiCandidate) {
        var normalized = (candidate ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var hasNonAscii = false;
        for (var i = 0; i < normalized.Length; i++) {
            if (normalized[i] > 127) {
                hasNonAscii = true;
                break;
            }
        }

        var minLen = hasNonAscii ? 2 : allowShortAsciiCandidate ? 2 : 3;
        if (normalized.Length < minLen || !seen.Add(normalized)) {
            return false;
        }

        tokens.Add(normalized);
        return tokens.Count >= maxTokens;
    }

    private static string NormalizeRoutingSeparatorToken(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(normalized.Length);
        var previousWasSeparator = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                sb.Append(char.ToLowerInvariant(ch));
                previousWasSeparator = false;
                continue;
            }

            if (ch is '_' or '-') {
                if (!previousWasSeparator && sb.Length > 0) {
                    sb.Append('_');
                    previousWasSeparator = true;
                }
            }
        }

        return sb.ToString().Trim('_');
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

        AppendRoutingPackTokens(sb, definition);

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

    private static void AppendRoutingPackTokens(StringBuilder sb, ToolDefinition definition) {
        var category = ResolvePlannerCategory(definition);
        var packHint = ResolvePlannerPackHint(definition, category);
        if (packHint.Length == 0) {
            return;
        }

        var appended = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AppendPackToken(string value) {
            var token = (value ?? string.Empty).Trim();
            if (token.Length == 0 || !appended.Add(token)) {
                return;
            }

            sb.Append(" pack ").Append(token);
            sb.Append(" pack:").Append(token);
        }

        foreach (var alias in ToolSelectionMetadata.GetPackSearchTokens(packHint)) {
            AppendPackToken(alias);
        }
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
