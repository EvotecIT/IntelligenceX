using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards bridge runtime state mapping and compatible-http usage identity scoping.
/// </summary>
public sealed class MainWindowBridgeRuntimeStateTests {
    /// <summary>
    /// Ensures bridge preset detection only matches supported bridge identifiers.
    /// </summary>
    [Theory]
    [InlineData("anthropic-bridge", true)]
    [InlineData("gemini-bridge", true)]
    [InlineData("Anthropic-Bridge", true)]
    [InlineData("manual", false)]
    [InlineData("", false)]
    public void IsBridgeCompatiblePreset_DetectsSupportedPresets(string preset, bool expected) {
        var result = MainWindow.IsBridgeCompatiblePreset(preset);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Ensures auth-failure warning detection flags bridge credential failures.
    /// </summary>
    [Theory]
    [InlineData("401 Unauthorized", true)]
    [InlineData("403 Forbidden", true)]
    [InlineData("Invalid credentials", true)]
    [InlineData("Auth required", true)]
    [InlineData("LM Studio is not reachable", false)]
    [InlineData("", false)]
    public void IsBridgeAuthFailureWarning_RecognizesBridgeAuthFailures(string warning, bool expected) {
        var result = MainWindow.IsBridgeAuthFailureWarning(warning);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Ensures bridge session state mapping prioritizes active apply, then auth failures, then model readiness.
    /// </summary>
    [Theory]
    [InlineData(true, "", 0, "connecting")]
    [InlineData(false, "401 Unauthorized", 5, "auth-failed")]
    [InlineData(false, "", 1, "ready")]
    [InlineData(false, "", 0, "connecting")]
    public void ResolveBridgeSessionState_MapsApplyWarningAndModelAvailability(
        bool applyInFlight,
        string warning,
        int discoveredModels,
        string expected) {
        var state = MainWindow.ResolveBridgeSessionState(applyInFlight, warning, discoveredModels);
        Assert.Equal(expected, state);
    }

    /// <summary>
    /// Ensures ready-state details include active bridge account identity.
    /// </summary>
    [Fact]
    public void ResolveBridgeSessionDetail_ReturnsReadyTextWithIdentityWhenAvailable() {
        var detail = MainWindow.ResolveBridgeSessionDetail("ready", "user@example.com", "");
        Assert.Equal("Bridge session ready for user@example.com.", detail);
    }

    /// <summary>
    /// Ensures auth-failed detail uses provider warning text when available.
    /// </summary>
    [Fact]
    public void ResolveBridgeSessionDetail_ReturnsWarningWhenAuthFailsWithServerMessage() {
        var detail = MainWindow.ResolveBridgeSessionDetail("auth-failed", "user@example.com", "401 Unauthorized");
        Assert.Equal("401 Unauthorized", detail);
    }

    /// <summary>
    /// Ensures connecting-state detail includes bridge account identity when provided.
    /// </summary>
    [Fact]
    public void ResolveBridgeSessionDetail_ReturnsConnectingTextWithIdentity() {
        var detail = MainWindow.ResolveBridgeSessionDetail("connecting", "user@example.com", "");
        Assert.Equal("Connecting to bridge runtime for user@example.com...", detail);
    }

    /// <summary>
    /// Ensures unconfigured compatible-http identity uses a stable unknown key.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_UsesUnknownKeyWhenUnconfigured() {
        var identity = MainWindow.BuildCompatibleUsageIdentity(null, null, null, null);

        Assert.Equal("compatible-http:unknown", identity.Key);
        Assert.Equal("Compatible HTTP (unconfigured)", identity.Label);
    }

    /// <summary>
    /// Ensures compatible-http usage identity is segmented by explicit account identifier.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_UsesAccountIdWhenProvided() {
        var identity = MainWindow.BuildCompatibleUsageIdentity(
            "https://api.example/v1",
            "bearer",
            "Account-One",
            "");

        Assert.Equal("compatible-http:https://api.example/v1|acct:account-one", identity.Key);
        Assert.Equal("Compatible HTTP (https://api.example/v1 | Account-One)", identity.Label);
    }

    /// <summary>
    /// Ensures basic-auth username becomes the account discriminator when account ID is not provided.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_UsesBasicUsernameAsFallbackIdentity() {
        var identity = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1",
            "basic",
            "",
            "bridge-user@example.com");

        Assert.Equal("compatible-http:https://bridge.local/v1|acct:bridge-user@example.com", identity.Key);
        Assert.Equal("Compatible HTTP (https://bridge.local/v1 | bridge-user@example.com)", identity.Label);
    }
}
