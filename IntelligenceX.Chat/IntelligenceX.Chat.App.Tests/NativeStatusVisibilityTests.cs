using IntelligenceX.Chat.App.Native;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>Guards signed-out status visibility when the empty state owns authentication copy.</summary>
public sealed class NativeStatusVisibilityTests {
    /// <summary>Independent errors remain visible without restoring duplicate sign-in banners.</summary>
    [Theory]
    [InlineData((int)NativeAuthenticationState.Required, false, "Ready", false)]
    [InlineData((int)NativeAuthenticationState.Required, false, "Sign-in canceled", false)]
    [InlineData((int)NativeAuthenticationState.Required, false, "Sign-in required.", false)]
    [InlineData((int)NativeAuthenticationState.Required, false, "Queued turns could not be cleared", true)]
    [InlineData((int)NativeAuthenticationState.Failed, false, "Sign-in failed. Use Sign in to reconnect.", false)]
    [InlineData((int)NativeAuthenticationState.Failed, true, "History save failed", true)]
    [InlineData((int)NativeAuthenticationState.SignedIn, false, "Runtime settings are being updated", true)]
    public void IndependentRuntimeStatusRemainsVisible(
        int state, bool hasTranscript, string status, bool expected) {
        Assert.Equal(expected, NativeChatWindow.ShouldShowRuntimeStatus((NativeAuthenticationState)state, hasTranscript, status));
    }
}
