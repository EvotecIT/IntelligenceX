using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for app-side prompt context gating on ordinary conversational turns.
/// </summary>
public sealed class MainWindowPromptContextGatingTests {
    /// <summary>
    /// Ensures unfinished onboarding does not keep riding along on concrete task turns.
    /// </summary>
    [Fact]
    public void ShouldIncludeAmbientOnboardingContext_ReturnsFalseForConcreteTaskTurn() {
        var result = MainWindow.ShouldIncludeAmbientOnboardingContext(
            userText: "Check AD replication health across all DCs.",
            onboardingInProgress: true,
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures light openers can still carry onboarding context when setup is unfinished.
    /// </summary>
    [Fact]
    public void ShouldIncludeAmbientOnboardingContext_ReturnsTrueForLightOpener() {
        var result = MainWindow.ShouldIncludeAmbientOnboardingContext(
            userText: "Hello",
            onboardingInProgress: true,
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures assistant-capability questions do not reactivate ambient onboarding context.
    /// </summary>
    [Fact]
    public void ShouldIncludeAmbientOnboardingContext_ReturnsFalseForCapabilityQuestion() {
        var result = MainWindow.ShouldIncludeAmbientOnboardingContext(
            userText: "What can you do for me today?",
            onboardingInProgress: true,
            assistantCapabilityQuestion: true,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures assistant-capability questions do not turn proactive execution hints back on.
    /// </summary>
    [Fact]
    public void ShouldIncludeProactiveExecutionMode_ReturnsFalseForCapabilityQuestion() {
        var result = MainWindow.ShouldIncludeProactiveExecutionMode(
            userText: "What can you do for me today?",
            assistantCapabilityQuestion: true,
            assistantRuntimeIntrospectionQuestion: false,
            recentAssistantAskedQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures runtime self-report questions do not trigger proactive execution hints.
    /// </summary>
    [Fact]
    public void ShouldIncludeProactiveExecutionMode_ReturnsFalseForRuntimeIntrospectionQuestion() {
        var result = MainWindow.ShouldIncludeProactiveExecutionMode(
            userText: "What model and tools are you using right now?",
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: true,
            recentAssistantAskedQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures concrete imperative tasks can still carry proactive execution guidance when enabled.
    /// </summary>
    [Fact]
    public void ShouldIncludeProactiveExecutionMode_ReturnsTrueForConcreteImperativeTask() {
        var result = MainWindow.ShouldIncludeProactiveExecutionMode(
            userText: "Check AD replication health across the remaining DCs.",
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false,
            recentAssistantAskedQuestion: false);

        Assert.True(result);
    }
}
