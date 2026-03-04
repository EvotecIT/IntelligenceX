using System;
using System.Collections.Generic;
using System.Linq;
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
    public void ApplyDomainDetectiveSummaryTimeoutFallback_ExtendsLowTimeoutOnTimeoutFailure() {
        var call = BuildToolCall(
            "call_1",
            "domaindetective_domain_summary",
            """{"domain":"contoso.com","checks":["NS","MX"],"timeout_ms":8000}""");
        const string output = """{"ok":false,"error_code":"timeout","error":"DomainDetective run timed out after timeout_ms=8000."}""";

        var repaired = InvokeApplyDomainDetectiveSummaryTimeoutFallback(call, output);

        Assert.NotSame(call, repaired);
        Assert.Equal(30000L, repaired.Arguments?.GetInt64("timeout_ms"));
        Assert.Equal("contoso.com", repaired.Arguments?.GetString("domain"));
    }

    [Fact]
    public void ApplyDomainDetectiveSummaryTimeoutFallback_DoesNotPatchWhenTimeoutAlreadyHighEnough() {
        var call = BuildToolCall(
            "call_1",
            "domaindetective_domain_summary",
            """{"domain":"contoso.com","checks":["NS","MX"],"timeout_ms":60000}""");
        const string output = """{"ok":false,"error_code":"timeout","error":"DomainDetective run timed out after timeout_ms=60000."}""";

        var repaired = InvokeApplyDomainDetectiveSummaryTimeoutFallback(call, output);

        Assert.Same(call, repaired);
    }

    [Fact]
    public void ApplyDomainDetectiveSummaryTimeoutFallback_DoesNotPatchNonTimeoutFailures() {
        var call = BuildToolCall(
            "call_1",
            "domaindetective_domain_summary",
            """{"domain":"contoso.com","checks":["NS","MX"],"timeout_ms":8000}""");
        const string output = """{"ok":false,"error_code":"tool_exception","error":"Unhandled exception."}""";

        var repaired = InvokeApplyDomainDetectiveSummaryTimeoutFallback(call, output);

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

}
