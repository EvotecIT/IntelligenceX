using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class HostNoToolRetryHeuristicsTests {
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
    public void ShouldRetryNoToolExecution_TriggersForDeferredExecutionNarrativeInNonEnglish() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "Devuelve una línea de tiempo UTC por DC para AD0.ad.evotec.xyz con IDs 4624 y 4625.",
            assistantDraft: "Puedo hacerlo, pero primero necesito ejecutar consultas de eventos en varios DC para construir la línea de tiempo.");

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_TriggersForScenarioExecutionContractTurns() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "[Scenario execution contract]\nThis scenario turn requires tool execution before the final response.\nUser request:\nCompare lastLogon vs lastLogonTimestamp.",
            assistantDraft: "Based on prior tool output, lastLogon is newer than lastLogonTimestamp and replication lag applies.");

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_DoesNotTriggerForScenarioNoToolExecutionContractTurns() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "[Scenario execution contract]\nThis scenario turn requires a response without tool execution.\nUser request:\nAcknowledge the chosen scope only.",
            assistantDraft: "Scope acknowledged. Continuing with the selected domain family.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_TriggersForScenarioExecutionContractTurns_WithStructuredDirectiveOnly() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 1
required_tools_any: ad_ldap_query*
User request:
Compare lastLogon vs lastLogonTimestamp.
""";

        var result = InvokeShouldRetryNoToolExecution(
            userRequest: request,
            assistantDraft: "I will summarize now based only on prior context.");

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_DoesNotTriggerForScenarioNoToolExecutionContractTurns_WithStructuredDirectiveOnly() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: false
requires_no_tool_execution: true
min_tool_calls: 0
required_tools_any: none
User request:
Acknowledge the chosen scope only.
""";

        var result = InvokeShouldRetryNoToolExecution(
            userRequest: request,
            assistantDraft: "Scope acknowledged. Continuing with the selected domain family.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryNoToolExecution_DoesNotTriggerForEmptyDraftOnScenarioNoToolExecutionContractTurn() {
        var result = InvokeShouldRetryNoToolExecution(
            userRequest: "[Scenario execution contract]\nThis scenario turn requires a response without tool execution.\nUser request:\nAcknowledge the chosen scope only.",
            assistantDraft: string.Empty);

        Assert.False(result);
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
    public void ShouldRetryScenarioContractRepair_ParsesStructuredDistinctInputRequirements() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*query*, eventlog_*stats*
distinct_tool_inputs: machine_name>=2
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
    public void ShouldRetryScenarioContractRepair_TriggersWhenForbiddenToolInputValueIsObserved() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*query*
distinct_tool_inputs: machine_name>=2
forbidden_tool_inputs: machine_name!=AD0
User request:
Continue recurring-error analysis across all remaining non-AD0 DCs in this turn.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", "{\"machine_name\":\"AD0\"}"),
            BuildToolCall("call_2", "eventlog_live_query", "{\"machine_name\":\"AD1\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryScenarioContractRepair_DoesNotTriggerWhenForbiddenToolInputValuesAreAbsent() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*query*
distinct_tool_inputs: machine_name>=2
forbidden_tool_inputs: machine_name!=AD0
User request:
Continue recurring-error analysis across all remaining non-AD0 DCs in this turn.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", "{\"machine_name\":\"AD1\"}"),
            BuildToolCall("call_2", "eventlog_live_query", "{\"machine_name\":\"AD2\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.False(result);
    }

    [Fact]
    public void ShouldRetryScenarioContractRepair_TriggersWhenForbiddenShortHostMatchesFqdnInput() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*query*
distinct_tool_inputs: machine_name>=2
forbidden_tool_inputs: machine_name!=AD0
User request:
Continue recurring-error analysis across all remaining non-AD0 DCs in this turn.
""";

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", "{\"machine_name\":\"AD0.ad.evotec.xyz\"}"),
            BuildToolCall("call_2", "eventlog_live_query", "{\"machine_name\":\"AD1.ad.evotec.xyz\"}")
        };

        var result = InvokeShouldRetryScenarioContractRepair(request, calls);

        Assert.True(result);
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

    [Fact]
    public void ResolveScenarioRepairForcedToolName_SelectsRequiredPatternToolOnEscalatedRetry() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 1.
- Required tool calls (at least one): ad_*replication*, ad_monitoring_probe_run.
User request:
Continue replication checks across all remaining DCs in this turn and surface any lag or errors.
""";

        var toolDefs = new List<ToolDefinition> {
            new("eventlog_live_query"),
            new("ad_monitoring_probe_run"),
            new("ad_replication_summary")
        };

        var forced = InvokeResolveScenarioRepairForcedToolName(
            userRequest: request,
            calls: new List<ToolCall>(),
            toolDefinitions: toolDefs,
            retryAttempt: 2);

        Assert.Equal("ad_replication_summary", forced);
    }

    [Fact]
    public void ResolveScenarioRepairForcedToolName_DoesNotSelectBeforeEscalationThreshold() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 1.
- Required tool calls (at least one): ad_*replication*, ad_monitoring_probe_run.
User request:
Continue replication checks across all remaining DCs in this turn and surface any lag or errors.
""";

        var toolDefs = new List<ToolDefinition> {
            new("ad_replication_summary")
        };

        var forced = InvokeResolveScenarioRepairForcedToolName(
            userRequest: request,
            calls: new List<ToolCall>(),
            toolDefinitions: toolDefs,
            retryAttempt: 1);

        Assert.Null(forced);
    }

    [Fact]
    public void ResolveScenarioRepairForcedToolName_PrefersHostTargetCapableToolsWhenDistinctHostCoverageIsRequired() {
        const string request = """
[Scenario execution contract]
This scenario turn requires tool execution before the final response.
- Minimum tool calls in this turn: 2.
- Distinct tool input value requirements: machine_name>=2.
- Required tool calls (at least one): eventlog_*query*, eventlog_*stats*.
User request:
Continue recurring-error analysis across all remaining DCs in this turn.
""";

        var fileSchema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("path", new JsonObject().Add("type", "string")));
        var hostSchema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var toolDefs = new List<ToolDefinition> {
            new("eventlog_evtx_query", parameters: fileSchema),
            new("eventlog_live_query", parameters: hostSchema)
        };

        var forced = InvokeResolveScenarioRepairForcedToolName(
            userRequest: request,
            calls: new List<ToolCall>(),
            toolDefinitions: toolDefs,
            retryAttempt: 2);

        Assert.Equal("eventlog_live_query", forced);
    }

    [Fact]
    public void PatternMatchesToolName_SupportsQuestionWildcard() {
        var result = InvokePatternMatchesToolName("ad_replication_summar?", "ad_replication_summary");

        Assert.True(result);
    }

    [Fact]
    public void BuildNoToolExecutionRetryPrompt_IncludesKnownHostTargetsHint() {
        var prompt = InvokeBuildNoToolExecutionRetryPrompt(
            userRequest: "Continue on remaining DCs.",
            assistantDraft: string.Empty,
            retryAttempt: 1,
            knownHostTargets: new[] { "AD0", "AD1" });

        Assert.Contains("Known host/DC targets from prior tool inputs in this thread: AD0, AD1.", prompt);
    }

    [Fact]
    public void BuildReadOnlyCallCanonicalIndices_DeduplicatesIdenticalReadOnlyCalls() {
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "dnsclientx_query", """{"name":"contoso.com","type":"MX"}"""),
            BuildToolCall("call_2", "dnsclientx_query", """{"name":"contoso.com","type":"MX"}""")
        };

        var (canonical, deduped) = InvokeBuildReadOnlyCallCanonicalIndices(calls, new HashSet<int>());

        Assert.Equal(new[] { 0, 0 }, canonical);
        Assert.Equal(1, deduped);
    }

    [Fact]
    public void BuildReadOnlyCallCanonicalIndices_DoesNotDeduplicateNonReusableCalls() {
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "dnsclientx_query", """{"name":"contoso.com","type":"MX"}"""),
            BuildToolCall("call_2", "dnsclientx_query", """{"name":"contoso.com","type":"MX"}""")
        };

        var (canonical, deduped) = InvokeBuildReadOnlyCallCanonicalIndices(calls, new HashSet<int> { 1 });

        Assert.Equal(new[] { 0, 1 }, canonical);
        Assert.Equal(0, deduped);
    }

    [Fact]
    public void BuildReadOnlyCallCanonicalIndices_KeepsDistinctArgumentsSeparated() {
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "dnsclientx_query", """{"name":"contoso.com","type":"MX"}"""),
            BuildToolCall("call_2", "dnsclientx_query", """{"name":"contoso.com","type":"A"}""")
        };

        var (canonical, deduped) = InvokeBuildReadOnlyCallCanonicalIndices(calls, new HashSet<int>());

        Assert.Equal(new[] { 0, 1 }, canonical);
        Assert.Equal(0, deduped);
    }

    [Fact]
    public void TryGetSessionToolOutputCacheKey_MatchesPackInfoWithEmptyInput() {
        var call = BuildToolCall("call_1", "AD_Pack_Info", "{}");

        var (matched, cacheKey) = InvokeTryGetSessionToolOutputCacheKey(call);

        Assert.True(matched);
        Assert.Equal("ad_pack_info", cacheKey);
    }

    [Fact]
    public void TryGetSessionToolOutputCacheKey_DoesNotMatchNonPackTools() {
        var call = BuildToolCall("call_1", "ad_monitoring_probe_run", "{}");

        var (matched, cacheKey) = InvokeTryGetSessionToolOutputCacheKey(call);

        Assert.False(matched);
        Assert.Equal(string.Empty, cacheKey);
    }

    [Fact]
    public void TryGetSessionToolOutputCacheKey_DoesNotMatchPackInfoWithArguments() {
        var call = BuildToolCall("call_1", "ad_pack_info", """{"include_tools":true}""");

        var (matched, cacheKey) = InvokeTryGetSessionToolOutputCacheKey(call);

        Assert.False(matched);
        Assert.Equal(string.Empty, cacheKey);
    }

    [Fact]
    public void ShouldCacheSessionToolOutput_RequiresOkTrueEnvelope() {
        var ok = InvokeShouldCacheSessionToolOutput("""{"ok":true,"data":{"name":"ad_pack_info"}}""");
        var failed = InvokeShouldCacheSessionToolOutput("""{"ok":false,"error_code":"not_configured"}""");
        var malformed = InvokeShouldCacheSessionToolOutput("not-json");

        Assert.True(ok);
        Assert.False(failed);
        Assert.False(malformed);
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_FillsTargetAndTargetsWhenMissing() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("target", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject().Add("type", "array")));
        var definition = new ToolDefinition("dnsclientx_ping", parameters: schema);
        var call = BuildToolCall("call_1", "dnsclientx_ping", "{}");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.Equal("AD0.ad.evotec.xyz", repaired.Arguments?.GetString("target"));
        var targets = repaired.Arguments?.GetArray("targets");
        Assert.NotNull(targets);
        Assert.Equal(2, targets!.Count);
        Assert.Equal("AD0.ad.evotec.xyz", targets[0].AsString());
        Assert.Equal("AD1.ad.evotec.xyz", targets[1].AsString());
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_PrefersFqdnWhenShortAndFqdnAreBothAvailable() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("target", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject().Add("type", "array")));
        var definition = new ToolDefinition("dnsclientx_ping", parameters: schema);
        var call = BuildToolCall("call_1", "dnsclientx_ping", "{}");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0", "AD0.ad.evotec.xyz", "localhost" });

        Assert.Equal("AD0.ad.evotec.xyz", repaired.Arguments?.GetString("target"));
        var targets = repaired.Arguments?.GetArray("targets");
        Assert.NotNull(targets);
        Assert.Equal("AD0.ad.evotec.xyz", targets![0].AsString());
    }

}
