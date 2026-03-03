using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies queued-after-login prompt deduplication comparisons.
/// </summary>
public sealed class MainWindowTurnQueueDeduplicationTests {
    /// <summary>
    /// Equivalent prompts should match when only casing or whitespace differs.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsTrue_ForCaseAndWhitespaceEquivalentText() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello   Mr",
            leftConversationId: "thread-1",
            rightText: " hello mr ",
            rightConversationId: "THREAD-1");

        Assert.True(equivalent);
    }

    /// <summary>
    /// Conversation scope remains part of deduplication matching.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsFalse_ForDifferentConversation() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello Mr",
            leftConversationId: "thread-1",
            rightText: "Hello Mr",
            rightConversationId: "thread-2");

        Assert.False(equivalent);
    }

    /// <summary>
    /// Empty prompt text should never be treated as a deduplication match.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsFalse_WhenTextIsMissing() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: " ",
            leftConversationId: "thread-1",
            rightText: "Hello Mr",
            rightConversationId: "thread-1");

        Assert.False(equivalent);
    }
}
