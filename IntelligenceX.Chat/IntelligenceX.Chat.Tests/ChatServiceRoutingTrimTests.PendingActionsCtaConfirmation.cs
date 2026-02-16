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

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveSinglePendingActionWhenSingleOverlapIsNonTrailing() {
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run later" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("run later", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesBestPendingActionByIntentOverlapWhenMultipleActionsExist() {
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

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "failed logons please" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_failed4625", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveMultiplePendingActionsOnAmbiguousSingleTokenOverlap() {
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

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "run it" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("run it", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesFallbackBulletChoiceWithoutActionMarker() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Absolutely — want me to pull top 5 recent events, or go straight to a focused cut like:
            - Failed logons (4625)
            - Account lockouts (4740)
            - Logon activity mix (4624/4625/4634/4647)
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "failed logons please" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.Equal("choice_001", selection.GetProperty("id").GetString());
        Assert.Equal("Failed logons (4625)", selection.GetProperty("request").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveFallbackBulletChoiceWithoutPromptContext() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Options
            - Failed logons (4625)
            - Account lockouts (4740)
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "failed logons please" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("failed logons please", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSingleNumberedFallbackChoiceWithPromptContext() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pulling failed logons from ADO now would be the next step, but I need your go-ahead in this flow:
            1. Run failed logon report (4625) on ADO Security
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.Equal("choice_001", selection.GetProperty("id").GetString());
        Assert.Equal("Run failed logon report (4625) on ADO Security", selection.GetProperty("request").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveSingleBulletFallbackChoiceWithPromptContext() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pulling failed logons from ADO now would be the next step, but I need your go-ahead in this flow:
            - Run failed logon report (4625) on ADO Security
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("go ahead and run it", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSingleBulletFallbackChoiceWithInlineActIdWithPromptContext() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pulling failed logons from ADO now would be the next step, but I need your go-ahead in this flow:
            - Run failed logon report (4625) on ADO Security (/act act_failed4625)
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.Equal("act_failed4625", selection.GetProperty("id").GetString());
        Assert.Equal("Run failed logon report (4625) on ADO Security", selection.GetProperty("request").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesSingleNumberedFallbackChoiceWithInlineActIdInParentheses() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pulling failed logons from ADO now would be the next step, but I need your go-ahead in this flow:
            You can reply with /act act_failed4625 (or just 1) and I'll execute it immediately.
            You can run one of these follow-up actions:
            1. Run failed logon report (4625) on ADO Security (/act act_failed4625)
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.Equal("act_failed4625", selection.GetProperty("id").GetString());
        Assert.Equal("Run failed logon report (4625) on ADO Security", selection.GetProperty("request").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesExplicitActForFallbackChoiceWithInlineActIdInParentheses() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            Pulling failed logons from ADO now would be the next step, but I need your go-ahead in this flow:
            You can run one of these follow-up actions:
            1. Run failed logon report (4625) on ADO Security (/act act_failed4625)
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_failed4625" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.Equal("act_failed4625", selection.GetProperty("id").GetString());
        Assert.Equal("Run failed logon report (4625) on ADO Security", selection.GetProperty("request").GetString());
    }

    [Theory]
    [InlineData("`/act act_001`")]
    [InlineData("\"/act act_001\"")]
    [InlineData("(/act act_001)")]
    [InlineData("/act act_001.")]
    [InlineData("/act act_001!")]
    [InlineData("/act act_001?")]
    public void ExpandContinuationUserRequest_ResolvesExplicitActWhenCommandUsesSafeWrappersOrPunctuation(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: Failed logons (4625)
            request: Run failed logons report.
            mutating: false
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("/act act_001 please")]
    [InlineData("/act act_001 and summarize")]
    public void ExpandContinuationUserRequest_DoesNotResolveExplicitActWhenCommandHasTrailingText(string input) {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: Failed logons (4625)
            request: Run failed logons report.
            mutating: false
            reply: /act act_001
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", input });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal(input, expanded);
    }

    [Fact]
    public void RememberPendingActions_NoMarkerOrFallbackChoices_DoesNotClearExistingActionContext() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var actionDraft = """
            [Action]
            ix:action:v1
            id: act_001
            title: Failed logons (4625)
            request: Run failed logons report.
            mutating: false
            reply: /act act_001
            """;
        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", actionDraft });

        var plainAssistantMessage = """
            Quick read
            normal service state flips, no spicy errors in this 5-event window.
            """;
        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", plainAssistantMessage });

        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "/act act_001" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_001", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void RememberPendingActions_RehydratesReplayActionFromExecutionContractBlockerText() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var selectionPayload = "{\"ix_action_selection\":{\"id\":\"act_failed4625\",\"title\":\"Failed logons (4625)\",\"request\":\"Run failed logon report on ADO Security and summarize top events.\",\"mutating\":false}}";
        var blockerText = Assert.IsType<string>(BuildExecutionContractBlockerTextMethod.Invoke(
            null,
            new object?[] { selectionPayload, "Ok, doing it now.", "no_tool_calls_after_watchdog_retry" }));

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", blockerText });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead and run it" });
        var expanded = Assert.IsType<string>(result);

        using var doc = JsonDocument.Parse(expanded);
        var selection = doc.RootElement.GetProperty("ix_action_selection");
        Assert.Equal("act_failed4625", selection.GetProperty("id").GetString());
    }
}
