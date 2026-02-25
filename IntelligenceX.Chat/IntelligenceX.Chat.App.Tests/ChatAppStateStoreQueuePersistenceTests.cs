using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies queued-turn state survives persistence and remains bounded.
/// </summary>
public sealed class ChatAppStateStoreQueuePersistenceTests {
    /// <summary>
    /// Ensures queued prompt collections round-trip through the app state store with retention limits.
    /// </summary>
    [Fact]
    public async Task UpsertAndGet_PreservesQueuedTurnState_WithBoundedRetention() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dbPath = Path.Combine(tempRoot, "state.db");

        try {
            using var store = new ChatAppStateStore(dbPath);
            var state = new ChatAppState {
                ProfileName = "default",
                PendingTurns = BuildQueuedTurns(count: 40, prefix: "pending"),
                QueuedTurnsAfterLogin = BuildQueuedTurns(count: 40, prefix: "signin")
            };

            await store.UpsertAsync("default", state, CancellationToken.None);
            var loaded = await store.GetAsync("default", CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.NotNull(loaded!.PendingTurns);
            Assert.NotNull(loaded.QueuedTurnsAfterLogin);
            Assert.True(loaded.PendingTurns.Count <= 24);
            Assert.True(loaded.QueuedTurnsAfterLogin.Count <= 24);
            Assert.All(loaded.PendingTurns, item => Assert.False(string.IsNullOrWhiteSpace(item.Text)));
            Assert.All(loaded.QueuedTurnsAfterLogin, item => Assert.False(string.IsNullOrWhiteSpace(item.Text)));
        } finally {
            try {
                Directory.Delete(tempRoot, recursive: true);
            } catch {
                // Best-effort cleanup.
            }
        }
    }

    private static List<ChatQueuedTurnState> BuildQueuedTurns(int count, string prefix) {
        var turns = new List<ChatQueuedTurnState>(count);
        var now = DateTime.UtcNow;
        for (var i = 0; i < count; i++) {
            turns.Add(new ChatQueuedTurnState {
                Text = prefix + "-turn-" + i,
                ConversationId = "chat-" + i,
                EnqueuedUtc = now.AddSeconds(i),
                SkipUserBubbleOnDispatch = i % 2 == 0
            });
        }

        return turns;
    }
}
