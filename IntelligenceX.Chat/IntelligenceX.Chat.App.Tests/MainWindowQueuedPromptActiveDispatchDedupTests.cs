using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies in-flight deduplication when queued-after-login dispatch races with manual resend.
/// </summary>
public sealed class MainWindowQueuedPromptActiveDispatchDedupTests {
    /// <summary>
    /// Equivalent manual resend should be suppressed while the same queued-after-login prompt is already in-flight.
    /// </summary>
    [Fact]
    public void ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch_ReturnsTrue_ForEquivalentPrompt() {
        var suppress = MainWindow.ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
            incomingText: "  Hello   Mr  ",
            incomingConversationId: "thread-1",
            incomingIsQueuedDispatch: false,
            incomingSkipUserBubble: false,
            activeDispatchFromQueuedAfterLogin: true,
            activeDispatchText: "hello mr",
            activeDispatchConversationId: "THREAD-1",
            startupScopeConversationId: "thread-1");

        Assert.True(suppress);
    }

    /// <summary>
    /// Suppression should not apply when the active dispatch was not sourced from the queued-after-login path.
    /// </summary>
    [Fact]
    public void ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch_ReturnsFalse_WhenActiveDispatchNotQueuedAfterLogin() {
        var suppress = MainWindow.ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
            incomingText: "Hello Mr",
            incomingConversationId: "thread-1",
            incomingIsQueuedDispatch: false,
            incomingSkipUserBubble: false,
            activeDispatchFromQueuedAfterLogin: false,
            activeDispatchText: "Hello Mr",
            activeDispatchConversationId: "thread-1",
            startupScopeConversationId: "thread-1");

        Assert.False(suppress);
    }

    /// <summary>
    /// Suppression should not apply for queued/system dispatches; it is only for manual resends.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch_ReturnsFalse_ForNonManualIncomingDispatch(bool incomingIsQueuedDispatch, bool incomingSkipUserBubble) {
        var suppress = MainWindow.ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
            incomingText: "Hello Mr",
            incomingConversationId: "thread-1",
            incomingIsQueuedDispatch: incomingIsQueuedDispatch,
            incomingSkipUserBubble: incomingSkipUserBubble,
            activeDispatchFromQueuedAfterLogin: true,
            activeDispatchText: "Hello Mr",
            activeDispatchConversationId: "thread-1",
            startupScopeConversationId: "thread-1");

        Assert.False(suppress);
    }

    /// <summary>
    /// One-sided missing conversation ids should still suppress duplicates when startup scope matches.
    /// </summary>
    [Fact]
    public void ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch_ReturnsTrue_ForOneSidedMissingConversationIdWithinScope() {
        var suppress = MainWindow.ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
            incomingText: "Hello Mr",
            incomingConversationId: string.Empty,
            incomingIsQueuedDispatch: false,
            incomingSkipUserBubble: false,
            activeDispatchFromQueuedAfterLogin: true,
            activeDispatchText: " hello   mr ",
            activeDispatchConversationId: "thread-1",
            startupScopeConversationId: "thread-1");

        Assert.True(suppress);
    }

    /// <summary>
    /// Startup/login fallback should also suppress when both conversation ids are missing but prompt text is equivalent.
    /// </summary>
    [Fact]
    public void ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch_ReturnsTrue_ForBothMissingConversationIds() {
        var suppress = MainWindow.ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
            incomingText: "Hello Mr",
            incomingConversationId: string.Empty,
            incomingIsQueuedDispatch: false,
            incomingSkipUserBubble: false,
            activeDispatchFromQueuedAfterLogin: true,
            activeDispatchText: " hello   mr ",
            activeDispatchConversationId: string.Empty,
            startupScopeConversationId: string.Empty);

        Assert.True(suppress);
    }
}
