using IntelligenceX.Chat.App;
using IntelligenceX.Chat.Abstractions.Policy;
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

    /// <summary>
    /// Ensures opt-out sessions still emit explicit disabled proactive-mode guidance.
    /// </summary>
    [Fact]
    public void ResolveProactiveExecutionGuidanceMode_ReturnsFalseWhenProactiveModeIsDisabled() {
        var result = MainWindow.ResolveProactiveExecutionGuidanceMode(
            proactiveModeEnabled: false,
            userText: "Check AD replication health across the remaining DCs.",
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false,
            recentAssistantAskedQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures runtime self-report turns still receive runtime-mode capability self-knowledge guidance.
    /// </summary>
    [Fact]
    public void SelectCapabilitySelfKnowledgeLines_ReturnsRuntimeModeLinesForRuntimeQuestion() {
        var result = MainWindow.SelectCapabilitySelfKnowledgeLines(
            new SessionPolicyDto {
                ReadOnly = true,
                DangerousToolsEnabled = false,
                MaxToolRounds = 24,
                ParallelTools = true,
                AllowMutatingParallelToolCalls = false,
                Packs = new[] {
                    new ToolPackInfoDto { Id = "system", Name = "System", Tier = CapabilityTier.ReadOnly, Enabled = true, IsDangerous = false }
                },
                CapabilitySnapshot = new SessionCapabilitySnapshotDto {
                    RegisteredTools = 1,
                    EnabledPackCount = 1,
                    PluginCount = 0,
                    EnabledPluginCount = 0,
                    ToolingAvailable = true,
                    AllowedRootCount = 1,
                    HealthyTools = Array.Empty<string>(),
                    RemoteReachabilityMode = "local_only",
                    FamilyActions = Array.Empty<SessionRoutingFamilyActionSummaryDto>()
                }
            },
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: true);

        Assert.NotNull(result);
        Assert.Contains(result!, line => line.Contains("runtime capability handshake", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result!, line => line.Contains("invite the user's task", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Ensures broader runtime self-report questions keep the richer runtime handshake path instead of the compact one.
    /// </summary>
    [Fact]
    public void ShouldUseCompactRuntimeCapabilityContext_ReturnsFalseForBroaderRuntimeQuestion() {
        var result = MainWindow.ShouldUseCompactRuntimeCapabilityContext(
            assistantRuntimeIntrospectionQuestion: true,
            compactAssistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures genuinely compact runtime self-report questions still use the compact runtime handshake path.
    /// </summary>
    [Fact]
    public void ShouldUseCompactRuntimeCapabilityContext_ReturnsTrueForCompactRuntimeQuestion() {
        var result = MainWindow.ShouldUseCompactRuntimeCapabilityContext(
            assistantRuntimeIntrospectionQuestion: true,
            compactAssistantRuntimeIntrospectionQuestion: true);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures ordinary operational turns do not keep live profile-update scaffolding enabled.
    /// </summary>
    [Fact]
    public void ShouldIncludeLiveProfileUpdates_ReturnsFalseWhenNoProfileFieldsArePresent() {
        var result = MainWindow.ShouldIncludeLiveProfileUpdates(
            hasUserNameUpdate: false,
            hasAssistantPersonaUpdate: false,
            hasThemePresetUpdate: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures actual name/persona/theme updates still opt into the live profile-update guidance path.
    /// </summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ShouldIncludeLiveProfileUpdates_ReturnsTrueWhenAnyProfileFieldIsPresent(
        bool hasUserNameUpdate,
        bool hasAssistantPersonaUpdate,
        bool hasThemePresetUpdate) {
        var result = MainWindow.ShouldIncludeLiveProfileUpdates(
            hasUserNameUpdate,
            hasAssistantPersonaUpdate,
            hasThemePresetUpdate);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures explicit capability/runtime meta turns still use the thin request path when no onboarding
    /// or live profile update guidance is actually needed.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ShouldUseThinServiceRequestEnvelope_ReturnsTrueForMetaTurnsWithoutOnboardingOrProfileUpdates(
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion) {
        var result = MainWindow.ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext: false,
            includeLiveProfileUpdates: false,
            assistantCapabilityQuestion: assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion: assistantRuntimeIntrospectionQuestion);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures unfinished onboarding still opts into the fuller request envelope even after the thin-path cleanup.
    /// </summary>
    [Fact]
    public void ShouldUseThinServiceRequestEnvelope_ReturnsFalseWhenAmbientOnboardingContextIsIncluded() {
        var result = MainWindow.ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext: true,
            includeLiveProfileUpdates: false,
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }

    /// <summary>
    /// Ensures actual name/persona/theme updates still opt into the fuller request envelope.
    /// </summary>
    [Fact]
    public void ShouldUseThinServiceRequestEnvelope_ReturnsFalseWhenLiveProfileUpdatesAreIncluded() {
        var result = MainWindow.ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext: false,
            includeLiveProfileUpdates: true,
            assistantCapabilityQuestion: false,
            assistantRuntimeIntrospectionQuestion: false);

        Assert.False(result);
    }
}
