using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Conversation;

internal sealed record DesktopChatMemorySelection(
    IReadOnlyList<string> Lines,
    List<ChatMemoryFactState> NormalizedFacts,
    int CandidateFacts,
    int UserTokenCount,
    double TopScore,
    double TopSimilarity,
    double AverageSelectedSimilarity,
    double AverageSelectedRelevance);

/// <summary>
/// Selects persistent memory for every desktop chat surface.
/// </summary>
internal static class DesktopChatMemorySelector {
    private const int MaximumStoredFacts = 120;
    private const int MaximumSelectedFacts = 10;
    private const int EmptyQuerySelectionLimit = 8;
    private const double DuplicateSimilarityThreshold = 0.72d;
    private static readonly StringComparer TokenComparer = StringComparer.OrdinalIgnoreCase;
    private static readonly Regex TokenSplitRegex = new(
        @"[^\p{L}\p{Nd}_]+",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal static DesktopChatMemorySelection Select(
        List<ChatMemoryFactState>? facts,
        string? userText,
        DateTime? nowUtc = null) {
        var clock = NormalizeClock(nowUtc ?? DateTime.UtcNow);
        var normalizedFacts = NormalizeFacts(facts, clock);
        if (normalizedFacts.Count == 0) {
            return Empty(normalizedFacts);
        }

        var query = (userText ?? string.Empty).Trim();
        var queryTokens = Tokenize(query);
        if (queryTokens.Count == 0) {
            var emptyQueryLines = SelectForEmptyQuery(normalizedFacts, clock);
            return new DesktopChatMemorySelection(
                emptyQueryLines,
                normalizedFacts,
                normalizedFacts.Count,
                0,
                0d,
                0d,
                0d,
                0d);
        }

        var documentFrequencies = BuildDocumentFrequencies(normalizedFacts);
        var candidates = new List<ScoredFact>(normalizedFacts.Count);
        foreach (var fact in normalizedFacts) {
            var factTokens = Tokenize(BuildSearchText(fact));
            var overlapScore = ComputeWeightedOverlap(queryTokens, factTokens, documentFrequencies, normalizedFacts.Count);
            var tokenSimilarity = ComputeJaccard(queryTokens, factTokens);
            var containsQuery = fact.Fact.Contains(query, StringComparison.OrdinalIgnoreCase)
                                || query.Contains(fact.Fact, StringComparison.OrdinalIgnoreCase);
            if (overlapScore <= 0d && !containsQuery) {
                continue;
            }

            var score = (fact.Weight * 0.8d)
                        + overlapScore
                        + (tokenSimilarity * 3d)
                        + (containsQuery ? 2.25d : 0d)
                        + ComputeRecencyBoost(fact.UpdatedUtc, clock);
            candidates.Add(new ScoredFact(fact, factTokens, score, tokenSimilarity));
        }

        if (candidates.Count == 0) {
            candidates.AddRange(BuildFallbackCandidates(normalizedFacts, clock));
        }

        candidates.Sort(CompareCandidates);
        var selected = SelectDiverse(candidates);
        var lines = selected.Select(static item => item.Fact.Fact).ToArray();
        var topScore = candidates.Count == 0 ? 0d : candidates[0].Score;

        return new DesktopChatMemorySelection(
            lines,
            normalizedFacts,
            candidates.Count,
            queryTokens.Count,
            topScore,
            candidates.Count == 0 ? 0d : candidates[0].QuerySimilarity,
            ComputeAveragePairSimilarity(selected),
            ComputeAverageRelevance(selected, topScore));
    }

    internal static List<ChatMemoryFactState> NormalizeFacts(
        List<ChatMemoryFactState>? facts,
        DateTime? nowUtc = null) {
        if (facts is null || facts.Count == 0) {
            return new List<ChatMemoryFactState>();
        }

        var clock = NormalizeClock(nowUtc ?? DateTime.UtcNow);
        var normalized = new List<ChatMemoryFactState>(facts.Count);
        var seenFacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts) {
            if (fact is null) {
                continue;
            }

            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0 || !seenFacts.Add(text)) {
                continue;
            }

            normalized.Add(new ChatMemoryFactState {
                Id = string.IsNullOrWhiteSpace(fact.Id) ? Guid.NewGuid().ToString("N") : fact.Id.Trim(),
                Fact = text,
                Weight = Math.Clamp(fact.Weight, 1, 5),
                Tags = NormalizeTags(fact.Tags),
                UpdatedUtc = NormalizeTimestamp(fact.UpdatedUtc, clock)
            });
        }

