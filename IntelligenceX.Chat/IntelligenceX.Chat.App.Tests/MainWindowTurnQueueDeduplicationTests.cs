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
    /// One-sided empty conversation ids should still dedupe equivalent prompts captured during startup/login transition.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsTrue_WhenOneConversationIdIsMissing() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello Mr",
            leftConversationId: string.Empty,
            rightText: " hello   mr ",
            rightConversationId: "thread-1",
            allowOneSidedMissingConversationId: true,
            startupScopeConversationId: "thread-1");

        Assert.True(equivalent);
    }

    /// <summary>
    /// One-sided empty conversation ids should not dedupe outside startup/login-gated queue operations.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsFalse_WhenOneConversationIdIsMissing_WithoutStartupScope() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello Mr",
            leftConversationId: string.Empty,
            rightText: " hello   mr ",
            rightConversationId: "thread-2");

        Assert.False(equivalent);
    }

    /// <summary>
    /// Startup/login one-sided fallback should stay scoped to the active conversation key.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsFalse_WhenOneConversationIdIsMissing_WithMismatchedStartupScope() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello Mr",
            leftConversationId: string.Empty,
            rightText: " hello   mr ",
            rightConversationId: "thread-2",
            allowOneSidedMissingConversationId: true,
            startupScopeConversationId: "thread-1");

        Assert.False(equivalent);
    }

    /// <summary>
    /// Two missing conversation ids do not provide enough scope evidence for safe deduplication.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsFalse_WhenBothConversationIdsAreMissing_EvenWithStartupScope() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello Mr",
            leftConversationId: string.Empty,
            rightText: " hello   mr ",
            rightConversationId: string.Empty,
            allowOneSidedMissingConversationId: true,
            startupScopeConversationId: "thread-1");

        Assert.False(equivalent);
    }

    /// <summary>
    /// Startup/login-gated dedupe can opt in to treat both-missing conversation ids as equivalent when text matches.
    /// </summary>
    [Fact]
    public void AreQueuedPromptsEquivalentForDispatch_ReturnsTrue_WhenBothConversationIdsMissing_AndStartupBothMissingFallbackEnabled() {
        var equivalent = MainWindow.AreQueuedPromptsEquivalentForDispatch(
            leftText: "Hello Mr",
            leftConversationId: string.Empty,
            rightText: " hello   mr ",
            rightConversationId: string.Empty,
            allowOneSidedMissingConversationId: true,
            allowBothMissingConversationIdsInStartupScope: true,
            startupScopeConversationId: string.Empty);

        Assert.True(equivalent);
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
