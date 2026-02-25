using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Chat.Service;
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
            mutating: false
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
        var draft = "If you say \"run now\", I'll execute it.";
        var method = typeof(ChatServiceSession).GetMethod(
            "ExtractPendingActionCallToActionTokens",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var tokensObj = method!.Invoke(null, new object?[] { draft });
        var tokens = Assert.IsType<string[]>(tokensObj);

        Assert.Contains(tokens, static t => string.Equals(t, "run now", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenAssistantUsesCurlyQuotesForCta() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say “run now”, I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            mutating: false
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
            mutating: false
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
            mutating: false
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
    [InlineData("run now\uFF1A")]
    [InlineData("run now\uFF1B")]
    public void ExpandContinuationUserRequest_DoesNotConfirmWhenAssistantCtaHadTrailingColonButUserRepliesWithPrefix(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say "run now:", I'll execute it.

            [Action]
            ix:action:v1
            id: act_001
            title: First
            request: Do first thing.
            mutating: false
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
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
            mutating: false
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
            mutating: false
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenUserMatchesActionIntentWithoutCtaEcho() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_failed4625
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            mutating: false
            reply: /act act_failed4625
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "failed logons please" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_failed4625", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenUserUsesShortNonLatinIntentTokens() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_cn_failed
            title: 失败 登录 报告
            request: 运行 失败 登录 报告 并 汇总
            mutating: false
            reply: /act act_cn_failed
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "失败 登录" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_cn_failed", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenTwoTokenFollowUpEndsWithShortNonLatinIntentToken() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_cn_execute
            title: 执行 巡检 报告
            request: 执行 域 控制器 巡检 并 汇总
            mutating: false
            reply: /act act_cn_execute
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "马上 执行" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_cn_execute", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveSinglePendingActionWhenShortNonLatinIntentTokenIsNonTrailing() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_cn_execute
            title: 执行 巡检 报告
            request: 执行 域 控制器 巡检 并 汇总
            mutating: false
            reply: /act act_cn_execute
            """;
        var input = "执行 马上";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenUserUsesContiguousNonLatinIntentToken() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_cn_compact
            title: 失败登录报告
            request: 运行失败登录报告并汇总
            mutating: false
            reply: /act act_cn_compact
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "失败登录报告" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_cn_compact", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveSinglePendingActionWhenContiguousNonLatinIntentTokenDoesNotMatch() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_cn_compact
            title: 失败登录报告
            request: 运行失败登录报告并汇总
            mutating: false
            reply: /act act_cn_compact
            """;
        var input = "异常登录报告";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveSinglePendingActionWhenUserUsesOnlyShortAsciiTokens() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_failed4625
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            mutating: false
            reply: /act act_failed4625
            """;
        var input = "go it";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenUserUsesNaturalConfirmationWithoutHardcodedPhrase() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_failed4625
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            mutating: false
            reply: /act act_failed4625
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_failed4625", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSinglePendingActionWhenUserUsesLongContextualConfirmation() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_failed4625
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            mutating: false
            reply: /act act_failed4625
            """;
        var input =
            "Please proceed with the failed logon report on ADO Security and include a concise top-events summary before we move on.";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_failed4625", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesBestPendingActionByIntentOverlapWhenMultipleActionsAndLongFollowUp() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_failed4625
            title: Failed logons (4625)
            request: Run failed logon report on ADO Security.
            mutating: false
            reply: /act act_failed4625

            [Action]
            ix:action:v1
            id: act_lockout4740
            title: Account lockouts (4740)
            request: Run account lockout report on ADO Security.
            mutating: false
            reply: /act act_lockout4740
            """;
        var input =
            "Let's start with the account lockouts 4740 report on ADO Security first and summarize impacted users for triage.";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_lockout4740", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveMutatingSinglePendingActionForLongNaturalLanguageConfirmation() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_disable_user
            title: Disable user account
            request: Disable user evotec\john and confirm status.
            mutating: true
            reply: /act act_disable_user
            """;
        var input =
            "Please go ahead with that user disable operation now and then confirm the account state in the same run.";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveMutatingSinglePendingActionForNaturalLanguageConfirmation() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_disable_user
            title: Disable user account
            request: Disable user evotec\john and confirm status.
            mutating: true
            reply: /act act_disable_user
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("go ahead and run it", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveUnknownSinglePendingActionForLongNaturalLanguageConfirmation() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_unknown_single
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            reply: /act act_unknown_single
            """;
        var input =
            "Please proceed with the failed logon report on ADO Security and include a concise top-events summary before we move on.";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveUnknownSinglePendingActionWhenUserEchoesAssistantCallToAction() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            If you say "run now", I'll execute it.

            [Action]
            ix:action:v1
            id: act_unknown_single
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            reply: /act act_unknown_single
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run now" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("run now", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveUnknownPendingActionByIntentOverlapWhenMultipleActionsExist() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_unknown_failed4625
            title: Failed logons (4625)
            request: Run failed logon report on ADO Security.
            reply: /act act_unknown_failed4625

            [Action]
            ix:action:v1
            id: act_unknown_lockout4740
            title: Account lockouts (4740)
            request: Run account lockout report on ADO Security.
            reply: /act act_unknown_lockout4740
            """;
        var input =
            "Let's start with the account lockouts 4740 report on ADO Security first and summarize impacted users for triage.";

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesUnknownSinglePendingActionWhenUserUsesExplicitAct() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_unknown_single
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            reply: /act act_unknown_single
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_unknown_single" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_unknown_single", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesUnknownSinglePendingActionWhenUserUsesOrdinalSelection() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_unknown_single
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            reply: /act act_unknown_single
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "1" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_unknown_single", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("１")]
    [InlineData("１．")]
    [InlineData("１）")]
    [InlineData("١")]
    [InlineData("١)")]
    [InlineData("١：")]
    [InlineData("①")]
    [InlineData("⑴")]
    [InlineData("❶")]
    public void ExpandContinuationUserRequest_ResolvesUnknownSinglePendingActionWhenUserUsesUnicodeOrdinalSelection(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_unknown_single
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            reply: /act act_unknown_single
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_unknown_single", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesMutatingSinglePendingActionWhenUserUsesExplicitAct() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_disable_user
            title: Disable user account
            request: Disable user evotec\john and confirm status.
            mutating: true
            reply: /act act_disable_user
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_disable_user" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_disable_user", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesMutatingSinglePendingActionWhenUserUsesOrdinalSelection() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_disable_user
            title: Disable user account
            request: Disable user evotec\john and confirm status.
            mutating: true
            reply: /act act_disable_user
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "1" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_disable_user", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_PreservesMutatingFlagForExplicitMutatingActionSelection() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_disable_user
            title: Disable user account
            request: Disable user evotec\john and confirm status.
            mutating: true
            reply: /act act_disable_user
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_disable_user" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.True(selection.GetProperty("mutating").GetBoolean());
    }

    [Fact]
    public void ExpandContinuationUserRequest_PreservesReadOnlyFlagForActionSelection() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_failed4625
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            mutating: false
            reply: /act act_failed4625
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "failed logons please" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.False(selection.GetProperty("mutating").GetBoolean());
    }

}
