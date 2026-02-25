using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools;
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
    public void ShouldRetryNoToolExecution_TriggersForBlockerPrefaceWithoutQuestion() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "Return a UTC timeline with event ID, DC hostname, source client/IP if available, and success/failure signal.",
            assistantDraft: "I can do that, but I need to run payload-based multi-DC event queries first to produce the timeline.");

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_DoesNotTriggerForUnrelatedBlockerPrefaceWithoutContextAnchor() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "Show replication summary now.",
            assistantDraft: "I can do that, but this account has no more credits today.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_TriggersForScenarioExecutionContractTurns() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "[Scenario execution contract]\nThis scenario turn requires tool execution before the final response.\nUser request:\nCompare lastLogon vs lastLogonTimestamp.",
            assistantDraft: "Based on prior tool output, lastLogon is newer than lastLogonTimestamp and replication lag applies.");

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryScenarioContractRepair_TriggersWhenDistinctCoverageIsIncomplete() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 2.
- Distinct tool input value requirements: machine_name>=2.
User request:
Continue recurring-error analysis across all remaining DCs in this turn.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_stats", "{\"machine_name\":\"AD1\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryScenarioContractRepair_DoesNotTriggerWhenContractCoverageIsMet() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 2.
- Distinct tool input value requirements: machine_name>=2.
User request:
Continue recurring-error analysis across all remaining DCs in this turn.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_stats", "{\"machine_name\":\"AD1\"}"),
            BuildToolCall("call_2", "eventlog_live_stats", "{\"machine_name\":\"AD2\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryScenarioContractRepair_TriggersWhenRequiredAnyToolPatternIsMissing() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 1.
- Required tool calls (at least one): ad_*replication*, ad_monitoring_probe_run.
User request:
Continue replication checks across all remaining DCs in this turn and surface any lag or errors.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_stats", "{\"machine_name\":\"AD1\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryScenarioContractRepair_DoesNotTriggerWhenRequiredAnyToolPatternIsSatisfied() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 1.
- Required tool calls (at least one): ad_*replication*, ad_monitoring_probe_run.
User request:
Continue replication checks across all remaining DCs in this turn and surface any lag or errors.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "ad_replication_summary", "{\"domain_controller\":\"AD1\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.False(result);
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

    private static bool InvokeShouldRetryScenarioContractRepair(string userRequest, IReadOnlyList<ToolCall> calls) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldRetryScenarioContractRepair",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, calls });
        return value is bool b && b;
    }

    private static ToolCall BuildToolCall(string callId, string name, string jsonArgs) {
        var args = JsonLite.Parse(jsonArgs)?.AsObject();
        var raw = new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("call_id", callId)
            .Add("name", name)
            .Add("input", jsonArgs);
        return new ToolCall(callId, name, jsonArgs, args, raw);
    }
}
