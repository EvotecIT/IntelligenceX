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
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    public void TryParseFlexibleBoolean_ParsesProtocolBooleanLiterals(string input, bool expected) {
        var args = new object?[] { input, false };
        var resolved = TryParseFlexibleBooleanMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(resolved));
        Assert.Equal(expected, Assert.IsType<bool>(args[1]));
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("on")]
    [InlineData("off")]
    [InlineData("sí")]
    [InlineData("oui")]
    [InlineData("да")]
    [InlineData("نعم")]
    [InlineData("はい")]
    public void TryParseFlexibleBoolean_RejectsNaturalLanguageTokens(string input) {
        var args = new object?[] { input, true };
        var resolved = TryParseFlexibleBooleanMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(resolved));
        Assert.False(Assert.IsType<bool>(args[1]));
    }
}
