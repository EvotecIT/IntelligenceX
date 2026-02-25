using System;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies profile-update scope fallback behavior stays session-safe unless structured scope is explicit.
/// </summary>
public sealed class MainWindowProfileScopeFallbackTests {
    private static readonly MethodInfo DetectProfileUpdateScopeMethod = typeof(MainWindow).GetMethod(
                                                                             "DetectProfileUpdateScope",
                                                                             BindingFlags.NonPublic | BindingFlags.Static)
                                                                         ?? throw new InvalidOperationException("DetectProfileUpdateScope method not found.");

    /// <summary>
    /// Ensures scope detection favors structured scope markers and defaults ambiguous cues to session scope.
    /// </summary>
    [Theory]
    [InlineData("Please call me Alex in this chat.", "Session")]
    [InlineData("scope: session", "Session")]
    [InlineData("scope = profile", "Profile")]
    [InlineData("{\"scope\":\"session\"}", "Session")]
    [InlineData("{\"scope\":\"profile\"}", "Profile")]
    [InlineData("```ix_profile\n{\"scope\":\"session\"}\n```", "Session")]
    [InlineData("```ix_profile\n{\"scope\":\"profile\"}\n```", "Profile")]
    [InlineData("scope: profile", "Profile")]
    [InlineData("", "Unspecified")]
    public void DetectProfileUpdateScope_PrefersStructuredScope_AndDefaultsToSession(string input, string expected) {
        var result = DetectProfileUpdateScopeMethod.Invoke(null, new object?[] { input });
        Assert.NotNull(result);
        Assert.Equal(expected, result!.ToString());
    }
}
