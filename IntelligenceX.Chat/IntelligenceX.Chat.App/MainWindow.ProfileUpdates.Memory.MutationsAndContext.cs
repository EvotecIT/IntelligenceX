using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
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
