using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Regression tests for profile persistence normalization and onboarding-completion eligibility.
/// </summary>
public sealed class MainWindowProfileStateNormalizationTests {
    /// <summary>
    /// Ensures onboarding completion with profile fields promotes an otherwise-unspecified scope to persistent profile scope.
    /// </summary>
    [Fact]
    public void ResolveEffectiveProfileUpdateScope_PromotesCompletionPayloadToProfileScope() {
        var update = new OnboardingProfileUpdate {
            Scope = ProfileUpdateScope.Unspecified,
            HasUserName = true,
            UserName = "Przemek",
            HasOnboardingCompleted = true,
            OnboardingCompleted = true
        };

        var scope = MainWindow.ResolveEffectiveProfileUpdateScope(update);

        Assert.Equal(ProfileUpdateScope.Profile, scope);
    }

    /// <summary>
    /// Ensures ordinary unspecified updates still stay session-scoped when they do not complete onboarding.
    /// </summary>
    [Fact]
    public void ResolveEffectiveProfileUpdateScope_LeavesOrdinaryUnspecifiedUpdatesSessionScoped() {
        var update = new OnboardingProfileUpdate {
            Scope = ProfileUpdateScope.Unspecified,
            HasAssistantPersona = true,
            AssistantPersona = "focused helper"
        };

        var scope = MainWindow.ResolveEffectiveProfileUpdateScope(update);

        Assert.Equal(ProfileUpdateScope.Session, scope);
    }

    /// <summary>
    /// Ensures onboarding still treats the default theme as missing before completion is finalized.
    /// </summary>
    [Fact]
    public void BuildMissingOnboardingFields_TreatsDefaultThemeAsMissingBeforeCompletion() {
        var missing = MainWindow.BuildMissingOnboardingFields(
            effectiveUserName: "Przemek",
            effectiveAssistantPersona: "concise analyst",
            effectiveThemePreset: "default",
            onboardingCompleted: false);

        Assert.Contains("themePreset", missing);
    }

    /// <summary>
    /// Ensures already-completed profiles do not get downgraded merely because they still use the default theme.
    /// </summary>
    [Fact]
    public void BuildMissingOnboardingFields_AllowsDefaultThemeAfterCompletion() {
        var missing = MainWindow.BuildMissingOnboardingFields(
            effectiveUserName: "Przemek",
            effectiveAssistantPersona: "concise analyst",
            effectiveThemePreset: "default",
            onboardingCompleted: true);

        Assert.DoesNotContain("themePreset", missing);
    }
}
