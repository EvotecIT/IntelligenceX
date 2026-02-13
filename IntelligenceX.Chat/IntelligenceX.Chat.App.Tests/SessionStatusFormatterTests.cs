using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for session status text formatting.
/// </summary>
public sealed class SessionStatusFormatterTests {
    /// <summary>
    /// Ensures connection-state helper maps disconnected correctly.
    /// </summary>
    [Fact]
    public void ForConnection_Disconnected_UsesDisconnectedText() {
        var status = SessionStatus.ForConnection(isConnected: false, isAuthenticated: true);
        Assert.Equal("Starting runtime...", SessionStatusFormatter.Format(status));
    }

    /// <summary>
    /// Ensures connection-state helper maps connected+authenticated correctly.
    /// </summary>
    [Fact]
    public void ForConnection_ConnectedAuthenticated_UsesConnectedText() {
        var status = SessionStatus.ForConnection(isConnected: true, isAuthenticated: true);
        Assert.Equal("Ready", SessionStatusFormatter.Format(status));
    }

    /// <summary>
    /// Ensures connection-state helper maps connected+unauthenticated correctly.
    /// </summary>
    [Fact]
    public void ForConnection_ConnectedUnauthenticated_UsesSignInRequiredText() {
        var status = SessionStatus.ForConnection(isConnected: true, isAuthenticated: false);
        Assert.Equal("Sign in to continue", SessionStatusFormatter.Format(status));
    }

    /// <summary>
    /// Ensures specific status values remain stable for UI behavior.
    /// </summary>
    [Fact]
    public void Format_UsesStableValues_ForCommonStates() {
        Assert.Equal("Debug mode on", SessionStatusFormatter.Format(SessionStatus.DebugModeOn()));
        Assert.Equal("Opening sign-in...", SessionStatusFormatter.Format(SessionStatus.OpeningSignIn()));
        Assert.Equal("Runtime unavailable", SessionStatusFormatter.Format(SessionStatus.ConnectFailed()));
        Assert.Equal("Usage limit reached - switch account", SessionStatusFormatter.Format(SessionStatus.UsageLimitReached()));
        Assert.Equal("Exporting...", SessionStatusFormatter.Format(SessionStatus.Exporting()));
    }
}
