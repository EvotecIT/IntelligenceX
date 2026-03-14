using System.Text.Json;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ExpandContinuationUserRequest_ResolvesUnknownSinglePendingActionWhenUserEchoesAssistantCallToAction() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        using var doc = JsonDocument.Parse(expanded);
        Assert.Equal("act_unknown_single", doc.RootElement.GetProperty("ix_action_selection").GetProperty("id").GetString());
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveUnknownSinglePendingActionForCompactNonEchoFollowUpWhenAssistantProvidedCtaContext() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("go ahead", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveUnknownSinglePendingActionForCompactFollowUpWithoutCtaContext() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_unknown_single
            title: Run failed logon report (4625)
            request: Run failed logon report on ADO Security and summarize the top five events.
            reply: /act act_unknown_single
            """;

        RememberPendingActionsMethod.Invoke(session, new object?[] { "thread-001", assistantDraft });
        var result = ExpandContinuationUserRequestMethod.Invoke(session, new object?[] { "thread-001", "go ahead" });
        var expanded = Assert.IsType<string>(result);

        Assert.Equal("go ahead", expanded);
    }

    [Fact]
    public void ExpandContinuationUserRequest_DoesNotResolveUnknownPendingActionByIntentOverlapWhenMultipleActionsExist() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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
