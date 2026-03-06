using System;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.Client;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Ensures auth-required failure detection stays stable for queued sign-in recovery logic.
/// </summary>
public sealed class MainWindowAuthenticationFailureClassificationTests {
    /// <summary>
    /// Verifies structured service error codes classify auth-required failures.
    /// </summary>
    [Fact]
    public void IsAuthenticationRequiredError_ReturnsTrue_ForStructuredNotAuthenticatedCode() {
        var ex = new ChatServiceRequestException("provider said no", code: "not_authenticated");

        Assert.True(MainWindow.IsAuthenticationRequiredError(ex));
    }

    /// <summary>
    /// Verifies common auth-required message text is classified correctly.
    /// </summary>
    [Theory]
    [InlineData("Not authenticated. Run ChatGPT login and retry.")]
    [InlineData("Authentication required before this request can continue.")]
    [InlineData("Sign in to continue.")]
    public void IsAuthenticationRequiredError_ReturnsTrue_ForKnownAuthMessages(string message) {
        var ex = new InvalidOperationException(message);

        Assert.True(MainWindow.IsAuthenticationRequiredError(ex));
    }

    /// <summary>
    /// Verifies non-auth transport/runtime failures are not misclassified.
    /// </summary>
    [Theory]
    [InlineData("Usage limit reached. Retry later.")]
    [InlineData("Timed out waiting for service pipe.")]
    [InlineData("Invalid 'input[7].call_id': string too long. Expected a string with maximum length 64, but got a string with length 73 instead.")]
    [InlineData("Couldn't start chat because tool bootstrap failed: Method not found: 'Void IntelligenceX.Tools.ToolDefinition..ctor(...)'.")]
    [InlineData("")]
    public void IsAuthenticationRequiredError_ReturnsFalse_ForNonAuthFailures(string message) {
        var ex = new InvalidOperationException(message);

        Assert.False(MainWindow.IsAuthenticationRequiredError(ex));
    }
}
