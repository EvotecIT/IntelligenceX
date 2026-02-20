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

        Assert.Equal("compatible-http:https%3A%2F%2Fapi.example%2Fv1|acct:Account-One", identity.Key);
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

        Assert.Equal("compatible-http:https%3A%2F%2Fbridge.local%2Fv1|acct:bridge-user%40example.com", identity.Key);
        Assert.Equal("Compatible HTTP (https://bridge.local/v1 | bridge-user@example.com)", identity.Label);
    }

    /// <summary>
    /// Ensures base/account components are encoded so reserved separators cannot collide across identities.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_EncodesReservedCharactersInUsageKey() {
        var identity = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1|acct:in-base",
            "bearer",
            "user:one|two",
            "");

        Assert.Equal(
            "compatible-http:https%3A%2F%2Fbridge.local%2Fv1%257Cacct%3Ain-base|acct:user%3Aone%7Ctwo",
            identity.Key);
    }

    /// <summary>
    /// Ensures encoded key structure prevents collisions between base URL text and explicit account suffix.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_DoesNotCollideBaseDelimiterTextWithAccountSuffix() {
        var embeddedAccountInBase = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1|acct:user",
            "bearer",
            "",
            "");
        var explicitAccount = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1",
            "bearer",
            "user",
            "");

        Assert.NotEqual(embeddedAccountInBase.Key, explicitAccount.Key);
    }

    /// <summary>
    /// Ensures account IDs preserve case in usage keys to avoid case-folding attribution collisions.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_PreservesAccountCaseInUsageKey() {
        var upper = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1",
            "bearer",
            "AccountA",
            "");
        var lower = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1",
            "bearer",
            "accounta",
            "");

        Assert.NotEqual(upper.Key, lower.Key);
    }

    /// <summary>
    /// Ensures semantically equivalent base URLs map to one usage identity key.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_CanonicalizesEquivalentBaseUrls() {
        var canonical = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local/v1",
            "bearer",
            "account",
            "");
        var equivalent = MainWindow.BuildCompatibleUsageIdentity(
            "HTTPS://Bridge.Local:443/v1/",
            "bearer",
            "account",
            "");

        Assert.Equal(canonical.Key, equivalent.Key);
        Assert.Equal("Compatible HTTP (https://bridge.local/v1 | account)", equivalent.Label);
    }

    /// <summary>
    /// Ensures non-default ports stay part of canonical usage identity keys.
    /// </summary>
    [Fact]
    public void BuildCompatibleUsageIdentity_PreservesNonDefaultPortInCanonicalKey() {
        var identity = MainWindow.BuildCompatibleUsageIdentity(
            "https://bridge.local:8443/v1/",
            "bearer",
            "",
            "");

        Assert.Equal("compatible-http:https%3A%2F%2Fbridge.local%3A8443%2Fv1", identity.Key);
        Assert.Equal("Compatible HTTP (https://bridge.local:8443/v1)", identity.Label);
    }
}
