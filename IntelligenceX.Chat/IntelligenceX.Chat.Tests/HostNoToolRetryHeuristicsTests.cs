using System.Reflection;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostNoToolRetryHeuristicsTests {
    [Fact]
    public void ShouldRetryNoToolExecution_TriggersForEmptyAssistantDraftWithSubstantialRequest() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.",
            assistantDraft: string.Empty);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_DoesNotTriggerForEmptyAssistantDraftWithEmptyRequest() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "   ",
            assistantDraft: string.Empty);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_TriggersForLinkedFollowUpQuestionDraft() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "Correlate with recent security log evidence (4624/4625/4768/4769/4771) around the latest sign-in window.",
            assistantDraft: "I can do that for this security sign-in window, but I need one minimal detail first: is AD0 reachable as a remote Event Log target from this session?");

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_DoesNotTriggerForShortUnrelatedQuestionDraft() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "Hi",
            assistantDraft: "Which one?");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_TriggersForScenarioExecutionContractTurns() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "[Scenario execution contract]\nThis scenario turn requires tool execution before the final response.\nUser request:\nCompare lastLogon vs lastLogonTimestamp.",
            assistantDraft: "Based on prior tool output, lastLogon is newer than lastLogonTimestamp and replication lag applies.");

        Assert.True(result);
    }

    private static bool InvokeShouldRetryNoToolExecution(string userRequest, string assistantDraft) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldRetryNoToolExecution",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, assistantDraft });
        return value is bool b && b;
    }
}
