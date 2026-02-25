using System;
using System.Reflection;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo TryParseFlexibleBooleanMethod =
        typeof(ChatServiceSession).GetMethod("TryParseFlexibleBoolean", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryParseFlexibleBoolean not found.");

    [Theory]
    [InlineData("sí")]
    [InlineData("sim")]
    [InlineData("oui")]
    [InlineData("ja")]
    [InlineData("tak")]
    [InlineData("да")]
    [InlineData("نعم")]
    [InlineData("是")]
    [InlineData("はい")]
    [InlineData("예")]
    [InlineData("evet")]
    public void TryParseFlexibleBoolean_ParsesLanguageInclusiveTrueTokens(string input) {
        var args = new object?[] { input, false };
        var resolved = TryParseFlexibleBooleanMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(resolved));
        Assert.True(Assert.IsType<bool>(args[1]));
    }

    [Theory]
    [InlineData("non")]
    [InlineData("nein")]
    [InlineData("nie")]
    [InlineData("não")]
    [InlineData("нет")]
    [InlineData("لا")]
    [InlineData("否")]
    [InlineData("いいえ")]
    [InlineData("아니요")]
    [InlineData("hayır")]
    public void TryParseFlexibleBoolean_ParsesLanguageInclusiveFalseTokens(string input) {
        var args = new object?[] { input, true };
        var resolved = TryParseFlexibleBooleanMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(resolved));
        Assert.False(Assert.IsType<bool>(args[1]));
    }
}
