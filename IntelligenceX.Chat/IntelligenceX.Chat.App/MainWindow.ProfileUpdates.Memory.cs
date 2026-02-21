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

            if (_memorySemanticVectorCache.Count > MaxMemorySemanticVectorCacheEntries) {
                _memorySemanticVectorCache.Clear();
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

            // Hard cap as a safety net. Under normal operation, this is bounded by memory fact retention.
            if (_memorySemanticVectorCache.Count > MaxMemorySemanticVectorCacheEntries) {
                _memorySemanticVectorCache.Clear();
            }
        }
        return vector;
    }

    private const int MemorySemanticVectorDimensions = 384;
    private const int MaxMemorySemanticVectorCacheEntries = 160;

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

}
