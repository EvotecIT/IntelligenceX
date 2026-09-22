using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeConversationStateStore {
    public async Task RecordUsageAsync(string? accountId, TokenUsageDto? usage, CancellationToken cancellationToken) {
        if (usage is null) return;

        var prompt = Positive(usage.PromptTokens);
        var completion = Positive(usage.CompletionTokens);
        var total = Positive(usage.TotalTokens);
        if (total == 0) total = checked(prompt + completion);
        var cached = Positive(usage.CachedPromptTokens);
        var reasoning = Positive(usage.ReasoningTokens);
        if (prompt == 0 && completion == 0 && total == 0 && cached == 0 && reasoning == 0) return;

        var normalizedId = (accountId ?? string.Empty).Trim();
        var key = normalizedId.Length == 0 ? "native:unknown" : "native:" + normalizedId.ToLowerInvariant();
        var label = normalizedId.Length == 0 ? "ChatGPT (unknown account)" : "ChatGPT (" + normalizedId + ")";

        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            _state = await _stateStore.UpdateAsync(_profileName, current => {
                var state = NormalizeLoadedProfileState(current);
                state.AccountUsage ??= new List<ChatAccountUsageState>();
                var snapshot = state.AccountUsage.FirstOrDefault(item =>
                    string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase));
                if (snapshot is null) {
                    snapshot = new ChatAccountUsageState { Key = key, Label = label };
                    state.AccountUsage.Add(snapshot);
                }

                if (string.IsNullOrWhiteSpace(snapshot.Label)) snapshot.Label = label;
                snapshot.PromptTokens = checked(snapshot.PromptTokens + prompt);
                snapshot.CompletionTokens = checked(snapshot.CompletionTokens + completion);
                snapshot.TotalTokens = checked(snapshot.TotalTokens + total);
                snapshot.CachedPromptTokens = checked(snapshot.CachedPromptTokens + cached);
                snapshot.ReasoningTokens = checked(snapshot.ReasoningTokens + reasoning);
                snapshot.Turns = checked(snapshot.Turns + 1);
                snapshot.LastSeenUtc = DateTime.UtcNow;
                if (state.AccountUsage.Count > 12) {
                    state.AccountUsage = state.AccountUsage
                        .OrderByDescending(static item => item.LastSeenUtc ?? DateTime.MinValue)
                        .Take(12)
                        .ToList();
                }
                return state;
            }, cancellationToken).ConfigureAwait(false);
        } finally {
            _saveGate.Release();
        }
    }

    private static long Positive(long? value) => value is > 0 ? value.Value : 0;
}