        normalized.Sort(static (left, right) => right.UpdatedUtc.CompareTo(left.UpdatedUtc));
        if (normalized.Count > MaximumStoredFacts) {
            normalized.RemoveRange(MaximumStoredFacts, normalized.Count - MaximumStoredFacts);
        }

        return normalized;
    }

    internal static HashSet<string> Tokenize(string? text) {
        var tokens = new HashSet<string>(TokenComparer);
        if (string.IsNullOrWhiteSpace(text)) {
            return tokens;
        }

        foreach (var part in TokenSplitRegex.Split(text.Normalize(NormalizationForm.FormKC))) {
            var token = NormalizeToken(part);
            if (!IsSignalToken(token)) {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static DesktopChatMemorySelection Empty(List<ChatMemoryFactState> facts) {
        return new DesktopChatMemorySelection(
            Array.Empty<string>(),
            facts,
            0,
            0,
            0d,
            0d,
            0d,
            0d);
    }

    private static IReadOnlyList<string> SelectForEmptyQuery(
        IReadOnlyList<ChatMemoryFactState> facts,
        DateTime nowUtc) {
        var ordered = facts
            .OrderByDescending(static fact => fact.Weight)
            .ThenByDescending(fact => NormalizeTimestamp(fact.UpdatedUtc, nowUtc));
        var lines = new List<string>(EmptyQuerySelectionLimit);
        foreach (var fact in ordered) {
            if (lines.Count >= EmptyQuerySelectionLimit) {
                break;
            }

            if (fact.Weight >= 3 || lines.Count < 3) {
                lines.Add(fact.Fact);
            }
        }

        return lines;
    }

    private static Dictionary<string, int> BuildDocumentFrequencies(IReadOnlyList<ChatMemoryFactState> facts) {
        var frequencies = new Dictionary<string, int>(TokenComparer);
        foreach (var fact in facts) {
            foreach (var token in Tokenize(BuildSearchText(fact))) {
                frequencies[token] = frequencies.TryGetValue(token, out var count) ? count + 1 : 1;
            }
        }

        return frequencies;
    }

    private static string BuildSearchText(ChatMemoryFactState fact) {
        if (fact.Tags.Length == 0) {
            return fact.Fact;
        }

        return fact.Fact + " " + string.Join(' ', fact.Tags);
    }

    private static double ComputeWeightedOverlap(
        IReadOnlySet<string> queryTokens,
        IReadOnlySet<string> factTokens,
        IReadOnlyDictionary<string, int> documentFrequencies,
        int documentCount) {
        var score = 0d;
        foreach (var token in queryTokens) {
            if (!factTokens.Contains(token)) {
                continue;
            }

            var frequency = documentFrequencies.TryGetValue(token, out var count) ? count : 1;
            score += 1d + Math.Log((documentCount + 1d) / (frequency + 1d));
        }

        return score;
    }

    private static List<ScoredFact> BuildFallbackCandidates(
        IReadOnlyList<ChatMemoryFactState> facts,
        DateTime nowUtc) {
        return facts
            .OrderByDescending(static fact => fact.Weight)
            .ThenByDescending(static fact => fact.UpdatedUtc)
            .Take(3)
            .Select(fact => new ScoredFact(
                fact,
                Tokenize(BuildSearchText(fact)),
                fact.Weight + ComputeRecencyBoost(fact.UpdatedUtc, nowUtc),
                0d))
            .ToList();
    }

    private static List<ScoredFact> SelectDiverse(IReadOnlyList<ScoredFact> candidates) {
        var selected = new List<ScoredFact>(Math.Min(MaximumSelectedFacts, candidates.Count));
        foreach (var candidate in candidates) {
            if (selected.Count >= MaximumSelectedFacts) {
                break;
            }

            var tooSimilar = selected.Any(existing =>
                existing.Fact.Fact.Contains(candidate.Fact.Fact, StringComparison.OrdinalIgnoreCase)
                || candidate.Fact.Fact.Contains(existing.Fact.Fact, StringComparison.OrdinalIgnoreCase)
                || ComputeJaccard(existing.Tokens, candidate.Tokens) >= DuplicateSimilarityThreshold);
            if (!tooSimilar) {
                selected.Add(candidate);
            }
        }

        if (selected.Count == 0 && candidates.Count > 0) {
            selected.Add(candidates[0]);
        }

        return selected;
    }

    private static double ComputeJaccard(IReadOnlySet<string> left, IReadOnlySet<string> right) {
        if (left.Count == 0 || right.Count == 0) {
            return 0d;
        }

        var intersection = 0;
        foreach (var token in left) {
            if (right.Contains(token)) {
                intersection++;
            }
        }

        return intersection / (double)(left.Count + right.Count - intersection);
    }

    private static double ComputeAveragePairSimilarity(IReadOnlyList<ScoredFact> selected) {
        if (selected.Count < 2) {
            return 0d;
        }

        var total = 0d;
        var pairs = 0;
        for (var left = 0; left < selected.Count - 1; left++) {
            for (var right = left + 1; right < selected.Count; right++) {
                total += ComputeJaccard(selected[left].Tokens, selected[right].Tokens);
                pairs++;
            }
        }

        return pairs == 0 ? 0d : total / pairs;
    }

    private static double ComputeAverageRelevance(IReadOnlyList<ScoredFact> selected, double topScore) {
        if (selected.Count == 0 || topScore <= 0d) {
            return 0d;
        }

        return Math.Clamp(selected.Average(item => item.Score / topScore), 0d, 1d);
    }

    private static double ComputeRecencyBoost(DateTime updatedUtc, DateTime nowUtc) {
        var ageHours = Math.Max(0d, (nowUtc - NormalizeTimestamp(updatedUtc, nowUtc)).TotalHours);
        return ageHours switch {
            <= 24d => 0.9d,
            <= 72d => 0.55d,
            <= 168d => 0.2d,
            _ => 0d
        };
    }

    private static int CompareCandidates(ScoredFact left, ScoredFact right) {
        var score = right.Score.CompareTo(left.Score);
        if (score != 0) {
            return score;
        }

        var weight = right.Fact.Weight.CompareTo(left.Fact.Weight);
        return weight != 0 ? weight : right.Fact.UpdatedUtc.CompareTo(left.Fact.UpdatedUtc);
    }

    private static string NormalizeToken(string? token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }

        var decomposed = token.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed) {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark) {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
    }

    private static bool IsSignalToken(string token) {
        if (token.Length == 0 || token.All(char.IsDigit)) {
            return false;
        }

        return !ContainsLatinLetter(token) || token.Length >= 3;
    }

    private static bool ContainsLatinLetter(string token) {
        foreach (var character in token) {
            if (character is >= '\u0041' and <= '\u024F') {
                return true;
            }
        }

        return false;
    }

    internal static string[] NormalizeTags(string[]? tags) {
        if (tags is null || tags.Length == 0) {
            return Array.Empty<string>();
        }

        return tags
            .Select(static tag => (tag ?? string.Empty).Trim())
            .Where(static tag => tag.Length is > 0 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTime NormalizeClock(DateTime value) {
        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime NormalizeTimestamp(DateTime value, DateTime nowUtc) {
        if (value == default) {
            return nowUtc;
        }

        var utc = NormalizeClock(value);
        return utc > nowUtc ? nowUtc : utc;
    }

    private sealed record ScoredFact(
        ChatMemoryFactState Fact,
        HashSet<string> Tokens,
        double Score,
        double QuerySimilarity);
}
