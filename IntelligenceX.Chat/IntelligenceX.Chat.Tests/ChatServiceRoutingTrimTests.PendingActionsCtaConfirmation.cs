using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
[Theory]
    [InlineData("run now")]
    [InlineData(" run now ")]
    [InlineData("run now.")]
    [InlineData("run now,")]
    [InlineData("`run now`")]
    [InlineData(" run now! ")]
    [InlineData("\"run now\"")]
    [InlineData("'run now'")]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenUserEchoesAssistantCallToAction(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say "run now", I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("ix_action_selection", out var selection));
        Assert.Equal("act_001", selection.GetProperty("id").GetString());
    }


    [Fact]
    public void ExtractPendingActionCallToActionTokens_RecognizesCommaAfterQuoteCtaPattern() {
        var draft = "If you say \"run now\", I\'ll execute it.";
        var method = typeof(ChatServiceSession).GetMethod(
            "ExtractPendingActionCallToActionTokens",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var tokensObj = method!.Invoke(null, new object?[] { draft });
        var tokens = Assert.IsType<string[]>(tokensObj);

        Assert.Contains(tokens, static t => string.Equals(t, "run now", StringComparison.OrdinalIgnoreCase));
    }    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenAssistantUsesCurlyQuotesForCta() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say “run now”, I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenAssistantUsesFullWidthQuotesForCta() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say ＂run now＂, I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenAssistantCtaIncludesTrailingColonInQuote() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say "run now:", I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("run now:")]
    [InlineData("run now;")]
    [InlineData("run now：")]
    [InlineData("run now；")]
    [InlineData("`run now`:")]
    public void ExpandContinuationUserRequest_DoesNotConfirmWhenFollowUpLooksLikeAPrefix(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say "run now", I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }
    [Theory]
    [InlineData("{run now}")]
    [InlineData("{\"run now\"}")]
    [InlineData("[run now]")]
    [InlineData("<run now>")]
    [InlineData("/run now")]
    [InlineData("run now=1")]
    public void ExpandContinuationUserRequest_DoesNotConfirmWhenUserInputLooksLikeStructuredPayloadOrCommand(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say "run now", I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }
}
