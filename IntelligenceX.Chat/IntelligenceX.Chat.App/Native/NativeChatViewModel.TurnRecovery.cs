using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatViewModel {
    private async Task TryRecordAccountUsageAsync(TokenUsageDto? usage) {
        if (usage is null || _conversationStore is not INativeAccountUsageStore accountUsageStore) return;
        try {
            await accountUsageStore.RecordUsageAsync(AuthenticatedAccountId, usage, CancellationToken.None)
                .ConfigureAwait(false);
        } catch (Exception ex) {
            StartupLog.Write("Native account usage could not be saved; the completed turn is retained: " + ex);
        }
    }

    private async Task<bool> QueueTurnAfterSignInAsync(string text, string conversationId) {
        var turn = new NativeQueuedTurn(text, conversationId, DateTime.UtcNow,
            SkipUserBubbleOnDispatch: true, Source: NativeQueuedTurnSource.AfterLogin);
        try {
            if (_conversationStore is INativeQueuedTurnStore queuedStore) {
                if (!await queuedStore.EnqueueAfterLoginAsync(turn, CancellationToken.None).ConfigureAwait(false))
                    return false;
            } else if (QueuedTurns.Count >= ChatQueueContract.MaxTurns) {
                return false;
            }

            RunOnUi(() => QueuedTurns.Add(turn));
            return true;
        } catch (Exception ex) {
            StartupLog.Write("Native turn could not be queued after sign-in failure: " + ex);
            return false;
        }
    }
}
