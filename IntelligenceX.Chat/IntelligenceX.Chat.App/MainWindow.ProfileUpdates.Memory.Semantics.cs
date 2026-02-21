using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.Client;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private static Dictionary<int, double> BuildSemanticMemoryVector(string text) {
        var vector = new Dictionary<int, double>();
        if (string.IsNullOrWhiteSpace(text)) {
            return vector;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        var semanticTokens = TokenizeMemorySemanticText(normalized);
        foreach (var token in semanticTokens) {
            AddSemanticFeature(vector, token, weight: 1.2d);

            var padded = "_" + token + "_";
            AddCharNgramFeatures(vector, padded, minN: 2, maxN: 4, weightPerGram: 0.35d);
        }

        // Character n-grams over normalized text improve language/script coverage
        // when whitespace tokenization is weak (for example CJK or short phrases).
        AddCharNgramFeatures(vector, normalized.ToLowerInvariant(), minN: 2, maxN: 3, weightPerGram: 0.15d);
        return vector;
    }

    private static void AddCharNgramFeatures(Dictionary<int, double> vector, string text, int minN, int maxN, double weightPerGram) {
        if (string.IsNullOrWhiteSpace(text) || minN <= 0 || maxN < minN || weightPerGram <= 0d) {
            return;
        }

        var normalized = text.Normalize(NormalizationForm.FormKC);
        for (var n = minN; n <= maxN; n++) {
            if (normalized.Length < n) {
                continue;
            }

            for (var i = 0; i <= normalized.Length - n; i++) {
                var gram = normalized.Substring(i, n).Trim();
                if (gram.Length == 0 || IsWhitespaceOnly(gram)) {
                    continue;
                }

                AddSemanticFeature(vector, $"{n}:{gram}", weightPerGram);
            }
        }
    }

    private static bool IsWhitespaceOnly(string text) {
        for (var i = 0; i < text.Length; i++) {
            if (!char.IsWhiteSpace(text[i])) {
                return false;
            }
        }

        return true;
    }

    private static void AddSemanticFeature(Dictionary<int, double> vector, string feature, double weight) {
        if (string.IsNullOrWhiteSpace(feature) || weight <= 0d) {
            return;
        }

        var bucket = HashToFeatureBucket(feature);
        vector.TryGetValue(bucket, out var current);
        vector[bucket] = current + weight;
    }

    private static int HashToFeatureBucket(string value) {
        unchecked {
            uint hash = 2166136261u;
            for (var i = 0; i < value.Length; i++) {
                hash ^= value[i];
                hash *= 16777619u;
            }

            return (int)(hash % MemorySemanticVectorDimensions);
        }
    }

    private static double ComputeSemanticVectorCosine(IReadOnlyDictionary<int, double> left, IReadOnlyDictionary<int, double> right) {
        if (left.Count == 0 || right.Count == 0) {
            return 0d;
        }

        IReadOnlyDictionary<int, double> smaller = left;
        IReadOnlyDictionary<int, double> larger = right;
        if (left.Count > right.Count) {
            smaller = right;
            larger = left;
        }

        var dot = 0d;
        var leftNorm = 0d;
        var rightNorm = 0d;

        foreach (var pair in left) {
            leftNorm += pair.Value * pair.Value;
        }

        foreach (var pair in right) {
            rightNorm += pair.Value * pair.Value;
        }

        foreach (var pair in smaller) {
            if (larger.TryGetValue(pair.Key, out var value)) {
                dot += pair.Value * value;
            }
        }

        if (leftNorm <= 0d || rightNorm <= 0d) {
            return 0d;
        }

        var denom = Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm);
        if (denom <= 0d) {
            return 0d;
        }

        var similarity = dot / denom;
        if (double.IsNaN(similarity) || double.IsInfinity(similarity)) {
            return 0d;
        }

        return Math.Clamp(similarity, 0d, 1d);
    }

    private static List<ScoredMemoryFact> SelectDiverseMemoryFacts(IReadOnlyList<ScoredMemoryFact> scoredFacts, int maxCount, int userTokenCount) {
        if (scoredFacts.Count == 0 || maxCount <= 0) {
            return new List<ScoredMemoryFact>();
        }

        var topScore = Math.Max(scoredFacts[0].Score, 0.001d);
        var relevanceGate = ComputeMinimumRelevanceGate(userTokenCount);
        var candidates = new List<int>(Math.Min(scoredFacts.Count, maxCount * 3));
        for (var i = 0; i < scoredFacts.Count; i++) {
            var fact = scoredFacts[i];
            if (!ShouldIncludeMemoryFactCandidate(i, fact)) {
                continue;
            }

            if (i >= 2) {
                var relevance = ComputeMemoryRelevance(fact, topScore);
                if (relevance < relevanceGate
                    && fact.SemanticHits == 0
                    && fact.SemanticSimilarity < 0.18d) {
                    continue;
                }
            }

            candidates.Add(i);
        }

        if (candidates.Count == 0) {
            var fallbackCount = Math.Min(2, scoredFacts.Count);
            for (var i = 0; i < fallbackCount; i++) {
                candidates.Add(i);
            }
        }

        var relevanceWeight = ComputeMmrLambda(userTokenCount);
        var noveltyWeight = 1d - relevanceWeight;
        var selected = new List<ScoredMemoryFact>(Math.Min(maxCount, candidates.Count));
        var selectedIndexes = new List<int>(Math.Min(maxCount, candidates.Count));

        while (candidates.Count > 0 && selected.Count < maxCount) {
            var bestCandidateListIndex = -1;
            var bestCandidateScore = double.NegativeInfinity;
            var bestNoveltyPenalty = 0d;

            for (var i = 0; i < candidates.Count; i++) {
                var candidateIndex = candidates[i];
                var candidate = scoredFacts[candidateIndex];
                var relevance = ComputeMemoryRelevance(candidate, topScore);
                var noveltyPenalty = 0d;
                for (var j = 0; j < selectedIndexes.Count; j++) {
                    var selectedFact = scoredFacts[selectedIndexes[j]];
                    var similarity = ComputeSemanticVectorCosine(candidate.SemanticVector, selectedFact.SemanticVector);
                    if (similarity > noveltyPenalty) {
                        noveltyPenalty = similarity;
                    }
                }

                var mmrScore = selectedIndexes.Count == 0
                    ? relevance
                    : (relevanceWeight * relevance) - (noveltyWeight * noveltyPenalty);
                if (selectedIndexes.Count >= 2
                    && noveltyPenalty >= 0.93d
                    && relevance < Math.Max(0.88d, relevanceGate + 0.24d)) {
                    mmrScore -= 0.25d;
                }

                if (mmrScore > bestCandidateScore) {
                    bestCandidateScore = mmrScore;
                    bestCandidateListIndex = i;
                    bestNoveltyPenalty = noveltyPenalty;
                }
            }

            if (bestCandidateListIndex < 0) {
                break;
            }

            if (selected.Count >= 3
                && bestCandidateScore < (relevanceGate - 0.14d)
                && bestNoveltyPenalty >= 0.9d) {
                break;
            }

            var selectedCandidateIndex = candidates[bestCandidateListIndex];
            selectedIndexes.Add(selectedCandidateIndex);
            selected.Add(scoredFacts[selectedCandidateIndex]);
            candidates.RemoveAt(bestCandidateListIndex);
        }

        return selected;
    }

    private static bool ShouldIncludeMemoryFactCandidate(int index, ScoredMemoryFact fact) {
        return index < 2
               || fact.Score >= 3.2d
               || fact.SemanticSimilarity >= 0.22d
               || fact.SemanticHits >= 2
               || (fact.SemanticHits >= 1 && fact.Score >= 2.5d);
    }

    private static double ComputeMmrLambda(int userTokenCount) {
        if (userTokenCount <= 2) {
            return 0.68d;
        }

        if (userTokenCount <= 5) {
            return 0.74d;
        }

        return 0.82d;
    }

    private static double ComputeMinimumRelevanceGate(int userTokenCount) {
        if (userTokenCount <= 2) {
            return 0.18d;
        }

        if (userTokenCount <= 5) {
            return 0.24d;
        }

        return 0.30d;
    }

    private static double ComputeMemoryRelevance(ScoredMemoryFact fact, double topScore) {
        var normalizedScore = topScore <= 0d ? 0d : Math.Clamp(fact.Score / topScore, 0d, 1d);
        var normalizedSimilarity = Math.Clamp(fact.SemanticSimilarity, 0d, 1d);
        return (0.72d * normalizedScore) + (0.28d * normalizedSimilarity);
    }

    private static double ComputeAveragePairSimilarity(IReadOnlyList<ScoredMemoryFact> selectedFacts) {
        if (selectedFacts.Count < 2) {
            return 0d;
        }

        var pairCount = 0;
        var similaritySum = 0d;
        for (var i = 0; i < selectedFacts.Count - 1; i++) {
            for (var j = i + 1; j < selectedFacts.Count; j++) {
                pairCount++;
                similaritySum += ComputeSemanticVectorCosine(selectedFacts[i].SemanticVector, selectedFacts[j].SemanticVector);
            }
        }

        if (pairCount == 0) {
            return 0d;
        }

        return similaritySum / pairCount;
    }

    private static double ComputeAverageRelevance(IReadOnlyList<ScoredMemoryFact> selectedFacts, double topScore) {
        if (selectedFacts.Count == 0) {
            return 0d;
        }

        var relevanceSum = 0d;
        for (var i = 0; i < selectedFacts.Count; i++) {
            relevanceSum += ComputeMemoryRelevance(selectedFacts[i], topScore);
        }

        return relevanceSum / selectedFacts.Count;
    }

    [Conditional("DEBUG")]
    private static void TraceMemorySelectionDiagnostics(
        int availableFacts,
        int selectedFacts,
        int userTokenCount,
        double topScore,
        double topSemanticSimilarity,
        double averageSelectionSimilarity,
        double averageSelectionRelevance) {
        Debug.WriteLine(
            $"[memory.retrieval] facts={availableFacts} selected={selectedFacts} user_tokens={userTokenCount} "
            + $"top_score={topScore:F3} top_similarity={topSemanticSimilarity:F3} avg_selected_similarity={averageSelectionSimilarity:F3} "
            + $"avg_selected_relevance={averageSelectionRelevance:F3}");
    }

    private static Dictionary<string, int> BuildMemoryTokenDocumentFrequency(IReadOnlyList<ChatMemoryFactState> facts) {
        var frequency = new Dictionary<string, int>(MemoryTokenComparer);
        if (facts.Count == 0) {
            return frequency;
        }

        for (var i = 0; i < facts.Count; i++) {
            var fact = facts[i];
            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            var factTokens = TokenizeMemorySemanticText(text);
            var documentTokens = new HashSet<string>(factTokens, MemoryTokenComparer);
            var tags = fact.Tags ?? Array.Empty<string>();
            for (var j = 0; j < tags.Length; j++) {
                var tag = (tags[j] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }

                var tagTokens = TokenizeMemorySemanticText(tag);
                foreach (var token in tagTokens) {
                    documentTokens.Add(token);
                }
            }

            foreach (var token in documentTokens) {
                frequency.TryGetValue(token, out var count);
                frequency[token] = count + 1;
            }
        }

        return frequency;
    }

    private static HashSet<string> SelectHighSignalUserTokens(
        IReadOnlySet<string> normalizedUserTokens,
        IReadOnlyDictionary<string, int> documentFrequency,
        int documentCount) {
        var selected = new HashSet<string>(MemoryTokenComparer);
        if (normalizedUserTokens.Count == 0) {
            return selected;
        }

        if (normalizedUserTokens.Count <= 2 || documentCount <= 2) {
            foreach (var token in normalizedUserTokens) {
                selected.Add(token);
            }

            return selected;
        }

        var safeDocumentCount = Math.Max(1, documentCount);
        foreach (var token in normalizedUserTokens) {
            if (token.Length == 0) {
                continue;
            }

            documentFrequency.TryGetValue(token, out var tokenDocumentCount);
            var coverageRatio = tokenDocumentCount / (double)safeDocumentCount;
            var veryBroad = tokenDocumentCount >= 3 && coverageRatio >= 0.72d && token.Length <= 5;
            if (veryBroad) {
                continue;
            }

            selected.Add(token);
        }

        if (selected.Count == 0) {
            foreach (var token in normalizedUserTokens) {
                selected.Add(token);
            }
        }

        return selected;
    }

    private static bool IsSemanticMemoryTokenCandidate(string token) {
        if (token.Length == 0) {
            return false;
        }

        var hasLetter = false;
        var hasDigit = false;
        var latinLetterCount = 0;
        var nonLatinLetterCount = 0;
        for (var i = 0; i < token.Length; i++) {
            var ch = token[i];
            if (char.IsLetter(ch)) {
                hasLetter = true;
                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z')) {
                    latinLetterCount++;
                } else {
                    nonLatinLetterCount++;
                }
            } else if (char.IsDigit(ch)) {
                hasDigit = true;
            }
        }

        if (!hasLetter && !hasDigit) {
            return false;
        }

        if (!hasLetter) {
            return false;
        }

        if (nonLatinLetterCount > 0) {
            return token.Length >= 2;
        }

        if (latinLetterCount > 0) {
            return token.Length >= 3;
        }

        return token.Length >= 2;
    }

    private static string NormalizeMemoryToken(string? token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return string.Empty;
        }

        var normalized = token.Normalize(NormalizationForm.FormKC).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var decomposed = normalized.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        for (var i = 0; i < decomposed.Length; i++) {
            var category = CharUnicodeInfo.GetUnicodeCategory(decomposed[i]);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) {
                continue;
            }

            builder.Append(decomposed[i]);
        }

        return builder.ToString().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }

    private readonly record struct ScoredMemoryFact(
        string Text,
        int Weight,
        DateTime UpdatedUtc,
        double Score,
        int SemanticHits,
        double SemanticSimilarity,
        Dictionary<int, double> SemanticVector);

}
