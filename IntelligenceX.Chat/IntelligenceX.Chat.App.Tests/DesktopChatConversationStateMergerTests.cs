using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Protects the shared conversation merge contract used by both desktop shells.
/// </summary>
public sealed class DesktopChatConversationStateMergerTests {
    /// <summary>Ensures concurrent turns survive while the newest scalar edit wins.</summary>
    [Fact]
    public void MergeConversation_CombinesConcurrentTurnsAndUsesNewestMetadata() {
        var time = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildConversation(time, "baseline");
        var local = DesktopChatConversationStateMerger.CloneConversation(baseline);
        local.Title = "Native title";
        local.ThreadId = "thread-native";
        local.Messages.Add(BuildMessage("native answer", time.AddMinutes(1)));
        local.UpdatedUtc = time.AddMinutes(1);
        var latest = DesktopChatConversationStateMerger.CloneConversation(baseline);
        latest.Title = "Legacy title";
        latest.ThreadId = "thread-legacy";
        latest.Messages.Add(BuildMessage("legacy answer", time.AddMinutes(2)));
        latest.UpdatedUtc = time.AddMinutes(2);

        var merged = DesktopChatConversationStateMerger.MergeConversation(local, baseline, latest);

        Assert.NotNull(merged);
        Assert.Equal("Legacy title", merged!.Title);
        Assert.Equal("thread-legacy", merged.ThreadId);
        Assert.Equal(
            new[] { "baseline", "native answer", "legacy answer" },
            merged.Messages.Select(static message => message.Text));
        Assert.Equal(latest.UpdatedUtc, merged.UpdatedUtc);
    }

    /// <summary>Ensures identical role/timestamp values do not collapse distinct concurrent additions.</summary>
    [Fact]
    public void MergeConversation_PreservesSameTimestampConcurrentAdditions() {
        var time = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildConversation(time);
        var messageTime = time.AddMinutes(1);
        var local = DesktopChatConversationStateMerger.CloneConversation(baseline);
        local.Messages.Add(BuildMessage("native answer", messageTime));
        local.UpdatedUtc = time.AddMinutes(1);
        var latest = DesktopChatConversationStateMerger.CloneConversation(baseline);
        latest.Messages.Add(BuildMessage("legacy answer", messageTime));
        latest.UpdatedUtc = time.AddMinutes(2);

        var merged = DesktopChatConversationStateMerger.MergeConversation(local, baseline, latest);

        Assert.NotNull(merged);
        Assert.Equal(2, merged!.Messages.Count);
        Assert.Contains(merged.Messages, static message => message.Text == "native answer");
        Assert.Contains(merged.Messages, static message => message.Text == "legacy answer");
    }

    /// <summary>Ensures resetting the final conversation clears its persisted transcript.</summary>
    [Fact]
    public void MergeConversation_HonorsTranscriptResetWhenOtherWriterIsUnchanged() {
        var time = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildConversation(time, "first", "second");
        var local = DesktopChatConversationStateMerger.CloneConversation(baseline);
        local.Messages.Clear();
        local.Title = ChatConversationIdentity.DefaultTitle;
        local.ThreadId = null;
        local.UpdatedUtc = time.AddMinutes(1);
        var latest = DesktopChatConversationStateMerger.CloneConversation(baseline);

        var merged = DesktopChatConversationStateMerger.MergeConversation(local, baseline, latest);

        Assert.NotNull(merged);
        Assert.Equal(ChatConversationIdentity.DefaultTitle, merged!.Title);
        Assert.Null(merged.ThreadId);
        Assert.Empty(merged.Messages);
    }

    /// <summary>Ensures a streamed assistant update replaces the partial persisted bubble.</summary>
    [Fact]
    public void MergeConversation_ReplacesPartialAssistantTextAtStableTimestamp() {
        var time = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildConversation(time, "partial answer");
        baseline.Messages[0].Status = "Streaming";
        var local = DesktopChatConversationStateMerger.CloneConversation(baseline);
        local.Messages[0].Text = "final answer";
        local.Messages[0].Status = "Complete";
        local.UpdatedUtc = time.AddMinutes(1);
        var latest = DesktopChatConversationStateMerger.CloneConversation(baseline);

        var merged = DesktopChatConversationStateMerger.MergeConversation(local, baseline, latest);

        Assert.NotNull(merged);
        var message = Assert.Single(merged!.Messages);
        Assert.Equal("final answer", message.Text);
        Assert.Equal("Complete", message.Status);
    }

    /// <summary>Ensures consuming one action does not discard a concurrently-created action.</summary>
    [Fact]
    public void MergeConversation_ConsumesBaselineActionAndKeepsConcurrentAddition() {
        var time = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildConversation(time, "baseline");
        baseline.PendingActions.Add(BuildAction("confirm-old"));
        var local = DesktopChatConversationStateMerger.CloneConversation(baseline);
        local.PendingActions.Clear();
        local.UpdatedUtc = time.AddMinutes(1);
        var latest = DesktopChatConversationStateMerger.CloneConversation(baseline);
        latest.PendingActions.Add(BuildAction("confirm-new"));
        latest.UpdatedUtc = time.AddMinutes(2);

        var merged = DesktopChatConversationStateMerger.MergeConversation(local, baseline, latest);

        Assert.NotNull(merged);
        Assert.Equal("confirm-new", Assert.Single(merged!.PendingActions).Id);
    }

    /// <summary>Ensures conversation deletion wins only when the other writer left it unchanged.</summary>
    [Fact]
    public void MergeConversations_PreservesConcurrentEditButHonorsUncontestedDeletion() {
        var time = new DateTime(2026, 7, 18, 8, 0, 0, DateTimeKind.Utc);
        var baseline = BuildConversation(time, "baseline");
        var changed = DesktopChatConversationStateMerger.CloneConversation(baseline);
        changed.Messages.Add(BuildMessage("concurrent answer", time.AddMinutes(1)));
        changed.UpdatedUtc = time.AddMinutes(1);

        var preserved = DesktopChatConversationStateMerger.MergeConversations(
            Array.Empty<ChatConversationState>(),
            new[] { baseline },
            new[] { changed });
        var deleted = DesktopChatConversationStateMerger.MergeConversations(
            Array.Empty<ChatConversationState>(),
            new[] { baseline },
            new[] { DesktopChatConversationStateMerger.CloneConversation(baseline) });

        Assert.Equal("concurrent answer", Assert.Single(preserved).Messages[^1].Text);
        Assert.Empty(deleted);
    }

    private static ChatConversationState BuildConversation(DateTime timeUtc, params string[] messages) =>
        new() {
            Id = "chat-shared",
            Title = "Shared chat",
            ThreadId = "thread-baseline",
            Messages = messages
                .Select((text, index) => BuildMessage(text, timeUtc.AddSeconds(index)))
                .ToList(),
            UpdatedUtc = timeUtc
        };

    private static ChatMessageState BuildMessage(string text, DateTime timeUtc) =>
        new() {
            Role = "assistant",
            Text = text,
            TimeUtc = timeUtc,
            Status = "Complete"
        };

    private static ChatPendingActionState BuildAction(string id) =>
        new() {
            Id = id,
            Title = "Confirm",
            Request = "Confirm the action",
            Reply = "Proceed"
        };
}
