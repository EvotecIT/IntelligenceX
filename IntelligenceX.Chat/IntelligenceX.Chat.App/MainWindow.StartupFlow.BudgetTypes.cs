using System;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {

    private bool AnyConversationHasMessages() {
        foreach (var conversation in _conversations) {
            if (conversation.Messages.Count > 0) {
                return true;
            }
        }

        return false;
    }

    private sealed record StartupWebViewBudgetCacheEntry(
        int? LastEnsureWebViewMs,
        int ConsecutiveBudgetExhaustions,
        int ConsecutiveStableCompletions,
        int AdaptiveCooldownRunsRemaining,
        int? LastAppliedBudgetMs,
        DateTime? UpdatedUtc) {
        public static StartupWebViewBudgetCacheEntry Default { get; } = new(
            LastEnsureWebViewMs: null,
            ConsecutiveBudgetExhaustions: 0,
            ConsecutiveStableCompletions: 0,
            AdaptiveCooldownRunsRemaining: 0,
            LastAppliedBudgetMs: null,
            UpdatedUtc: null);
    }

    private sealed class StartupWebViewBudgetCachePayload {
        public int? LastEnsureWebViewMs { get; set; }
        public int ConsecutiveBudgetExhaustions { get; set; }
        public int ConsecutiveStableCompletions { get; set; }
        public int AdaptiveCooldownRunsRemaining { get; set; }
        public int? LastAppliedBudgetMs { get; set; }
        public string? UpdatedUtc { get; set; }
    }

    private sealed record StartupWebViewBudgetDecision(int BudgetMs, string Reason);
}
