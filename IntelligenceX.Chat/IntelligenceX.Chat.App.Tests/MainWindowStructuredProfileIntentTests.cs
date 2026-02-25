using System;
using System.Reflection;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies language-neutral structured profile extraction from key-value payloads.
/// </summary>
public sealed class MainWindowStructuredProfileIntentTests {
    private static readonly MethodInfo TryExtractStructuredUserProfileIntentMethod = typeof(MainWindow).GetMethod(
                                                                                          "TryExtractStructuredUserProfileIntent",
                                                                                          BindingFlags.NonPublic | BindingFlags.Static)
                                                                                      ?? throw new InvalidOperationException("TryExtractStructuredUserProfileIntent method not found.");

    /// <summary>
    /// Ensures fenced ix_profile key-value payloads parse with Unicode separators and populate all profile fields.
    /// </summary>
    [Fact]
    public void TryExtractStructuredUserProfileIntent_ParsesFencedKeyValuePayloadWithUnicodeDelimiters() {
        const string input = """
                             ```ix_profile
                             user_name：Марек
                             assistant_persona = Аналитик безопасности
                             theme_preset: ocean
                             scope＝profile
                             ```
                             """;

        var args = new object?[] { input, null };
        var parsed = (bool)(TryExtractStructuredUserProfileIntentMethod.Invoke(null, args) ?? false);
        Assert.NotNull(args[1]);
        var intent = args[1]!;

        Assert.True(parsed);
        Assert.True(ReadIntentBoolean(intent, "HasUserName"));
        Assert.Equal("Марек", ReadIntentString(intent, "UserName"));
        Assert.True(ReadIntentBoolean(intent, "HasAssistantPersona"));
        Assert.Equal("Аналитик безопасности", ReadIntentString(intent, "AssistantPersona"));
        Assert.True(ReadIntentBoolean(intent, "HasThemePreset"));
        Assert.Equal("ocean", ReadIntentString(intent, "ThemePreset"));
        Assert.Equal("Profile", ReadIntentString(intent, "Scope"));
    }

    /// <summary>
    /// Ensures inline semicolon-delimited key-value profile payloads parse without English command cues.
    /// </summary>
    [Fact]
    public void TryExtractStructuredUserProfileIntent_ParsesInlineKeyValueSegments() {
        const string input = "user_name: Amina; assistant_persona: focused helper; theme: forest; scope: session";

        var args = new object?[] { input, null };
        var parsed = (bool)(TryExtractStructuredUserProfileIntentMethod.Invoke(null, args) ?? false);
        Assert.NotNull(args[1]);
        var intent = args[1]!;

        Assert.True(parsed);
        Assert.True(ReadIntentBoolean(intent, "HasUserName"));
        Assert.Equal("Amina", ReadIntentString(intent, "UserName"));
        Assert.True(ReadIntentBoolean(intent, "HasAssistantPersona"));
        Assert.Equal("focused helper", ReadIntentString(intent, "AssistantPersona"));
        Assert.True(ReadIntentBoolean(intent, "HasThemePreset"));
        Assert.Equal("forest", ReadIntentString(intent, "ThemePreset"));
        Assert.Equal("Session", ReadIntentString(intent, "Scope"));
    }

    private static bool ReadIntentBoolean(object intent, string propertyName) {
        var property = intent.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException($"Property {propertyName} not found on UserProfileIntent.");
        return Assert.IsType<bool>(property.GetValue(intent));
    }

    private static string ReadIntentString(object intent, string propertyName) {
        var property = intent.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                       ?? throw new InvalidOperationException($"Property {propertyName} not found on UserProfileIntent.");
        return (property.GetValue(intent)?.ToString() ?? string.Empty).Trim();
    }
}
