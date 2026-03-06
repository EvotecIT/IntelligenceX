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

    /// <summary>
    /// Ensures pending conversation actions survive persistence and remain bounded per conversation.
    /// </summary>
    [Fact]
    public async Task UpsertAndGet_PreservesConversationPendingActions_WithBoundedRetention() {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ix-chat-state-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var dbPath = Path.Combine(tempRoot, "state.db");

        try {
            using var store = new ChatAppStateStore(dbPath);
            var state = new ChatAppState {
                ProfileName = "default",
                Conversations = new List<ChatConversationState> {
                    new() {
                        Id = "chat-1",
                        Title = "Check domain",
                        PendingAssistantQuestionHint = "Do you mean internal AD health or the public DNS/mail side?",
                        PendingActions = BuildPendingActions(count: 12)
                    }
                }
            };

            await store.UpsertAsync("default", state, CancellationToken.None);
            var loaded = await store.GetAsync("default", CancellationToken.None);

            var conversation = Assert.Single(loaded!.Conversations);
            Assert.NotNull(conversation.PendingActions);
            Assert.Equal(6, conversation.PendingActions.Count);
            Assert.Equal("Do you mean internal AD health or the public DNS/mail side?", conversation.PendingAssistantQuestionHint);
            Assert.All(conversation.PendingActions, item => Assert.False(string.IsNullOrWhiteSpace(item.Id)));
            Assert.All(conversation.PendingActions, item => Assert.False(string.IsNullOrWhiteSpace(item.Reply)));
            Assert.Collection(
                conversation.PendingActions,
                item => Assert.Equal("act_6", item.Id),
                item => Assert.Equal("act_7", item.Id),
                item => Assert.Equal("act_8", item.Id),
                item => Assert.Equal("act_9", item.Id),
                item => Assert.Equal("act_10", item.Id),
                item => Assert.Equal("act_11", item.Id));
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

    private static List<ChatPendingActionState> BuildPendingActions(int count) {
        var actions = new List<ChatPendingActionState>(count);
        for (var i = 0; i < count; i++) {
            actions.Add(new ChatPendingActionState {
                Id = "act_" + i,
                Title = "Action " + i,
                Request = "Do action " + i,
                Reply = "/act act_" + i
            });
        }

        return actions;
    }
}
