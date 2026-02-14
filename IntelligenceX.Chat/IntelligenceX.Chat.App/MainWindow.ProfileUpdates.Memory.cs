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

    private static IReadOnlyList<string> BuildLocalContextFallbackLines(ConversationRuntime conversation, string userText) {
        ArgumentNullException.ThrowIfNull(conversation);

        // Prefer local-history fallback when no remote thread exists or the
        // user asks context-dependent follow-ups ("check this", "same", etc.).
        var needsFallback = string.IsNullOrWhiteSpace(conversation.ThreadId)
                            || LooksLikeContextDependentFollowUp(userText);
        if (!needsFallback) {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var remaining = 6;
        for (var i = conversation.Messages.Count - 1; i >= 0 && remaining > 0; i--) {
            var message = conversation.Messages[i];
            if (string.IsNullOrWhiteSpace(message.Text)) {
                continue;
            }

            if (string.Equals(message.Role, "Tools", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "System", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase)
                && string.Equals(message.Text.Trim(), (userText ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var compact = CompactMessageForContext(message.Text);
            if (compact.Length == 0) {
                continue;
            }

            var role = string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                ? "Assistant"
                : "User";
            lines.Add(role + ": " + compact);
            remaining--;
        }

        lines.Reverse();
        return lines;
    }

    private IReadOnlyList<string> BuildPersistentMemoryContextLines(string userText) {
        if (!_persistentMemoryEnabled) {
            return Array.Empty<string>();
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        _appState.MemoryFacts = facts;
        if (facts.Count == 0) {
            return Array.Empty<string>();
        }

        var normalizedUserText = (userText ?? string.Empty).Trim();
        if (normalizedUserText.Length == 0) {
            return BuildPersistentMemoryLinesForEmptyQuery(facts);
        }

        PruneMemorySemanticVectorCache(facts);
        var nowUtc = DateTime.UtcNow;
        var userTokens = TokenizeMemorySemanticText(normalizedUserText);
        var normalizedUserTokens = NormalizeMemoryTokenSet(userTokens);
        var tokenDocumentFrequency = BuildMemoryTokenDocumentFrequency(facts);
        var effectiveUserTokens = SelectHighSignalUserTokens(normalizedUserTokens, tokenDocumentFrequency, facts.Count);
        var querySemanticVector = BuildSemanticMemoryVector(normalizedUserText);
        var scoredFacts = new List<ScoredMemoryFact>(facts.Count);

        foreach (var fact in facts) {
            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            var score = fact.Weight * 1.4d;
            var semanticHits = 0;
            var matchedTokens = new HashSet<string>(MemoryTokenComparer);
            var tags = fact.Tags ?? Array.Empty<string>();

            var factSemanticVector = GetOrBuildMemoryFactSemanticVector(fact);
            var semanticSimilarity = ComputeSemanticVectorCosine(querySemanticVector, factSemanticVector);
            if (semanticSimilarity > 0d) {
                score += Math.Min(3.6d, semanticSimilarity * 4.8d);
                if (semanticSimilarity >= 0.24d) {
                    semanticHits += 2;
                } else if (semanticSimilarity >= 0.12d) {
                    semanticHits++;
                }
            }

            if (normalizedUserText.Length > 0
                && (normalizedUserText.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(normalizedUserText, StringComparison.OrdinalIgnoreCase))) {
                score += 2.25d;
                semanticHits += 2;
            }

            var factTokens = TokenizeMemorySemanticText(text);
            var factTokenOverlap = CountNewTokenMatchesFromNormalizedUserTokens(effectiveUserTokens, factTokens, matchedTokens);
            if (factTokenOverlap > 0) {
                score += Math.Min(5d, factTokenOverlap * 1.35d);
                semanticHits += factTokenOverlap;
            }

            for (var i = 0; i < tags.Length; i++) {
                var tag = (tags[i] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }

                if (normalizedUserText.Length > 0 && normalizedUserText.Contains(tag, StringComparison.OrdinalIgnoreCase)) {
                    score += 1.1d;
                    semanticHits++;
                }

                var tagTokens = TokenizeMemorySemanticText(tag);
                var tagTokenOverlap = CountNewTokenMatchesFromNormalizedUserTokens(effectiveUserTokens, tagTokens, matchedTokens);
                if (tagTokenOverlap > 0) {
                    score += Math.Min(2.5d, tagTokenOverlap * 0.75d);
                    semanticHits += tagTokenOverlap;
                }
            }

            var updatedUtc = NormalizeMemoryUpdatedUtcForRecency(fact.UpdatedUtc, nowUtc);
            var ageHours = Math.Max(0d, (nowUtc - updatedUtc).TotalHours);
            if (ageHours <= 24d) {
                score += 0.9d;
            } else if (ageHours <= 72d) {
                score += 0.55d;
            } else if (ageHours <= 168d) {
                score += 0.2d;
            }

            scoredFacts.Add(new ScoredMemoryFact(text, fact.Weight, updatedUtc, score, semanticHits, semanticSimilarity, factSemanticVector));
        }

        if (scoredFacts.Count == 0) {
            return Array.Empty<string>();
        }

        scoredFacts.Sort(static (left, right) => {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            var similarityCompare = right.SemanticSimilarity.CompareTo(left.SemanticSimilarity);
            if (similarityCompare != 0) {
                return similarityCompare;
            }

            var weightCompare = right.Weight.CompareTo(left.Weight);
            if (weightCompare != 0) {
                return weightCompare;
            }

            var leftUpdatedUtc = left.UpdatedUtc;
            var rightUpdatedUtc = right.UpdatedUtc;
            return rightUpdatedUtc.CompareTo(leftUpdatedUtc);
        });

        var selectedFacts = SelectDiverseMemoryFacts(scoredFacts, maxCount: 10, userTokenCount: effectiveUserTokens.Count);
        var lines = new List<string>(selectedFacts.Count);
        for (var i = 0; i < selectedFacts.Count; i++) {
            var text = (selectedFacts[i].Text ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            lines.Add(text);
        }

        if (lines.Count == 0) {
            var fallbackCount = Math.Min(3, scoredFacts.Count);
            for (var i = 0; i < fallbackCount; i++) {
                var text = (scoredFacts[i].Text ?? string.Empty).Trim();
                if (text.Length == 0) {
                    continue;
                }

                lines.Add(text);
            }
        }

        var topScore = scoredFacts.Count > 0 ? scoredFacts[0].Score : 0d;
        var topSimilarity = scoredFacts.Count > 0 ? scoredFacts[0].SemanticSimilarity : 0d;
        var averageSelectionSimilarity = ComputeAveragePairSimilarity(selectedFacts);
        var averageSelectionRelevance = ComputeAverageRelevance(selectedFacts, topScore);
        RememberLastMemoryDebugSnapshot(
            availableFacts: facts.Count,
            candidateFacts: scoredFacts.Count,
            selectedFacts: lines.Count,
            userTokenCount: effectiveUserTokens.Count,
            topScore: topScore,
            topSemanticSimilarity: topSimilarity,
            averageSelectedSimilarity: averageSelectionSimilarity,
            averageSelectedRelevance: averageSelectionRelevance);
        TraceMemorySelectionDiagnostics(
            availableFacts: facts.Count,
            selectedFacts: lines.Count,
            userTokenCount: effectiveUserTokens.Count,
            topScore: topScore,
            topSemanticSimilarity: topSimilarity,
            averageSelectionSimilarity: averageSelectionSimilarity,
            averageSelectionRelevance: averageSelectionRelevance);

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private void RememberLastMemoryDebugSnapshot(
        int availableFacts,
        int candidateFacts,
        int selectedFacts,
        int userTokenCount,
        double topScore,
        double topSemanticSimilarity,
        double averageSelectedSimilarity,
        double averageSelectedRelevance) {
        var quality = ComputeMemoryDebugQuality(averageSelectedRelevance, averageSelectedSimilarity, selectedFacts);
        int nextSequence;
        int normalizedCacheEntries;
        lock (_memoryDiagnosticsSync) {
            nextSequence = unchecked(_memoryDebugSequence + 1);
            _memoryDebugSequence = nextSequence;
            normalizedCacheEntries = Math.Max(0, _memorySemanticVectorCache.Count);
        }
        var snapshot = new MemoryDebugSnapshot {
            UpdatedUtc = DateTime.UtcNow,
            Sequence = nextSequence,
            AvailableFacts = Math.Max(0, availableFacts),
            CandidateFacts = Math.Max(0, candidateFacts),
            SelectedFacts = Math.Max(0, selectedFacts),
            UserTokenCount = Math.Max(0, userTokenCount),
            TopScore = double.IsFinite(topScore) ? Math.Max(0d, topScore) : 0d,
            TopSemanticSimilarity = double.IsFinite(topSemanticSimilarity) ? Math.Clamp(topSemanticSimilarity, 0d, 1d) : 0d,
            AverageSelectedSimilarity = double.IsFinite(averageSelectedSimilarity) ? Math.Clamp(averageSelectedSimilarity, 0d, 1d) : 0d,
            AverageSelectedRelevance = double.IsFinite(averageSelectedRelevance) ? Math.Clamp(averageSelectedRelevance, 0d, 1d) : 0d,
            CacheEntries = normalizedCacheEntries,
            Quality = quality
        };
        lock (_memoryDiagnosticsSync) {
            _lastMemoryDebugSnapshot = snapshot;
            _memoryDebugHistory.Add(snapshot);
            if (_memoryDebugHistory.Count > 24) {
                _memoryDebugHistory.RemoveRange(0, _memoryDebugHistory.Count - 24);
            }
        }
    }

    private static string ComputeMemoryDebugQuality(double averageSelectedRelevance, double averageSelectedSimilarity, int selectedFacts) {
        if (selectedFacts <= 0) {
            return "none";
        }

        var relevance = double.IsFinite(averageSelectedRelevance) ? Math.Clamp(averageSelectedRelevance, 0d, 1d) : 0d;
        var similarity = double.IsFinite(averageSelectedSimilarity) ? Math.Clamp(averageSelectedSimilarity, 0d, 1d) : 0d;

        // "good" means: high relevance and not overly repetitive.
        if (relevance >= 0.62d && similarity <= 0.78d) {
            return "good";
        }

        // "ok" means: decent relevance, or strong relevance but somewhat repetitive.
        if (relevance >= 0.46d) {
            return "ok";
        }

        return "low";
    }

    private static DateTime NormalizeMemoryUpdatedUtcForRecency(DateTime value, DateTime nowUtc) {
        if (value == default) {
            return nowUtc;
        }

        if (value.Kind == DateTimeKind.Utc) {
            return ClampMemoryUpdatedUtcToNow(value, nowUtc);
        }

        if (value.Kind == DateTimeKind.Local) {
            return ClampMemoryUpdatedUtcToNow(value.ToUniversalTime(), nowUtc);
        }

        var unspecifiedAsUtc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        var unspecifiedAsLocalUtc = DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime();
        var utcDistanceHours = Math.Abs((nowUtc - unspecifiedAsUtc).TotalHours);
        var localDistanceHours = Math.Abs((nowUtc - unspecifiedAsLocalUtc).TotalHours);
        var selectedUtc = localDistanceHours <= utcDistanceHours ? unspecifiedAsLocalUtc : unspecifiedAsUtc;
        return ClampMemoryUpdatedUtcToNow(selectedUtc, nowUtc);
    }

    private static DateTime ClampMemoryUpdatedUtcToNow(DateTime valueUtc, DateTime nowUtc) {
        return valueUtc > nowUtc ? nowUtc : valueUtc;
    }

    private static IReadOnlyList<string> BuildPersistentMemoryLinesForEmptyQuery(List<ChatMemoryFactState> facts) {
        if (facts.Count == 0) {
            return Array.Empty<string>();
        }

        var ordered = new List<ChatMemoryFactState>(facts);
        var nowUtc = DateTime.UtcNow;
        ordered.Sort((a, b) => {
            var weightCompare = b.Weight.CompareTo(a.Weight);
            if (weightCompare != 0) {
                return weightCompare;
            }

            var aUpdatedUtc = NormalizeMemoryUpdatedUtcForRecency(a.UpdatedUtc, nowUtc);
            var bUpdatedUtc = NormalizeMemoryUpdatedUtcForRecency(b.UpdatedUtc, nowUtc);
            return bUpdatedUtc.CompareTo(aUpdatedUtc);
        });

        var lines = new List<string>(8);
        for (var i = 0; i < ordered.Count && lines.Count < 8; i++) {
            var text = (ordered[i].Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            if (ordered[i].Weight >= 3 || lines.Count < 3) {
                lines.Add(text);
            }
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private static HashSet<string> TokenizeMemorySemanticText(string text) {
        var tokens = new HashSet<string>(MemoryTokenComparer);
        if (string.IsNullOrWhiteSpace(text)) {
            return tokens;
        }

        var parts = MemoryTokenSplitRegex.Split(text.Normalize(NormalizationForm.FormKC));
        for (var i = 0; i < parts.Length; i++) {
            var token = NormalizeMemoryToken(parts[i]);
            if (!IsSemanticMemoryTokenCandidate(token)) {
                continue;
            }

            tokens.Add(token);
        }

        return tokens;
    }

    private static int CountNewTokenMatches(IReadOnlySet<string> userTokens, IReadOnlySet<string> candidateTokens, HashSet<string> seen) {
        var normalizedUserTokens = NormalizeMemoryTokenSet(userTokens);
        return CountNewTokenMatchesFromNormalizedUserTokens(normalizedUserTokens, candidateTokens, seen);
    }

    private static int CountNewTokenMatchesFromNormalizedUserTokens(
        IReadOnlySet<string> normalizedUserTokens,
        IReadOnlySet<string> candidateTokens,
        HashSet<string> seen) {
        if (normalizedUserTokens.Count == 0 || candidateTokens.Count == 0) {
            return 0;
        }

        var matches = 0;
        foreach (var token in candidateTokens) {
            var normalizedToken = NormalizeMemoryToken(token);
            if (normalizedToken.Length == 0 || !normalizedUserTokens.Contains(normalizedToken)) {
                continue;
            }
            if (seen.Add(normalizedToken)) {
                matches++;
            }
        }

        return matches;
    }

    private static HashSet<string> NormalizeMemoryTokenSet(IReadOnlySet<string> tokens) {
        if (tokens.Count == 0) {
            return new HashSet<string>(MemoryTokenComparer);
        }

        var normalized = new HashSet<string>(MemoryTokenComparer);
        foreach (var token in tokens) {
            var normalizedToken = NormalizeMemoryToken(token);
            if (normalizedToken.Length == 0) {
                continue;
            }

            normalized.Add(normalizedToken);
        }

        return normalized;
    }

    private void PruneMemorySemanticVectorCache(IReadOnlyList<ChatMemoryFactState> facts) {
        var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < facts.Count; i++) {
            var id = (facts[i].Id ?? string.Empty).Trim();
            if (id.Length > 0) {
                activeIds.Add(id);
            }
        }

        lock (_memoryDiagnosticsSync) {
            if (_memorySemanticVectorCache.Count == 0) {
                return;
            }

            if (activeIds.Count == 0) {
                _memorySemanticVectorCache.Clear();
                return;
            }

            var staleKeys = new List<string>();
            foreach (var pair in _memorySemanticVectorCache) {
                if (!activeIds.Contains(pair.Key)) {
                    staleKeys.Add(pair.Key);
                }
            }

            for (var i = 0; i < staleKeys.Count; i++) {
                _memorySemanticVectorCache.Remove(staleKeys[i]);
            }
        }
    }

    private Dictionary<int, double> GetOrBuildMemoryFactSemanticVector(ChatMemoryFactState fact) {
        var id = (fact.Id ?? string.Empty).Trim();
        if (id.Length == 0) {
            return BuildSemanticMemoryVector(BuildMemorySemanticSource(fact.Fact, fact.Tags ?? Array.Empty<string>()));
        }

        var signature = BuildMemoryFactSemanticSignature(fact);
        lock (_memoryDiagnosticsSync) {
            if (_memorySemanticVectorCache.TryGetValue(id, out var cached)
                && string.Equals(cached.Signature, signature, StringComparison.Ordinal)) {
                return cached.Vector;
            }
        }

        var vector = BuildSemanticMemoryVector(BuildMemorySemanticSource(fact.Fact, fact.Tags ?? Array.Empty<string>()));
        lock (_memoryDiagnosticsSync) {
            if (_memorySemanticVectorCache.TryGetValue(id, out var cached)
                && string.Equals(cached.Signature, signature, StringComparison.Ordinal)) {
                return cached.Vector;
            }

            _memorySemanticVectorCache[id] = new MemorySemanticVectorCacheEntry {
                Signature = signature,
                Vector = vector
            };
        }
        return vector;
    }

    private const int MemorySemanticVectorDimensions = 384;

    private static string BuildMemorySemanticSource(string factText, IReadOnlyList<string> tags) {
        if (tags.Count == 0) {
            return factText ?? string.Empty;
        }

        var sb = new StringBuilder((factText?.Length ?? 0) + (tags.Count * 24));
        if (!string.IsNullOrWhiteSpace(factText)) {
            sb.Append(factText!.Trim());
        }

        for (var i = 0; i < tags.Count; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0) {
                continue;
            }

            if (sb.Length > 0) {
                sb.Append(' ');
            }

            sb.Append(tag);
        }

        return sb.ToString();
    }

    private static string BuildMemoryFactSemanticSignature(ChatMemoryFactState fact) {
        var text = (fact.Fact ?? string.Empty).Trim();
        var tags = fact.Tags ?? Array.Empty<string>();
        var sb = new StringBuilder(text.Length + (tags.Length * 24) + 24);
        sb.Append(text).Append('|').Append(fact.UpdatedUtc.Ticks);
        for (var i = 0; i < tags.Length; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0) {
                continue;
            }

            sb.Append('|').Append(tag);
        }

        return sb.ToString();
    }

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

    private static List<ChatMemoryFactState> NormalizeMemoryFacts(List<ChatMemoryFactState>? facts) {
        if (facts is null || facts.Count == 0) {
            return new List<ChatMemoryFactState>();
        }

        var normalized = new List<ChatMemoryFactState>(facts.Count);
        var seenByFact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts) {
            if (fact is null) {
                continue;
            }

            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            if (!seenByFact.Add(text)) {
                continue;
            }

            var id = string.IsNullOrWhiteSpace(fact.Id) ? Guid.NewGuid().ToString("N") : fact.Id.Trim();
            var weight = Math.Clamp(fact.Weight, 1, 5);
            var tags = NormalizeMemoryTags(fact.Tags);
            var updatedUtc = fact.UpdatedUtc == default ? DateTime.UtcNow : EnsureUtc(fact.UpdatedUtc);

            normalized.Add(new ChatMemoryFactState {
                Id = id,
                Fact = text,
                Weight = weight,
                Tags = tags,
                UpdatedUtc = updatedUtc
            });
        }

        normalized.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        if (normalized.Count > 120) {
            normalized.RemoveRange(120, normalized.Count - 120);
        }

        return normalized;
    }

    private static string[] NormalizeMemoryTags(string[]? tags) {
        if (tags is null || tags.Length == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(tags.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < tags.Length; i++) {
            var tag = (tags[i] ?? string.Empty).Trim();
            if (tag.Length == 0 || tag.Length > 40) {
                continue;
            }

            if (seen.Add(tag)) {
                list.Add(tag);
            }
        }

        return list.Count == 0 ? Array.Empty<string>() : list.ToArray();
    }

    private async Task SetPersistentMemoryEnabledAsync(bool enabled) {
        _persistentMemoryEnabled = enabled;
        _appState.PersistentMemoryEnabled = enabled;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private void ResetMemoryDiagnosticsState() {
        lock (_memoryDiagnosticsSync) {
            _memorySemanticVectorCache.Clear();
            _lastMemoryDebugSnapshot = null;
            _memoryDebugHistory.Clear();
            _memoryDebugSequence = 0;
        }
    }

    private async Task AddMemoryFactAsync(string? factText, int weight = 3, string[]? tags = null) {
        var text = (factText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return;
        }

        if (!_persistentMemoryEnabled) {
            _persistentMemoryEnabled = true;
            _appState.PersistentMemoryEnabled = true;
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        var changed = UpsertMemoryFact(facts, text, weight, tags);
        if (!changed) {
            return;
        }

        _appState.MemoryFacts = facts;
        ResetMemoryDiagnosticsState();
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task RemoveMemoryFactAsync(string? memoryId) {
        var id = (memoryId ?? string.Empty).Trim();
        if (id.Length == 0) {
            return;
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        var removed = facts.RemoveAll(fact => string.Equals(fact.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed <= 0) {
            return;
        }

        _appState.MemoryFacts = facts;
        ResetMemoryDiagnosticsState();
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ClearPersistentMemoryAsync() {
        var facts = _appState.MemoryFacts;
        if (facts is null || facts.Count == 0) {
            return;
        }

        facts.Clear();
        _appState.MemoryFacts = facts;
        ResetMemoryDiagnosticsState();
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ForceRecomputeMemoryCacheAsync() {
        // Debug action: clear semantic vectors and diagnostics so the next turn recomputes fresh.
        ResetMemoryDiagnosticsState();
        await PublishOptionsStateAsync().ConfigureAwait(false);
    }

    private async Task<bool> ApplyMemoryUpdateAsync(AssistantMemoryUpdate update) {
        if (!_persistentMemoryEnabled) {
            return false;
        }

        var facts = NormalizeMemoryFacts(_appState.MemoryFacts);
        var changed = false;

        if (update.DeleteFacts is { Count: > 0 }) {
            for (var i = 0; i < update.DeleteFacts.Count; i++) {
                var candidate = (update.DeleteFacts[i] ?? string.Empty).Trim();
                if (candidate.Length == 0) {
                    continue;
                }

                var removed = facts.RemoveAll(fact =>
                    string.Equals(fact.Id, candidate, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(fact.Fact, candidate, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) {
                    changed = true;
                }
            }
        }

        if (update.Upserts is { Count: > 0 }) {
            for (var i = 0; i < update.Upserts.Count; i++) {
                var upsert = update.Upserts[i];
                if (upsert is null || string.IsNullOrWhiteSpace(upsert.Fact)) {
                    continue;
                }

                if (UpsertMemoryFact(facts, upsert.Fact, upsert.Weight, upsert.Tags)) {
                    changed = true;
                }
            }
        }

        if (!changed) {
            return false;
        }

        _appState.MemoryFacts = facts;
        ResetMemoryDiagnosticsState();
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
        return true;
    }

    private static bool UpsertMemoryFact(List<ChatMemoryFactState> facts, string? factText, int weight, string[]? tags) {
        var text = (factText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        var normalizedTags = NormalizeMemoryTags(tags);
        var clampedWeight = Math.Clamp(weight, 1, 5);
        var now = DateTime.UtcNow;

        for (var i = 0; i < facts.Count; i++) {
            if (!string.Equals(facts[i].Fact, text, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            facts[i].Weight = clampedWeight;
            facts[i].Tags = normalizedTags;
            facts[i].UpdatedUtc = now;
            return true;
        }

        facts.Add(new ChatMemoryFactState {
            Id = Guid.NewGuid().ToString("N"),
            Fact = text,
            Weight = clampedWeight,
            Tags = normalizedTags,
            UpdatedUtc = now
        });

        if (facts.Count > 120) {
            facts.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            facts.RemoveRange(120, facts.Count - 120);
        }

        return true;
    }

    private static bool LooksLikeContextDependentFollowUp(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        if (text.Contains('\n', StringComparison.Ordinal) || text.Length > 96) {
            return false;
        }

        var tokenCount = 0;
        var inToken = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch)) {
                if (!inToken) {
                    tokenCount++;
                    inToken = true;
                }
            } else {
                inToken = false;
            }
        }

        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= 6 && text.Length <= 64) {
            return true;
        }

        return tokenCount <= 8 && text.Contains('?', StringComparison.Ordinal);
    }

    private static string CompactMessageForContext(string text) {
        var normalized = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length > 220) {
            normalized = normalized[..220].TrimEnd() + "...";
        }

        return normalized;
    }

}
