using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

public sealed class MainWindowPostLoginQueueRecoveryTests {
    [Fact]
    public void ShouldAttemptQueuedPromptDispatchAfterVerificationFailure_ReturnsTrue_WhenQueuedPromptExistsAndLoginIsIdle() {
        var shouldDispatch = MainWindow.ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
            requiresInteractiveSignIn: true,
            queuedPromptCount: 1,
            loginInProgress: false);

        Assert.True(shouldDispatch);
    }

    [Fact]
    public void ShouldAttemptQueuedPromptDispatchAfterVerificationFailure_ReturnsFalse_WhenNoQueuedPromptExists() {
        var shouldDispatch = MainWindow.ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
            requiresInteractiveSignIn: true,
            queuedPromptCount: 0,
            loginInProgress: false);

        Assert.False(shouldDispatch);
    }

    [Fact]
    public void ShouldAttemptQueuedPromptDispatchAfterVerificationFailure_ReturnsFalse_WhenLoginIsStillInProgress() {
        var shouldDispatch = MainWindow.ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
            requiresInteractiveSignIn: true,
            queuedPromptCount: 2,
            loginInProgress: true);

        Assert.False(shouldDispatch);
    }

    [Fact]
    public void ShouldRenderFinalAssistantAsSeparateBubbleAfterInterim_ReturnsFalse() {
        Assert.False(MainWindow.ShouldRenderFinalAssistantAsSeparateBubbleAfterInterim());
    }
}
