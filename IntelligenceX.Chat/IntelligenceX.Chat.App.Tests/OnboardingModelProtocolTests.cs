using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for model onboarding metadata protocol extraction.
/// </summary>
public sealed class OnboardingModelProtocolTests {
    /// <summary>
    /// Ensures a valid ix_profile envelope is parsed and removed from visible assistant text.
    /// </summary>
    [Fact]
    public void TryExtractLastProfileUpdate_ParsesEnvelopeAndStripsBlock() {
        const string text = """
                            Great, I'll use that style.
                            ```ix_profile
                            {"userName":"Przemek","assistantPersona":"concise ops","themePreset":"emerald","onboardingComplete":true}
                            ```
                            """;

        var ok = OnboardingModelProtocol.TryExtractLastProfileUpdate(text, out var update, out var cleaned);

        Assert.True(ok);
        Assert.Equal("Great, I'll use that style.", cleaned);
        Assert.True(update.HasUserName);
        Assert.Equal("Przemek", update.UserName);
        Assert.True(update.HasAssistantPersona);
        Assert.Equal("concise ops", update.AssistantPersona);
        Assert.True(update.HasThemePreset);
        Assert.Equal("emerald", update.ThemePreset);
        Assert.True(update.HasOnboardingCompleted);
        Assert.True(update.OnboardingCompleted);
    }

    /// <summary>
    /// Ensures assistant text without metadata envelope does not produce profile updates.
    /// </summary>
    [Fact]
    public void TryExtractLastProfileUpdate_ReturnsFalseWithoutEnvelope() {
        const string text = "Hello there.";

        var ok = OnboardingModelProtocol.TryExtractLastProfileUpdate(text, out var update, out var cleaned);

        Assert.False(ok);
        Assert.Equal("Hello there.", cleaned);
        Assert.False(update.HasUserName);
        Assert.False(update.HasAssistantPersona);
        Assert.False(update.HasThemePreset);
        Assert.False(update.HasOnboardingCompleted);
    }

    /// <summary>
    /// Ensures unknown scope values are treated as unspecified instead of persisted profile scope.
    /// </summary>
    [Fact]
    public void TryExtractLastProfileUpdate_UnknownScopeFallsBackToUnspecified() {
        const string text = """
                            ```ix_profile
                            {"scope":"sessie","assistantPersona":"focused helper"}
                            ```
                            """;

        var ok = OnboardingModelProtocol.TryExtractLastProfileUpdate(text, out var update, out _);

        Assert.True(ok);
        Assert.True(update.HasAssistantPersona);
        Assert.Equal(ProfileUpdateScope.Unspecified, update.Scope);
    }

    /// <summary>
    /// Ensures missing scope keeps the update scope unspecified until an explicit scope is provided.
    /// </summary>
    [Fact]
    public void TryExtractLastProfileUpdate_MissingScopeRemainsUnspecified() {
        const string text = """
                            ```ix_profile
                            {"assistantPersona":"focused helper"}
                            ```
                            """;

        var ok = OnboardingModelProtocol.TryExtractLastProfileUpdate(text, out var update, out _);

        Assert.True(ok);
        Assert.True(update.HasAssistantPersona);
        Assert.Equal(ProfileUpdateScope.Unspecified, update.Scope);
    }
}
