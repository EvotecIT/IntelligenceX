using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards shared profile normalization, cross-shell state merging, and native theme projection.
/// </summary>
public sealed class DesktopChatSharedStateTests {
    /// <summary>Ensures a stale legacy save preserves newer native profile, memory, and transcript state.</summary>
    [Fact]
    public async Task LegacyMerge_PreservesNativeChangesMadeAfterLegacyLoad() {
        var directory = Path.Combine(Path.GetTempPath(), "ix-shared-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var path = Path.Combine(directory, "app-state.db");
            var initialTime = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
            using var store = new ChatAppStateStore(path);
            await store.UpsertAsync(
                "default",
                BuildState("Original", "Original memory", "Initial message", initialTime),
                CancellationToken.None);
            var staleLegacy = Assert.IsType<ChatAppState>(await store.GetAsync("default", CancellationToken.None));
            var baseline = store.CloneState(staleLegacy);

            _ = await store.UpdateAsync(
                "default",
                latest => {
                    var state = Assert.IsType<ChatAppState>(latest);
                    state.UserName = "Native name";
                    state.MemoryFacts.Add(new ChatMemoryFactState {
                        Id = "native-memory",
                        Fact = "Native memory",
                        UpdatedUtc = initialTime.AddMinutes(1)
                    });
                    var conversation = Assert.Single(state.Conversations);
                    conversation.Messages.Add(new ChatMessageState {
                        Role = "assistant",
                        Text = "Native answer",
                        TimeUtc = initialTime.AddMinutes(1)
                    });
                    conversation.UpdatedUtc = initialTime.AddMinutes(1);
                    return state;
                },
                CancellationToken.None);

            _ = await store.UpdateAsync(
                "default",
                latest => DesktopChatStateMerger.MergeLegacySnapshot(staleLegacy, baseline, latest),
                CancellationToken.None);
            var saved = Assert.IsType<ChatAppState>(await store.GetAsync("default", CancellationToken.None));

            Assert.Equal("Native name", saved.UserName);
            Assert.Contains(saved.MemoryFacts, fact => fact.Fact == "Native memory");
            Assert.Contains(Assert.Single(saved.Conversations).Messages, message => message.Text == "Native answer");
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Ensures concurrent message additions from both shells are retained.</summary>
    [Fact]
    public void LegacyMerge_CombinesConcurrentMessageAdditions() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildState("Operator", "Memory", "Initial message", time);
        var local = CloneForTest(baseline);
        var latest = CloneForTest(baseline);
        local.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "user",
            Text = "Legacy addition",
            TimeUtc = time.AddMinutes(1)
        });
        latest.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "assistant",
            Text = "Native addition",
            TimeUtc = time.AddMinutes(2)
        });

        var merged = DesktopChatStateMerger.MergeLegacySnapshot(local, baseline, latest);
        var messages = Assert.Single(merged.Conversations).Messages;

        Assert.Contains(messages, message => message.Text == "Legacy addition");
        Assert.Contains(messages, message => message.Text == "Native addition");
    }

    /// <summary>Ensures advancing the baseline to the reconciled snapshot allows a later legacy deletion.</summary>
    [Fact]
    public void LegacyMerge_ReconciledBaselineDoesNotResurrectDeletedState() {
        var time = new DateTime(2026, 7, 17, 8, 0, 0, DateTimeKind.Utc);
        var original = BuildState("Operator", "Original memory", "Initial message", time);
        var latest = CloneForTest(original);
        latest.MemoryFacts.Add(new ChatMemoryFactState {
            Id = "native-memory",
            Fact = "Native memory",
            UpdatedUtc = time.AddMinutes(1)
        });
        latest.Conversations[0].Messages.Add(new ChatMessageState {
            Role = "assistant",
            Text = "Native answer",
            TimeUtc = time.AddMinutes(1)
        });
        latest.Conversations[0].UpdatedUtc = time.AddMinutes(1);

        var reconciled = DesktopChatStateMerger.MergeLegacySnapshot(CloneForTest(original), original, latest);
        var afterDeletion = CloneForTest(reconciled);
        afterDeletion.MemoryFacts.Clear();
        afterDeletion.Conversations.Clear();
        afterDeletion.Messages.Clear();
        afterDeletion.ActiveConversationId = null;

        var saved = DesktopChatStateMerger.MergeLegacySnapshot(afterDeletion, reconciled, CloneForTest(reconciled));

        Assert.Empty(saved.MemoryFacts);
        Assert.Empty(saved.Conversations);
    }

    /// <summary>Ensures structured profile values normalize identically regardless of desktop shell.</summary>
    [Fact]
    public void ProfileNormalizer_UsesOneDeterministicContract() {
        var longName = new string('N', 60);
        var longPersona = new string('P', 200);

        Assert.Equal(new string('N', 48), DesktopChatProfileNormalizer.NormalizeUserName(longName));
        Assert.Equal(new string('P', 180), DesktopChatProfileNormalizer.NormalizeAssistantPersona(longPersona));
        Assert.Null(DesktopChatProfileNormalizer.NormalizeUserName("skip"));
        Assert.Equal("analyst", DesktopChatProfileNormalizer.NormalizeAssistantPersona("analyst"));
    }

    private static ChatAppState BuildState(string userName, string memory, string message, DateTime timeUtc) {
        var conversation = new ChatConversationState {
            Id = "chat-one",
            Title = "Chat",
            Messages = new List<ChatMessageState> {
                new() { Role = "user", Text = message, TimeUtc = timeUtc }
            },
            UpdatedUtc = timeUtc
        };
        return new ChatAppState {
            UserName = userName,
            ActiveConversationId = conversation.Id,
            Conversations = new List<ChatConversationState> { conversation },
            Messages = conversation.Messages.Select(messageState => new ChatMessageState {
                Role = messageState.Role,
                Text = messageState.Text,
                TimeUtc = messageState.TimeUtc
            }).ToList(),
            MemoryFacts = new List<ChatMemoryFactState> {
                new() { Id = "memory-one", Fact = memory, UpdatedUtc = timeUtc }
            }
        };
    }

    private static ChatAppState CloneForTest(ChatAppState state) {
        var conversation = Assert.Single(state.Conversations);
        var clonedConversation = new ChatConversationState {
            Id = conversation.Id,
            Title = conversation.Title,
            ThreadId = conversation.ThreadId,
            Messages = conversation.Messages.Select(message => new ChatMessageState {
                Role = message.Role,
                Text = message.Text,
                TimeUtc = message.TimeUtc,
                Model = message.Model,
                Status = message.Status
            }).ToList(),
            UpdatedUtc = conversation.UpdatedUtc
        };
        return new ChatAppState {
            UserName = state.UserName,
            AssistantPersona = state.AssistantPersona,
            ThemePreset = state.ThemePreset,
            ActiveConversationId = state.ActiveConversationId,
            Conversations = new List<ChatConversationState> { clonedConversation },
            Messages = clonedConversation.Messages.ToList(),
            MemoryFacts = state.MemoryFacts.Select(fact => new ChatMemoryFactState {
                Id = fact.Id,
                Fact = fact.Fact,
                Weight = fact.Weight,
                Tags = fact.Tags.ToArray(),
                UpdatedUtc = fact.UpdatedUtc
            }).ToList()
        };
    }
}
