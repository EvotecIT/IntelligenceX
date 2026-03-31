using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class HostNoToolRetryHeuristicsTests {
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
    public void ApplyKnownHostTargetFallbacks_AutofillsMachineNameForRemoteSchemas() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string"))
                .Add("log_name", new JsonObject().Add("type", "string")));
        var definition = new ToolDefinition("eventlog_live_query", parameters: schema);
        var call = BuildToolCall("call_1", "eventlog_live_query", """{"log_name":"System"}""");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.NotSame(call, repaired);
        Assert.Equal("System", repaired.Arguments?.GetString("log_name"));
        Assert.Equal("AD0.ad.evotec.xyz", repaired.Arguments?.GetString("machine_name"));
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_DoesNotAutofillLocalOnlyExecutionContractTools() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string"))
                .Add("log_name", new JsonObject().Add("type", "string")));
        var definition = new ToolDefinition(
            "system_local_trace_query",
            parameters: schema,
            execution: new ToolExecutionContract {
                ExecutionScope = ToolExecutionScopes.LocalOnly
            });
        var call = BuildToolCall("call_1", "system_local_trace_query", """{"log_name":"System"}""");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.Same(call, repaired);
        Assert.Null(repaired.Arguments?.GetString("machine_name"));
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_PreservesOriginalScalarTargetKeyWhenOtherAliasesExist() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject()
                    .Add("type", "array")
                    .Add("items", new JsonObject().Add("type", "string")))
                .Add("log_name", new JsonObject().Add("type", "string")));
        var definition = new ToolDefinition("eventlog_live_query", parameters: schema);
        var call = BuildToolCall("call_1", "eventlog_live_query", """{"machine_name":"","targets":[],"log_name":"System"}""");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.NotSame(call, repaired);
        Assert.Equal("System", repaired.Arguments?.GetString("log_name"));
        Assert.Equal("AD0.ad.evotec.xyz", repaired.Arguments?.GetString("machine_name"));

        var targets = repaired.Arguments?.GetArray("targets");
        Assert.Null(targets);
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_PreservesOriginalArrayTargetShape() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("targets", new JsonObject()
                    .Add("type", "array")
                    .Add("items", new JsonObject().Add("type", "string")))
                .Add("machine_name", new JsonObject().Add("type", "string"))
                .Add("log_name", new JsonObject().Add("type", "string")));
        var definition = new ToolDefinition("eventlog_live_query", parameters: schema);
        var call = BuildToolCall("call_1", "eventlog_live_query", """{"targets":[],"log_name":"System"}""");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.NotSame(call, repaired);
        Assert.Equal("System", repaired.Arguments?.GetString("log_name"));
        Assert.Null(repaired.Arguments?.GetString("machine_name"));

        var targetsArray = repaired.Arguments?.GetArray("targets");
        Assert.NotNull(targetsArray);
        Assert.Equal(2, targetsArray!.Count);
        Assert.Equal("AD0.ad.evotec.xyz", targetsArray[0]?.AsString());
        Assert.Equal("AD1.ad.evotec.xyz", targetsArray[1]?.AsString());
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_RemovesConflictingAliasKeysWhenRepairingSelectedTarget() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("target", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject()
                    .Add("type", "array")
                    .Add("items", new JsonObject().Add("type", "string")))
                .Add("query", new JsonObject().Add("type", "string")));
        var definition = new ToolDefinition("dnsclientx_ping", parameters: schema);
        var call = BuildToolCall("call_1", "dnsclientx_ping", """{"target":"","targets":[],"query":"dc"}""");

        var repaired = InvokeApplyKnownHostTargetFallbacks(
            call,
            definition,
            new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz" });

        Assert.Equal("dc", repaired.Arguments?.GetString("query"));
        Assert.Equal("AD0.ad.evotec.xyz", repaired.Arguments?.GetString("target"));
        Assert.Null(repaired.Arguments?.GetArray("targets"));
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
    public void ApplyScenarioDistinctHostCoverageFallbacks_DoesNotPatchLocalOnlyExecutionContractTools() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: system_local_trace_query
distinct_tool_inputs: machine_name>=2
User request:
Continue that trace collection across all remaining hosts in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new(
                "system_local_trace_query",
                parameters: schema,
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                })
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "system_local_trace_query", """{"machine_name":"localhost"}"""),
            BuildToolCall("call_2", "system_local_trace_query", """{"machine_name":"localhost"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "localhost" });

        Assert.Same(calls, repaired);
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_AppendsDerivedCallWhenSingleCallCannotCoverDistinctHosts() {
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
            BuildToolCall("call_1", "eventlog_live_stats", """{"log_name":"System","machine_name":"AD0"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "AD1" });

        Assert.Equal(2, repaired.Count);
        Assert.Equal("call_1", repaired[0].CallId);
        Assert.StartsWith("call_1_hostcov_", repaired[1].CallId, StringComparison.Ordinal);
        Assert.Equal("AD0", repaired[0].Arguments?.GetString("machine_name"));
        Assert.Equal("AD1", repaired[1].Arguments?.GetString("machine_name"));
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
    public void ApplyScenarioDistinctHostCoverageFallbacks_PrefersMostRecentFqdnFallbackTarget() {
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
            knownHostTargets: new[] { "AD0.ad.evotec.xyz", "AD2.ad.evotec.xyz", "localhost" });

        Assert.Equal(2, repaired.Count);
        Assert.Equal("AD2.ad.evotec.xyz", repaired[0].Arguments?.GetString("machine_name"));
        Assert.Equal("localhost", repaired[1].Arguments?.GetString("machine_name"));
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_PrefersRecentTargetOverOlderHigherSpecificityTarget() {
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
            knownHostTargets: new[] { "AD0.ad.evotec.xyz", "AD2", "localhost" });

        Assert.Equal(2, repaired.Count);
        Assert.Equal("AD2", repaired[0].Arguments?.GetString("machine_name"));
        Assert.Equal("localhost", repaired[1].Arguments?.GetString("machine_name"));
    }

    [Fact]
    public void ApplyKnownHostTargetFallbacks_RankingOrderIsDeterministicUnderSoak() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("target", new JsonObject().Add("type", "string"))
                .Add("targets", new JsonObject().Add("type", "array")));

        const int iterations = 128;
        string? baselineTarget = null;
        string[]? baselineTargets = null;
        for (var iteration = 0; iteration < iterations; iteration++) {
            var repaired = InvokeApplyKnownHostTargetFallbacks(
                BuildToolCall("call_1", "dnsclientx_ping", "{}"),
                new ToolDefinition("dnsclientx_ping", parameters: schema),
                new[] { "AD0", "AD0.ad.evotec.xyz", "localhost", "AD1.ad.evotec.xyz", "AD1" });

            var target = repaired.Arguments?.GetString("target");
            Assert.False(string.IsNullOrWhiteSpace(target));

            var targetsArray = repaired.Arguments?.GetArray("targets");
            Assert.Null(targetsArray);

            if (iteration == 0) {
                baselineTarget = target;
                baselineTargets = Array.Empty<string>();
                continue;
            }

            Assert.Equal(baselineTarget, target);
            Assert.Equal(baselineTargets, Array.Empty<string>());
        }
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_RankingOrderIsDeterministicUnderSoak() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*stats*
distinct_tool_inputs: machine_name>=2
forbidden_tool_inputs: machine_name!=AD2
User request:
Continue that failure-signature collection across all remaining non-AD2 DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_stats", parameters: schema)
        };

        const int iterations = 128;
        string[]? baselineHosts = null;
        for (var iteration = 0; iteration < iterations; iteration++) {
            var calls = new List<ToolCall> {
                BuildToolCall("call_1", "eventlog_live_stats", """{"log_name":"System","machine_name":"localhost"}"""),
                BuildToolCall("call_2", "eventlog_live_stats", """{"log_name":"Directory Service","machine_name":"localhost"}""")
            };

            var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
                userRequest: request,
                calls: calls,
                toolDefinitions: definitions,
                knownHostTargets: new[] { "AD0.ad.evotec.xyz", "AD2.ad.evotec.xyz", "AD3", "AD3", "localhost" });
            Assert.Equal(2, repaired.Count);

            var hosts = repaired
                .Select(call => call.Arguments?.GetString("machine_name") ?? string.Empty)
                .ToArray();
            Assert.DoesNotContain(hosts, host => string.Equals(host, "AD2", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(hosts, host => host.StartsWith("AD2.", StringComparison.OrdinalIgnoreCase));

            if (iteration == 0) {
                baselineHosts = hosts;
                continue;
            }

            Assert.Equal(baselineHosts, hosts);
        }
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
    public void ApplyScenarioDistinctHostCoverageFallbacks_PrefersMostRecentAllowedTargetAfterForbiddenAndDuplicateFiltering() {
        const string request = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
requires_no_tool_execution: false
min_tool_calls: 2
required_tools_all: none
required_tools_any: eventlog_*stats*
distinct_tool_inputs: machine_name>=2
forbidden_tool_inputs: machine_name!=AD2
User request:
Continue that failure-signature collection across all remaining non-AD2 DCs in this turn.
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
            knownHostTargets: new[] { "AD0.ad.evotec.xyz", "AD2.ad.evotec.xyz", "AD2.ad.evotec.xyz", "AD3", "localhost" });

        Assert.Equal(2, repaired.Count);
        Assert.Equal("AD3", repaired[0].Arguments?.GetString("machine_name"));
        Assert.Equal("localhost", repaired[1].Arguments?.GetString("machine_name"));
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_ReplacesForbiddenHostTarget() {
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
Continue that failure-signature collection across all remaining non-AD0 DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_query", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", """{"machine_name":"AD0","log_name":"System"}"""),
            BuildToolCall("call_2", "eventlog_live_query", """{"machine_name":"AD1","log_name":"System"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "AD1", "AD2" });

        Assert.Equal(2, repaired.Count);
        var hosts = repaired
            .Select(call => call.Arguments?.GetString("machine_name") ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.DoesNotContain(hosts, host => string.Equals(host, "AD0", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hosts, host => string.Equals(host, "AD2", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_DoesNotUseForbiddenFallbackTargetForDerivedCalls() {
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
Continue that failure-signature collection across all remaining non-AD0 DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_query", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", """{"machine_name":"AD1","log_name":"System"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0", "AD1", "AD2" });

        Assert.Equal(2, repaired.Count);
        var derivedHost = repaired[1].Arguments?.GetString("machine_name");
        Assert.Equal("AD2", derivedHost);
        Assert.False(string.Equals("AD0", derivedHost, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApplyScenarioDistinctHostCoverageFallbacks_TreatsShortForbiddenHostAsMatchingFqdnCandidates() {
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
Continue that failure-signature collection across all remaining non-AD0 DCs in this turn.
""";
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("machine_name", new JsonObject().Add("type", "string")));
        var definitions = new List<ToolDefinition> {
            new("eventlog_live_query", parameters: schema)
        };
        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", """{"machine_name":"AD1.ad.evotec.xyz","log_name":"System"}""")
        };

        var repaired = InvokeApplyScenarioDistinctHostCoverageFallbacks(
            userRequest: request,
            calls: calls,
            toolDefinitions: definitions,
            knownHostTargets: new[] { "AD0.ad.evotec.xyz", "AD1.ad.evotec.xyz", "AD2.ad.evotec.xyz" });

        Assert.Equal(2, repaired.Count);
        var hosts = repaired
            .Select(call => call.Arguments?.GetString("machine_name") ?? string.Empty)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.DoesNotContain(hosts, host => host.StartsWith("AD0.", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hosts, host => string.Equals(host, "AD2.ad.evotec.xyz", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public async Task WaitForToolOutputWithTimeoutAsync_ReturnsTimedOutForNonCompletingTask() {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        var result = await InvokeWaitForToolOutputWithTimeoutAsync(
            pending.Task,
            timeoutSeconds: 1,
            cancellationToken: CancellationToken.None);

        Assert.True(result.timedOut);
        Assert.Null(result.output);
    }

    [Fact]
    public async Task WaitForToolOutputWithTimeoutAsync_ReturnsOutputWhenTaskCompletesWithinTimeout() {
        var result = await InvokeWaitForToolOutputWithTimeoutAsync(
            Task.FromResult("""{"ok":true}"""),
            timeoutSeconds: 2,
            cancellationToken: CancellationToken.None);

        Assert.False(result.timedOut);
        Assert.Equal("""{"ok":true}""", result.output);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_SynthesizesSummaryFromExecutedToolOutputs() {
        var calls = new[] {
            BuildToolCall("call_1", "dnsclientx_query", """{"name":"fabrikam.com","type":"NS"}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"summary_markdown":"DNS NS records look consistent across resolvers."}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: string.Empty,
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("dnsclientx_query", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DNS NS records look consistent", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextToolOutputRetryPromptForTesting_IncludesCompactCallArgumentsInEvidence() {
        var calls = new[] {
            BuildToolCall("call_1", "eventlog_live_query", """{"machine_name":"AD1.ad.evotec.xyz","log_name":"System","max_events":200,"event_ids":[41,6008]}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"summary_markdown":"No reboot markers found in selected UTC window."}""")
        };

        var prompt = InvokeBuildNoTextToolOutputRetryPromptForTesting(
            userRequest: "Compare non-AD0 DC reboot evidence.",
            toolCalls: calls,
            toolOutputs: outputs);

        Assert.Contains("eventlog_live_query", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("args: machine_name=AD1.ad.evotec.xyz", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_events=200", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No reboot markers found", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_ReturnsWarningWhenNoToolOutputsExist() {
        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: string.Empty,
            toolCalls: Array.Empty<ToolCall>(),
            toolOutputs: Array.Empty<ToolOutput>(),
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("No response text was produced", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_IncludesLocalOnlyToolingWarning() {
        var toolDefinitions = new[] {
            new ToolDefinition(
                name: "system_local_trace_query",
                description: "Inspect local traces only.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Host label."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOnly
                })
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: string.Empty,
            toolCalls: Array.Empty<ToolCall>(),
            toolOutputs: Array.Empty<ToolOutput>(),
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null,
            toolDefinitions: toolDefinitions,
            knownHostTargets: new[] { "AD1", "AD1", "AD0" });

        Assert.Contains("Tool locality:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-only in this session", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Known host/DC targets from prior tool inputs in this thread (ordered distinct candidates): AD1, AD0.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_IncludesRemoteReadyToolingWarning() {
        var toolDefinitions = new[] {
            new ToolDefinition(
                name: "eventlog_live_query",
                description: "Query remote event logs.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                execution: new ToolExecutionContract {
                    ExecutionScope = ToolExecutionScopes.LocalOrRemote
                })
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: string.Empty,
            toolCalls: Array.Empty<ToolCall>(),
            toolOutputs: Array.Empty<ToolOutput>(),
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null,
            toolDefinitions: toolDefinitions,
            knownHostTargets: new[] { "AD0" });

        Assert.Contains("Tool locality:", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remote-ready tools are available", text, StringComparison.OrdinalIgnoreCase);
    }

}
