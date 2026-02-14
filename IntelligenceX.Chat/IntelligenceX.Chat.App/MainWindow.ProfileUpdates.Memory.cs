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

        var nowUtc = DateTime.UtcNow;
        var userTokens = TokenizeMemorySemanticText(normalizedUserText);
        var normalizedUserTokens = NormalizeMemoryTokenSet(userTokens);
        var scoredFacts = new List<ScoredMemoryFact>(facts.Count);

        foreach (var fact in facts) {
            var text = (fact.Fact ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            var score = fact.Weight * 1.4d;
            var semanticHits = 0;
            var matchedTokens = new HashSet<string>(MemoryTokenComparer);

            if (normalizedUserText.Length > 0
                && (normalizedUserText.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || text.Contains(normalizedUserText, StringComparison.OrdinalIgnoreCase))) {
                score += 2.25d;
                semanticHits += 2;
            }

            var factTokens = TokenizeMemorySemanticText(text);
            var factTokenOverlap = CountNewTokenMatchesFromNormalizedUserTokens(normalizedUserTokens, factTokens, matchedTokens);
            if (factTokenOverlap > 0) {
                score += Math.Min(5d, factTokenOverlap * 1.35d);
                semanticHits += factTokenOverlap;
            }

            var tags = fact.Tags ?? Array.Empty<string>();
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
                var tagTokenOverlap = CountNewTokenMatchesFromNormalizedUserTokens(normalizedUserTokens, tagTokens, matchedTokens);
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

            scoredFacts.Add(new ScoredMemoryFact(text, fact.Weight, updatedUtc, score, semanticHits));
        }

        if (scoredFacts.Count == 0) {
            return Array.Empty<string>();
        }

        scoredFacts.Sort(static (left, right) => {
            var scoreCompare = right.Score.CompareTo(left.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            var weightCompare = right.Weight.CompareTo(left.Weight);
            if (weightCompare != 0) {
                return weightCompare;
            }

            var leftUpdatedUtc = left.UpdatedUtc;
            var rightUpdatedUtc = right.UpdatedUtc;
            return rightUpdatedUtc.CompareTo(leftUpdatedUtc);
        });

        var lines = new List<string>(Math.Min(10, scoredFacts.Count));
        for (var i = 0; i < scoredFacts.Count && lines.Count < 10; i++) {
            var fact = scoredFacts[i];
            var include = i < 2
                          || fact.Score >= 3.2d
                          || fact.SemanticHits >= 2
                          || (fact.SemanticHits >= 1 && fact.Score >= 2.5d);
            if (!include) {
                continue;
            }

            var text = (fact.Text ?? string.Empty).Trim();
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

        return lines.Count == 0 ? Array.Empty<string>() : lines;
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
            if (token.Length < 3 || MemoryTokenStopWords.Contains(token)) {
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

    private static readonly HashSet<string> MemoryTokenStopWords = new(MemoryTokenComparer) {
        "the", "and", "with", "from", "that", "this", "for", "you", "your", "have", "show", "give", "list",
        "check", "please", "about", "into", "just", "today", "need", "want", "when", "what", "where", "then",
        "them", "they", "their", "there", "after", "before", "will", "should", "would", "could", "also", "been",
        "being", "while", "does", "did", "done", "using", "used", "more", "same", "again"
    };

    private readonly record struct ScoredMemoryFact(
        string Text,
        int Weight,
        DateTime UpdatedUtc,
        double Score,
        int SemanticHits);

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
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
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

        return text.Contains("check this", StringComparison.OrdinalIgnoreCase)
               || text.Contains("check that", StringComparison.OrdinalIgnoreCase)
               || text.Contains("that one", StringComparison.OrdinalIgnoreCase)
               || text.Contains("same", StringComparison.OrdinalIgnoreCase)
               || text.Contains("again", StringComparison.OrdinalIgnoreCase)
               || text.Contains("as above", StringComparison.OrdinalIgnoreCase)
               || text.Contains("this please", StringComparison.OrdinalIgnoreCase)
               || text.Equals("ok?", StringComparison.OrdinalIgnoreCase);
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
