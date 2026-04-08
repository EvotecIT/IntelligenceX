using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Covers conversation ordering timestamps used by the sidebar and persisted state.
/// </summary>
public sealed class MainWindowConversationOrderingTests {
    /// <summary>
    /// Ensures actual last-message activity wins over later metadata edits for display ordering.
    /// </summary>
    [Fact]
    public void ResolveConversationDisplayUpdatedUtc_PrefersLastMessageTimeWhenPresent() {
        var lastMessageLocal = new DateTime(2026, 4, 7, 21, 15, 0, DateTimeKind.Local);
        var metadataUpdatedUtc = new DateTime(2026, 4, 7, 22, 45, 0, DateTimeKind.Utc);

        var actual = MainWindow.ResolveConversationDisplayUpdatedUtc(metadataUpdatedUtc, lastMessageLocal);

        Assert.Equal(lastMessageLocal.ToUniversalTime(), actual);
    }

    /// <summary>
    /// Ensures empty conversations still fall back to their explicit updated timestamp.
    /// </summary>
    [Fact]
    public void ResolveConversationDisplayUpdatedUtc_UsesExplicitTimestampWhenNoMessagesExist() {
        var metadataUpdatedUtc = new DateTime(2026, 4, 7, 22, 45, 0, DateTimeKind.Utc);

        var actual = MainWindow.ResolveConversationDisplayUpdatedUtc(metadataUpdatedUtc, lastMessageTimeLocal: null);

        Assert.Equal(metadataUpdatedUtc, actual);
    }
}
