using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests the app launch contract without constructing WinUI windows.
/// </summary>
public sealed class ChatAppLaunchModeResolverTests {
    /// <summary>
    /// Ensures normal launches use the native WinUI shell instead of the legacy WebView shell.
    /// </summary>
    [Fact]
    public void Resolve_DefaultsToNativeWinUi() {
        var mode = ChatAppLaunchModeResolver.Resolve(_ => null);

        Assert.Equal(ChatAppLaunchMode.NativeWinUI, mode);
    }

    /// <summary>
    /// Ensures the legacy WebView shell remains available only as an explicit escape hatch.
    /// </summary>
    [Fact]
    public void Resolve_UsesLegacyWebViewWhenExplicitlyRequested() {
        var mode = ChatAppLaunchModeResolver.Resolve(CreateEnvironment(
            (ChatAppLaunchModeResolver.LegacyWebViewEnvVar, "1")));

        Assert.Equal(ChatAppLaunchMode.LegacyWebView, mode);
    }

    /// <summary>
    /// Ensures diagnostic launch modes continue to outrank the normal shell selection.
    /// </summary>
    [Theory]
    [InlineData(ChatAppLaunchModeResolver.MinimalWindowEnvVar, nameof(ChatAppLaunchMode.MinimalWindow))]
    [InlineData(ChatAppLaunchModeResolver.WebViewSmokeEnvVar, nameof(ChatAppLaunchMode.WebViewSmoke))]
    public void Resolve_PreservesDiagnosticLaunchModes(string variableName, string expectedName) {
        var mode = ChatAppLaunchModeResolver.Resolve(CreateEnvironment(
            (variableName, "true"),
            (ChatAppLaunchModeResolver.LegacyWebViewEnvVar, "true")));

        Assert.Equal(expectedName, mode.ToString());
    }

    /// <summary>
    /// Ensures the old native opt-in flag is harmless because native WinUI is now the default.
    /// </summary>
    [Fact]
    public void Resolve_TreatsNativeWinUiFlagAsNoOpCompatibilitySignal() {
        var mode = ChatAppLaunchModeResolver.Resolve(CreateEnvironment(
            (ChatAppLaunchModeResolver.NativeWinUiEnvVar, "1")));

        Assert.Equal(ChatAppLaunchMode.NativeWinUI, mode);
    }

    private static Func<string, string?> CreateEnvironment(params (string Name, string Value)[] values) {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values) {
            map[value.Name] = value.Value;
        }

        return name => map.TryGetValue(name, out var value) ? value : null;
    }
}
