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
    public void BuildNoTextReplFallbackTextForTesting_ReplacesEventLogDraftThatStillRequestsMachineName() {
        var calls = new[] {
            BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System","max_events":10}"""),
            BuildToolCall("call_2", "eventlog_top_events", """{"machine_name":"ADRODC.ad.evotec.pl","log_name":"System","max_events":10}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"log_name":"System","count":10,"truncated":true,"events":[{"machine_name":"AD0.ad.evotec.xyz"}],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"AD0.ad.evotec.xyz","query_mode":"top_events","rows":10,"truncated":true}}"""),
            new ToolOutput("call_2", """{"ok":true,"log_name":"System","count":0,"truncated":false,"events":[],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"ADRODC.ad.evotec.pl","query_mode":"top_events","rows":0,"truncated":false}}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: "Necesito los nombres de al menos dos DCs concretos en `machine_name` para poder recoger evidencia.",
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered EventLog findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 2 distinct DC target(s)", text, StringComparison.Ordinal);
        Assert.Contains("AD0.ad.evotec.xyz", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADRODC.ad.evotec.pl", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Necesito los nombres", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_SummarizesAllStructuredEventLogHostsWithoutGenericTail() {
        var calls = new[] {
            BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"DC1.ad.evotec.pl","log_name":"System","max_events":10}"""),
            BuildToolCall("call_2", "eventlog_top_events", """{"machine_name":"ADRODC.ad.evotec.pl","log_name":"System","max_events":10}"""),
            BuildToolCall("call_3", "eventlog_top_events", """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System","max_events":10}"""),
            BuildToolCall("call_4", "eventlog_top_events", """{"machine_name":"AD1.ad.evotec.xyz","log_name":"System","max_events":10}"""),
            BuildToolCall("call_5", "eventlog_top_events", """{"machine_name":"ad2.ad.evotec.xyz","log_name":"System","max_events":10}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"log_name":"System","count":10,"truncated":true,"events":[{"machine_name":"DC1.ad.evotec.pl"}],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"DC1.ad.evotec.pl","query_mode":"top_events","rows":10,"truncated":true}}"""),
            new ToolOutput("call_2", """{"ok":true,"log_name":"System","count":0,"truncated":false,"events":[],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"ADRODC.ad.evotec.pl","query_mode":"top_events","rows":0,"truncated":false}}"""),
            new ToolOutput("call_3", """{"ok":true,"log_name":"System","count":10,"truncated":true,"events":[{"machine_name":"AD0.ad.evotec.xyz"}],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"AD0.ad.evotec.xyz","query_mode":"top_events","rows":10,"truncated":true}}"""),
            new ToolOutput("call_4", """{"ok":true,"log_name":"System","count":10,"truncated":true,"events":[{"machine_name":"AD1.ad.evotec.xyz"}],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"AD1.ad.evotec.xyz","query_mode":"top_events","rows":10,"truncated":true}}"""),
            new ToolOutput("call_5", """{"ok":true,"log_name":"System","count":10,"truncated":true,"events":[{"machine_name":"ad2.ad.evotec.xyz"}],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"ad2.ad.evotec.xyz","query_mode":"top_events","rows":10,"truncated":true}}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: string.Empty,
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered EventLog findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 5 distinct DC target(s) (4 returned rows, 1 returned 0 rows).", text, StringComparison.Ordinal);
        Assert.Contains("DC1.ad.evotec.pl", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ADRODC.ad.evotec.pl", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD0.ad.evotec.xyz", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AD1.ad.evotec.xyz", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad2.ad.evotec.xyz", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("more tool output", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_ReplacesMissingInputDraftAfterAdEventPlatformFallbackExecutes() {
        var calls = new[] {
            BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System","max_events":10}"""),
            BuildToolCall("call_2", "eventlog_top_events", """{"machine_name":"ad1.ad.evotec.xyz","log_name":"System","max_events":10}"""),
            BuildToolCall("call_3", "system_connectivity_probe", """{"computer_name":"AD0.ad.evotec.xyz","include_time_sync":true}"""),
            BuildToolCall("call_4", "system_windows_update_telemetry", """{"computer_name":"AD0.ad.evotec.xyz","include_event_telemetry":false}"""),
            BuildToolCall("call_5", "system_connectivity_probe", """{"computer_name":"ad1.ad.evotec.xyz","include_time_sync":true}"""),
            BuildToolCall("call_6", "system_windows_update_telemetry", """{"computer_name":"ad1.ad.evotec.xyz","include_event_telemetry":false}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":false,"error_code":"platform_not_supported","error":"Remote event log query failed for log 'System' on machine 'AD0.ad.evotec.xyz'. Reason: EventLog access is not supported on this platform."}"""),
            new ToolOutput("call_2", """{"ok":false,"error_code":"platform_not_supported","error":"Remote event log query failed for log 'System' on machine 'ad1.ad.evotec.xyz'. Reason: EventLog access is not supported on this platform."}"""),
            new ToolOutput("call_3", """{"ok":true,"computer_name":"AD0.ad.evotec.xyz","probe_status":"healthy","time_sync_probe_succeeded":true,"computer_system":{"domain":"ad.evotec.xyz"},"time_sync":{"time_skew_seconds":0.536},"summary_markdown":"### System connectivity probe\r\n\r\n| Field | Value |"}"""),
            new ToolOutput("call_4", """{"ok":true,"computer_name":"AD0.ad.evotec.xyz","is_pending_reboot":true,"coverage_state":"NoUpdateSignals","detection_missing":true,"wsus_decision":"Unknown","summary_markdown":"### Windows Update telemetry\r\n\r\n| Metric | Value |"}"""),
            new ToolOutput("call_5", """{"ok":true,"computer_name":"ad1.ad.evotec.xyz","probe_status":"healthy","time_sync_probe_succeeded":true,"computer_system":{"domain":"ad.evotec.xyz"},"time_sync":{"time_skew_seconds":0.544},"summary_markdown":"### System connectivity probe\r\n\r\n| Field | Value |"}"""),
            new ToolOutput("call_6", """{"ok":true,"computer_name":"ad1.ad.evotec.xyz","is_pending_reboot":false,"coverage_state":"NoUpdateSignals","detection_missing":true,"wsus_decision":"Unknown","summary_markdown":"### Windows Update telemetry\r\n\r\n| Metric | Value |"}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: "Missing input once: at least two concrete DC machine_name values are required for Event Log collection.",
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered AD EventLog fallback findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 2 distinct DC target(s); EventLog blocked on this runtime for 2 target(s).", text, StringComparison.Ordinal);
        Assert.Contains("AD0.ad.evotec.xyz", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad1.ad.evotec.xyz", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connectivity healthy; domain ad.evotec.xyz; time sync ok; time skew 0.536s", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending reboot yes; coverage NoUpdateSignals; detection missing yes; WSUS Unknown", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pending reboot no; coverage NoUpdateSignals; detection missing yes; WSUS Unknown", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("### System connectivity probe", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("| Field | Value |", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("### Windows Update telemetry", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Missing input once", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_ReplacesAdMonitoringDraftThatClaimsRowsWereMissing() {
        var calls = new[] {
            BuildToolCall("call_1", "ad_monitoring_probe_run", """{"probe_kind":"ldap","targets":["AD0.ad.evotec.xyz","ad1.ad.evotec.xyz"]}"""),
            BuildToolCall("call_2", "ad_monitoring_probe_run", """{"probe_kind":"adws","targets":["AD0.ad.evotec.xyz","ad1.ad.evotec.xyz"]}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"probe_kind":"ldap","probe_result":{"status":"Down","completed_utc":"2026-04-08T18:25:55.6542797+00:00","children":[{"target":"AD0.ad.evotec.xyz","status":"Down","error":"System.DirectoryServices.Protocols is not supported on this platform."},{"target":"ad1.ad.evotec.xyz","status":"Down","error":"System.DirectoryServices.Protocols is not supported on this platform."}]}}"""),
            new ToolOutput("call_2", """{"ok":true,"probe_kind":"adws","probe_result":{"status":"Up","completed_utc":"2026-04-08T18:25:55.5433420+00:00","children":[{"target":"AD0.ad.evotec.xyz","status":"Up"},{"target":"ad1.ad.evotec.xyz","status":"Up"}]}}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: "The actual row values were not provided, so health state per DC cannot be confirmed.",
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered AD monitoring findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 2 distinct DC target(s).", text, StringComparison.Ordinal);
        Assert.Contains("ldap: overall Down; targets 2; completed 2026-04-08T18:25:55.6542797+00:00; AD0.ad.evotec.xyz=Down", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad1.ad.evotec.xyz=Down", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adws: overall Up; targets 2; completed 2026-04-08T18:25:55.5433420+00:00; AD0.ad.evotec.xyz=Up, ad1.ad.evotec.xyz=Up", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("actual row values were not provided", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_ReplacesAdMonitoringDraftThatOmitsCoverageTargets() {
        var calls = new[] {
            BuildToolCall("call_1", "ad_monitoring_probe_run", """{"probe_kind":"ldap","targets":["AD0.ad.evotec.xyz","ad1.ad.evotec.xyz"]}"""),
            BuildToolCall("call_2", "ad_monitoring_probe_run", """{"probe_kind":"replication","targets":["AD0.ad.evotec.xyz","ad1.ad.evotec.xyz"]}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"probe_kind":"ldap","probe_result":{"status":"Up","completed_utc":"2026-04-08T18:40:00+00:00","children":[{"target":"AD0.ad.evotec.xyz","status":"Up"},{"target":"ad1.ad.evotec.xyz","status":"Up"}]}}"""),
            new ToolOutput("call_2", """{"ok":true,"probe_kind":"replication","probe_result":{"status":"Warning","completed_utc":"2026-04-08T18:41:00+00:00","children":[{"target":"AD0.ad.evotec.xyz","status":"Up"},{"target":"ad1.ad.evotec.xyz","status":"Warning","error":"backlog detected"}]}}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: "Con la evidencia entregada no se puede confirmar estado final por host. Faltan las filas de resultado.",
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered AD monitoring findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 2 distinct DC target(s).", text, StringComparison.Ordinal);
        Assert.Contains("ldap: overall Up; targets 2; completed 2026-04-08T18:40:00+00:00; AD0.ad.evotec.xyz=Up, ad1.ad.evotec.xyz=Up", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replication: overall Warning; targets 2; completed 2026-04-08T18:41:00+00:00; AD0.ad.evotec.xyz=Up, ad1.ad.evotec.xyz=Warning (backlog detected)", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("no se puede confirmar estado final por host", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_ReplacesAdMonitoringDraftThatMentionsTargetsWithoutRowFacts() {
        var calls = new[] {
            BuildToolCall("call_1", "ad_monitoring_probe_run", """{"probe_kind":"ldap","targets":["ad0.ad.evotec.xyz","AD1.ad.evotec.xyz","ad2.ad.evotec.xyz"]}"""),
            BuildToolCall("call_2", "ad_monitoring_probe_run", """{"probe_kind":"adws","targets":["ad0.ad.evotec.xyz","AD1.ad.evotec.xyz","ad2.ad.evotec.xyz"]}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"probe_kind":"ldap","probe_result":{"status":"Down","completed_utc":"2026-04-08T18:52:48.1632876+00:00","children":[{"target":"ad0.ad.evotec.xyz","status":"Down","error":"ldap unsupported"},{"target":"AD1.ad.evotec.xyz","status":"Down","error":"ldap unsupported"},{"target":"ad2.ad.evotec.xyz","status":"Down","error":"ldap unsupported"}]}}"""),
            new ToolOutput("call_2", """{"ok":true,"probe_kind":"adws","probe_result":{"status":"Up","completed_utc":"2026-04-08T18:52:47.9237132+00:00","children":[{"target":"ad0.ad.evotec.xyz","status":"Up"},{"target":"AD1.ad.evotec.xyz","status":"Up"},{"target":"ad2.ad.evotec.xyz","status":"Up"}]}}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: """
                Validación ejecutada sobre:
                - ad0.ad.evotec.xyz
                - AD1.ad.evotec.xyz
                - ad2.ad.evotec.xyz

                No se puede concluir estado saludable o degradado por host porque la evidencia recuperada no incluye las filas de resultado.
                """,
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered AD monitoring findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 3 distinct DC target(s).", text, StringComparison.Ordinal);
        Assert.Contains("ldap: overall Down; targets 3; completed 2026-04-08T18:52:48.1632876+00:00", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adws: overall Up; targets 3; completed 2026-04-08T18:52:47.9237132+00:00", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; ad.evotec.xyz=Down", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(", ad.evotec.xyz=Down", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; 3 targets=Up", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(", 3 targets=Up", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No se puede concluir estado saludable o degradado por host", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextReplFallbackTextForTesting_ReplacesAdMonitoringBoundarySummaryThatClaimsPerDcRowsWereMissing() {
        var calls = new[] {
            BuildToolCall("call_1", "ad_monitoring_probe_run", """{"probe_kind":"ldap","targets":["ad0.ad.evotec.xyz","AD1.ad.evotec.xyz","ad2.ad.evotec.xyz"]}"""),
            BuildToolCall("call_2", "ad_monitoring_probe_run", """{"probe_kind":"adws","targets":["ad0.ad.evotec.xyz","AD1.ad.evotec.xyz","ad2.ad.evotec.xyz"]}"""),
            BuildToolCall("call_3", "ad_monitoring_probe_run", """{"probe_kind":"replication","targets":["ad0.ad.evotec.xyz","AD1.ad.evotec.xyz","ad2.ad.evotec.xyz"]}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"probe_kind":"ldap","probe_result":{"status":"Down","completed_utc":"2026-04-08T19:25:09.2874642+00:00","children":[{"target":"ad0.ad.evotec.xyz","status":"Down","error":"ldap unsupported"},{"target":"AD1.ad.evotec.xyz","status":"Down","error":"ldap unsupported"},{"target":"ad2.ad.evotec.xyz","status":"Down","error":"ldap unsupported"}]}}"""),
            new ToolOutput("call_2", """{"ok":true,"probe_kind":"adws","probe_result":{"status":"Up","completed_utc":"2026-04-08T19:25:09.1888368+00:00","children":[{"target":"ad0.ad.evotec.xyz","status":"Up"},{"target":"AD1.ad.evotec.xyz","status":"Up"},{"target":"ad2.ad.evotec.xyz","status":"Up"}]}}"""),
            new ToolOutput("call_3", """{"ok":true,"probe_kind":"replication","probe_result":{"status":"Degraded","completed_utc":"2026-04-08T19:25:39.9704542+00:00","children":[{"target":"ad0.ad.evotec.xyz","status":"Up"},{"target":"AD1.ad.evotec.xyz","status":"Degraded","error":"Skipped: previous replication probe still running."},{"target":"ad2.ad.evotec.xyz","status":"Up"}]}}""")
        };

        var text = InvokeBuildNoTextReplFallbackTextForTesting(
            assistantDraft: """
                **AD**

                **Evidence references**
                - `ad_monitoring_probe_run`

                **Findings**
                - Later **AD** monitoring covered three DC targets:
                 - `ad0.ad.evotec.xyz`
                 - `AD1.ad.evotec.xyz`
                 - `ad2.ad.evotec.xyz`
                - LDAP, ADWS, and replication probes were executed across that discovered scope

                **Unresolved blockers**
                - No per-DC result rows were returned for LDAP, ADWS, or replication
                - No per-DC statuses, errors, latencies, or detailed results were returned
                """,
            toolCalls: calls,
            toolOutputs: outputs,
            model: "gpt-test",
            transport: OpenAITransportKind.Native,
            baseUrl: null);

        Assert.Contains("Recovered AD monitoring findings from executed tools", text, StringComparison.Ordinal);
        Assert.Contains("Coverage: 3 distinct DC target(s).", text, StringComparison.Ordinal);
        Assert.Contains("ldap: overall Down; targets 3; completed 2026-04-08T19:25:09.2874642+00:00", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adws: overall Up; targets 3; completed 2026-04-08T19:25:09.1888368+00:00", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replication: overall Degraded; targets 3; completed 2026-04-08T19:25:39.9704542+00:00", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No per-DC result rows were returned", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No per-DC statuses, errors, latencies, or detailed results were returned", text, StringComparison.OrdinalIgnoreCase);
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
    public void BuildNoTextToolOutputRetryPromptForTesting_IncludesDistinctTargetCoverageAndDoesNotTrimLaterHosts() {
        var calls = new[] {
            BuildToolCall("call_1", "dnsclientx_ping", """{"target":"contoso-com.mail.protection.outlook.com"}"""),
            BuildToolCall("call_2", "dnsclientx_ping", """{"target":"ns1-205.azure-dns.com"}"""),
            BuildToolCall("call_3", "dnsclientx_ping", """{"target":"ns2-205.azure-dns.net"}"""),
            BuildToolCall("call_4", "dnsclientx_ping", """{"target":"ns3-205.azure-dns.org"}"""),
            BuildToolCall("call_5", "dnsclientx_ping", """{"target":"ns4-205.azure-dns.info"}"""),
            BuildToolCall("call_6", "domaindetective_network_probe", """{"host":"ns4-205.azure-dns.info","run_ping":true}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"summary_markdown":"MX host ping succeeded."}"""),
            new ToolOutput("call_2", """{"ok":true,"summary_markdown":"NS1 timed out on ICMP."}"""),
            new ToolOutput("call_3", """{"ok":true,"summary_markdown":"NS2 timed out on ICMP."}"""),
            new ToolOutput("call_4", """{"ok":true,"summary_markdown":"NS3 timed out on ICMP."}"""),
            new ToolOutput("call_5", """{"ok":true,"summary_markdown":"NS4 timed out on ICMP."}"""),
            new ToolOutput("call_6", """{"ok":true,"summary_markdown":"Probe confirmed the same timeout posture for NS4."}""")
        };

        var prompt = InvokeBuildNoTextToolOutputRetryPromptForTesting(
            userRequest: "Continue network and resolver checks for all remaining discovered hosts in this turn.",
            toolCalls: calls,
            toolOutputs: outputs);

        Assert.Contains("Executed distinct target coverage:", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contoso-com.mail.protection.outlook.com", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ns1-205.azure-dns.com", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ns2-205.azure-dns.net", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ns3-205.azure-dns.org", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ns4-205.azure-dns.info", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NS4 timed out on ICMP.", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Probe confirmed the same timeout posture for NS4.", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNoTextToolOutputRetryPromptForTesting_CondensesMixedEventLogPreviewEvidence() {
        var calls = new[] {
            BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"AD0.ad.evotec.xyz","log_name":"System","max_events":10}"""),
            BuildToolCall("call_2", "eventlog_top_events", """{"machine_name":"ADRODC.ad.evotec.pl","log_name":"System","max_events":10}""")
        };
        var outputs = new[] {
            new ToolOutput("call_1", """{"ok":true,"log_name":"System","count":10,"truncated":true,"events":[{"machine_name":"AD0.ad.evotec.xyz"}],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"AD0.ad.evotec.xyz","query_mode":"top_events","rows":10,"truncated":true},"summary_markdown":"### Top 10 recent events (preview)\r\n\r\n| Time Created Utc | Id | Record Id |"}"""),
            new ToolOutput("call_2", """{"ok":true,"log_name":"System","count":0,"truncated":false,"events":[],"discovery_status":{"scope":"remote","log_name":"System","machine_name":"ADRODC.ad.evotec.pl","query_mode":"top_events","rows":0,"truncated":false},"summary_markdown":"### Top 10 recent events (preview)\r\n\r\n| Time Created Utc | Id | Record Id |"}""")
        };

        var prompt = InvokeBuildNoTextToolOutputRetryPromptForTesting(
            userRequest: "Continue EventLog evidence across all remaining DCs for this AD scope.",
            toolCalls: calls,
            toolOutputs: outputs);

        Assert.Contains("System EventLog top events for `AD0.ad.evotec.xyz` returned 10 row(s); preview truncated.", prompt, StringComparison.Ordinal);
        Assert.Contains("System EventLog top events for `ADRODC.ad.evotec.pl` returned 0 row(s).", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("| Time Created Utc |", prompt, StringComparison.OrdinalIgnoreCase);
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
