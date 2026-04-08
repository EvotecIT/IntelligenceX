using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for queued prompt recovery decisions after post-login verification failures.
/// </summary>
public sealed class MainWindowPostLoginQueueRecoveryTests {
    /// <summary>
    /// Ensures queued prompts are retried when sign-in is required, at least one prompt is queued, and login is idle.
    /// </summary>
    [Fact]
    public void ShouldAttemptQueuedPromptDispatchAfterVerificationFailure_ReturnsTrue_WhenQueuedPromptExistsAndLoginIsIdle() {
        var shouldDispatch = MainWindow.ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
            requiresInteractiveSignIn: true,
            queuedPromptCount: 1,
            loginInProgress: false);

        Assert.True(shouldDispatch);
    }

    /// <summary>
    /// Ensures retry is skipped when no queued prompt exists.
    /// </summary>
    [Fact]
    public void ShouldAttemptQueuedPromptDispatchAfterVerificationFailure_ReturnsFalse_WhenNoQueuedPromptExists() {
        var shouldDispatch = MainWindow.ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
            requiresInteractiveSignIn: true,
            queuedPromptCount: 0,
            loginInProgress: false);

        Assert.False(shouldDispatch);
    }

    /// <summary>
    /// Ensures retry is skipped while login is still in progress.
    /// </summary>
    [Fact]
    public void ShouldAttemptQueuedPromptDispatchAfterVerificationFailure_ReturnsFalse_WhenLoginIsStillInProgress() {
        var shouldDispatch = MainWindow.ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
            requiresInteractiveSignIn: true,
            queuedPromptCount: 2,
            loginInProgress: true);

        Assert.False(shouldDispatch);
    }

    /// <summary>
    /// Ensures post-login completion stays in the queued-prompt usage-limit state instead of falling through into a misleading kickoff.
    /// </summary>
    [Fact]
    public void ShouldKeepQueuedPromptUsageLimitStateAfterLogin_ReturnsTrue_WhenQueuedPromptIsStillBlocked() {
        var shouldKeepBlockedState = MainWindow.ShouldKeepQueuedPromptUsageLimitStateAfterLogin(
            dispatched: false,
            queuedPromptCount: 1,
            queueAutoDispatchEnabled: true,
            usageLimitStillBlocked: true);

        Assert.True(shouldKeepBlockedState);
    }

    /// <summary>
    /// Ensures the usage-limit hold is skipped once there is no queued retry left to protect.
    /// </summary>
    [Fact]
    public void ShouldKeepQueuedPromptUsageLimitStateAfterLogin_ReturnsFalse_WhenNoQueuedPromptRemains() {
        var shouldKeepBlockedState = MainWindow.ShouldKeepQueuedPromptUsageLimitStateAfterLogin(
            dispatched: false,
            queuedPromptCount: 0,
            queueAutoDispatchEnabled: true,
            usageLimitStillBlocked: true);

        Assert.False(shouldKeepBlockedState);
    }

    /// <summary>
    /// Ensures final assistant responses replace interim bubbles by default instead of appending a second final bubble.
    /// </summary>
    [Fact]
    public void ShouldRenderFinalAssistantAsSeparateBubbleAfterInterim_ReturnsFalse() {
        Assert.False(MainWindow.ShouldRenderFinalAssistantAsSeparateBubbleAfterInterim());
    }
}
