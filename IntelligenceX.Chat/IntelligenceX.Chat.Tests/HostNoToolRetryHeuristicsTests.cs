using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
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

    [Fact]
    public void ApplyKnownHostTargetFallbacks_DoesNotOverrideExplicitTargetInputs() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("target", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject().Add("type", "array")));
        var definition = new ToolDefinition("dnsclientx_ping", parameters: schema);
        var call = BuildToolCall("call_1", "dnsclientx_ping", """{"target":"explicit.dc","targets":["explicit.dc"]}""");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.Same(call, repaired);
        Assert.Equal("explicit.dc", repaired.Arguments?.GetString("target"));
        var targets = repaired.Arguments?.GetArray("targets");
        Assert.NotNull(targets);
        Assert.Single(targets!);
        Assert.Equal("explicit.dc", targets[0].AsString());
    }

    [Fact]
    public void ApplyAdDiscoveryRootDseFallback_UnpinsDomainControllerAfterRootDseFailure() {
        var call = BuildToolCall(
            "call_1",
            "ad_environment_discover",
            """{"domain_controller":"AD0.ad.evotec.xyz","search_base_dn":"DC=ad,DC=evotec,DC=xyz","include_domain_controllers":true}""");
        const string output = """{"ok":false,"error_code":"not_configured","error":"Failed to read RootDSE from 'AD0.ad.evotec.xyz'."}""";

        var repaired = InvokeApplyAdDiscoveryRootDseFallback(call, output);

        Assert.NotSame(call, repaired);
        Assert.Equal(string.Empty, repaired.Arguments?.GetString("domain_controller"));
        Assert.Equal("DC=ad,DC=evotec,DC=xyz", repaired.Arguments?.GetString("search_base_dn"));
    }

    [Fact]
    public void ApplyAdDiscoveryRootDseFallback_DoesNotPatchWhenFailureIsUnrelated() {
        var call = BuildToolCall(
            "call_1",
            "ad_environment_discover",
            """{"domain_controller":"AD0.ad.evotec.xyz","search_base_dn":"DC=ad,DC=evotec,DC=xyz","include_domain_controllers":true}""");
        const string output = """{"ok":false,"error_code":"timeout","error":"Replication query timed out after 0:00:07."}""";

        var repaired = InvokeApplyAdDiscoveryRootDseFallback(call, output);

        Assert.Same(call, repaired);
    }

    [Fact]
    public void ApplyAdDiscoveryRootDseFallback_DoesNotPatchForNonAdDiscoveryTools() {
        var call = BuildToolCall(
            "call_1",
            "eventlog_live_query",
            """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System"}""");
        const string output = """{"ok":false,"error_code":"not_configured","error":"Failed to read RootDSE from 'AD0.ad.evotec.xyz'."}""";

        var repaired = InvokeApplyAdDiscoveryRootDseFallback(call, output);

        Assert.Same(call, repaired);
    }

    [Fact]
    public void ApplyAdReplicationProbeFallback_ExtendsTimeoutAndPromotesFqdnTargets() {
        var call = BuildToolCall(
            "call_1",
            "ad_monitoring_probe_run",
            """{"probe_kind":"replication","domain_controller":"AD2","targets":["AD2"],"timeout_ms":5000}""");
        const string output = """{"ok":false,"error_code":"timeout","error":"Replication query timed out after 0:00:05."}""";

        var repaired = InvokeApplyAdReplicationProbeFallback(
            call: call,
            output: output,
            knownHostTargets: new[] { "AD2.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.NotSame(call, repaired);
        Assert.Equal(10000L, repaired.Arguments?.GetInt64("timeout_ms"));
        Assert.Equal("AD2.ad.evotec.xyz", repaired.Arguments?.GetString("domain_controller"));
        var targets = repaired.Arguments?.GetArray("targets");
        Assert.NotNull(targets);
        Assert.Single(targets!);
        Assert.Equal("AD2.ad.evotec.xyz", targets[0].AsString());
    }

    [Fact]
    public void ApplyAdReplicationProbeFallback_PromotesFqdnOnNoDataFailure() {
        var call = BuildToolCall(
            "call_1",
            "ad_monitoring_probe_run",
            """{"probe_kind":"replication","domain_controller":"AD1","targets":["AD1"]}""");
        const string output = """{"ok":false,"error":"No replication data returned (domain=ad.evotec.xyz; explicitDCs=AD1; preferredDC=; ldapFallback=True; cred=False)."}""";

        var repaired = InvokeApplyAdReplicationProbeFallback(
            call: call,
            output: output,
            knownHostTargets: new[] { "AD1.ad.evotec.xyz", "AD2.ad.evotec.xyz" });

        Assert.NotSame(call, repaired);
        Assert.Equal("AD1.ad.evotec.xyz", repaired.Arguments?.GetString("domain_controller"));
        var targets = repaired.Arguments?.GetArray("targets");
        Assert.NotNull(targets);
        Assert.Equal("AD1.ad.evotec.xyz", targets![0].AsString());
    }

    [Fact]
    public void ApplyAdReplicationProbeFallback_DoesNotPatchNonReplicationProbeCalls() {
        var call = BuildToolCall(
            "call_1",
            "ad_monitoring_probe_run",
            """{"probe_kind":"ldap","domain_controller":"AD2","timeout_ms":5000}""");
        const string output = """{"ok":false,"error_code":"timeout","error":"Replication query timed out after 0:00:05."}""";

        var repaired = InvokeApplyAdReplicationProbeFallback(
            call: call,
            output: output,
            knownHostTargets: new[] { "AD2.ad.evotec.xyz" });

        Assert.Same(call, repaired);
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_PatchesCallsWhenDistinctMachineCoverageMissing() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*stats*
distinct_tool_inputs: machine_name>=2
User request:
Continue that failure-signature collection across all remaining DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_stats", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_stats", """{"log_name":"System","machine_name":"localhost"}"""),
            BuildToolCall("call_2", "eventlog_live_stats", """{"log_name":"Directory Service","machine_name":"localhost"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "localhost" });

        Assert.Equal(2, repaired.Count);
        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < repaired.Count; i++) {
            var machineName = repaired[i].Arguments?.GetString("machine_name");
            if (!string.IsNullOrWhiteSpace(machineName)) {
                distinct.Add(machineName);
            }
        }

        Assert.Equal(2, distinct.Count);
        Assert.Contains("localhost", distinct);
        Assert.Contains("AD0", distinct);
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_KeepsHostAliasesConsistentWithinPatchedCall() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*stats*
distinct_tool_inputs: machine_name>=2
User request:
Continue that failure-signature collection across all remaining DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string"))
                .Add("domain_controller", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject()
                    .Add("type", "array")
                    .Add("items", new JsonObject().Add("type", "string"))));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_stats", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall(
                "call_1",
                "eventlog_live_stats",
                """{"log_name":"System","machine_name":"localhost","domain_controller":"localhost","targets":["localhost"]}"""),
            BuildToolCall(
                "call_2",
                "eventlog_live_stats",
                """{"log_name":"Directory Service","machine_name":"localhost"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "localhost" });

        Assert.Equal(2, repaired.Count);
        Assert.Equal("AD0", repaired[0].Arguments?.GetString("machine_name"));
        Assert.Equal("AD0", repaired[0].Arguments?.GetString("domain_controller"));

        var targets = repaired[0].Arguments?.GetArray("targets");
        Assert.NotNull(targets);
        Assert.Equal(1, targets!.Count);
        Assert.Equal("AD0", targets[0]?.AsString());
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_PrefersFqdnFallbackTarget() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*stats*
distinct_tool_inputs: machine_name>=2
User request:
Continue that failure-signature collection across all remaining DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_stats", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_stats", """{"log_name":"System","machine_name":"localhost"}"""),
            BuildToolCall("call_2", "eventlog_live_stats", """{"log_name":"Directory Service","machine_name":"localhost"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "AD0.ad.evotec.xyz", "localhost" });

        Assert.Equal(2, repaired.Count);
        Assert.Equal("AD0.ad.evotec.xyz", repaired[0].Arguments?.GetString("machine_name"));
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_DoesNotPatchWhenDistinctMachineCoverageIsAlreadyMet() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*stats*
distinct_tool_inputs: machine_name>=2
User request:
Continue that failure-signature collection across all remaining DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_stats", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_stats", """{"log_name":"System","machine_name":"AD0"}"""),
            BuildToolCall("call_2", "eventlog_live_stats", """{"log_name":"Directory Service","machine_name":"localhost"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "localhost" });

        Assert.Same(calls, repaired);
    }

    [Fact]
    public void ReplSession_HostTargetRetentionCapacity_IsSizedForLongContinuationRuns() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var recentCapacityField = replSessionType!.GetField(
            "MaxRecentHostTargets",
            BindingFlags.NonPublic | BindingFlags.Static);
        var promptCapacityField = replSessionType.GetField(
            "MaxRetryPromptHostTargets",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(recentCapacityField);
        Assert.NotNull(promptCapacityField);

        var recentCapacity = Assert.IsType<int>(recentCapacityField!.GetRawConstantValue());
        var promptCapacity = Assert.IsType<int>(promptCapacityField!.GetRawConstantValue());

        Assert.True(recentCapacity >= 64, $"Expected MaxRecentHostTargets >= 64 but found {recentCapacity}.");
        Assert.True(promptCapacity >= 12, $"Expected MaxRetryPromptHostTargets >= 12 but found {promptCapacity}.");
    }

    [Fact]
    public void ShouldRetryModelPhaseAttempt_RetriesOnProviderServerErrorMessage() {
        var ex = new InvalidOperationException(
            "The server had an error processing your request. Please include the request ID 123.");

        var result = InvokeShouldRetryModelPhaseAttempt(
            ex: ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public void ShouldRetryModelPhaseAttempt_DoesNotRetryAuthenticationErrors() {
        var ex = new OpenAIAuthenticationRequiredException("Not logged in.");

        var result = InvokeShouldRetryModelPhaseAttempt(
            ex: ex,
            attempt: 0,
            maxAttempts: 2,
            cancellationToken: CancellationToken.None);

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

    private static string? InvokeResolveScenarioRepairForcedToolName(
        string userRequest,
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        int retryAttempt) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ResolveScenarioRepairForcedToolName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, calls, toolDefinitions, retryAttempt });
        return value as string;
    }

    private static bool InvokePatternMatchesToolName(string pattern, string toolName) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "PatternMatchesToolName",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { pattern, toolName });
        return value is bool b && b;
    }

    private static string InvokeBuildNoToolExecutionRetryPrompt(
        string userRequest,
        string assistantDraft,
        int retryAttempt,
        IReadOnlyList<string> knownHostTargets) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "BuildNoToolExecutionRetryPrompt",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, assistantDraft, retryAttempt, knownHostTargets });
        return Assert.IsType<string>(value);
    }

    private static bool InvokeShouldRetryModelPhaseAttempt(
        Exception ex,
        int attempt,
        int maxAttempts,
        CancellationToken cancellationToken) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ShouldRetryModelPhaseAttempt",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { ex, attempt, maxAttempts, cancellationToken });
        return value is bool b && b;
    }

    private static ToolCall InvokeApplyKnownHostTargetFallbacks(
        ToolCall call,
        ToolDefinition definition,
        IReadOnlyList<string> knownHostTargets) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ApplyKnownHostTargetFallbacks",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { call, definition, knownHostTargets });
        return Assert.IsType<ToolCall>(value);
    }

    private static ToolCall InvokeApplyAdDiscoveryRootDseFallback(ToolCall call, string output) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ApplyAdDiscoveryRootDseFallback",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { call, output });
        return Assert.IsType<ToolCall>(value);
    }

    private static ToolCall InvokeApplyAdReplicationProbeFallback(
        ToolCall call,
        string output,
        IReadOnlyList<string>? knownHostTargets) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ApplyAdReplicationProbeFallback",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { call, output, knownHostTargets });
        return Assert.IsType<ToolCall>(value);
    }

    private static IReadOnlyList<ToolCall> InvokeApplyScenarioDistinctHostCoverageFallbacks(
        string userRequest,
        IReadOnlyList<ToolCall> calls,
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<string> knownHostTargets) {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var replSessionType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplSession", throwOnError: true);
        Assert.NotNull(replSessionType);

        var method = replSessionType!.GetMethod(
            "ApplyScenarioDistinctHostCoverageFallbacks",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new object?[] { userRequest, calls, toolDefinitions, knownHostTargets });
        return Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(value);
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
