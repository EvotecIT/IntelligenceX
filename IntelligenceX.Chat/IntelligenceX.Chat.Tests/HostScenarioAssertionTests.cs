using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostScenarioAssertionTests {
    [Fact]
    public void EvaluateScenarioAssertions_FailsStrictToolContract_OnPairingDuplicatesAndRetryChurn() {
        const string json = """
{
  "name": "strict-check",
  "turns": [
    {
      "name": "Strict Turn",
      "user": "Check AD0 reboot evidence.",
      "min_tool_calls": 1,
      "require_any_tools": ["eventlog_*query*"]
    }
  ]
}
""";
        var turn = ParseSingleTurn(json);

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", "{\"machine_name\":\"AD0\"}"),
            BuildToolCall("call_1", "eventlog_live_query", "{\"machine_name\":\"AD0\"}")
        };
        var outputs = new List<ToolOutput> {
            new("call_2", "{\"ok\":true}"),
            new("call_2", "{\"ok\":true}")
        };

        var metricsResult = BuildMetricsResult(
            assistantText: "Completed.",
            toolCalls: calls,
            toolOutputs: outputs,
            toolRounds: 1,
            noToolExecutionRetries: 1);

        var failures = InvokeEvaluateScenarioAssertions(turn, metricsResult);

        Assert.Contains(failures, value => value.Contains("no-tool execution retry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("unique tool call IDs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("unique tool output call IDs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("missing output", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("orphan output", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("tool call signature", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenarioAssertions_FailsCleanCompletion_WhenPartialMarkerAppears() {
        const string json = """
{
  "name": "clean-completion",
  "turns": [
    {
      "name": "Turn 1",
      "user": "Check AD status."
    }
  ]
}
""";
        var turn = ParseSingleTurn(json);

        var metricsResult = BuildMetricsResult(
            assistantText: "Partial response shown above. The turn ended before completion.",
            toolCalls: Array.Empty<ToolCall>(),
            toolOutputs: Array.Empty<ToolOutput>(),
            toolRounds: 0,
            noToolExecutionRetries: 0);

        var failures = InvokeEvaluateScenarioAssertions(turn, metricsResult);

        Assert.Contains(failures, value => value.Contains("clean completion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenarioAssertions_FailsOnDuplicateSignature_WhenArgumentsOnlyDifferByKeyOrder() {
        const string json = """
{
  "name": "signature-normalization",
  "turns": [
    {
      "name": "Turn 1",
      "user": "Query reboot evidence.",
      "min_tool_calls": 1,
      "require_any_tools": ["eventlog_*query*"]
    }
  ]
}
""";
        var turn = ParseSingleTurn(json);

        var calls = new List<ToolCall> {
            BuildToolCall("call_1", "eventlog_live_query", "{\"machine_name\":\"AD0\",\"window\":\"last_24_hours_utc\"}"),
            BuildToolCall("call_2", "eventlog_live_query", "{\"window\":\"last_24_hours_utc\",\"machine_name\":\"AD0\"}")
        };
        var outputs = new List<ToolOutput> {
            new("call_1", "{\"ok\":true}"),
            new("call_2", "{\"ok\":true}")
        };

        var metricsResult = BuildMetricsResult(
            assistantText: "Completed.",
            toolCalls: calls,
            toolOutputs: outputs,
            toolRounds: 1,
            noToolExecutionRetries: 0);

        var failures = InvokeEvaluateScenarioAssertions(turn, metricsResult);

        Assert.Contains(failures, value => value.Contains("tool call signature", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(failures, value => value.Contains("unique tool call IDs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(failures, value => value.Contains("orphan output", StringComparison.OrdinalIgnoreCase));
    }

    private static object ParseSingleTurn(string json) {
        var programType = ResolveHostProgramType();
        var parseMethod = programType.GetMethod("ParseChatScenarioDefinition", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parseMethod);

        var scenario = parseMethod!.Invoke(null, new object?[] { json, "scenario" });
        Assert.NotNull(scenario);

        var turnsProperty = scenario!.GetType().GetProperty("Turns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(turnsProperty);
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(turnsProperty!.GetValue(scenario));
        var turns = enumerable.Cast<object>().ToList();
        Assert.Single(turns);
        return turns[0];
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

    private static object BuildMetricsResult(string assistantText, IReadOnlyList<ToolCall> toolCalls, IReadOnlyList<ToolOutput> toolOutputs,
        int toolRounds, int noToolExecutionRetries) {
        var programType = ResolveHostProgramType();
        var hostAssembly = programType.Assembly;
        var resultType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplTurnResult", throwOnError: true);
        var metricsType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplTurnMetrics", throwOnError: true);
        var metricsResultType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplTurnMetricsResult", throwOnError: true);

        Assert.NotNull(resultType);
        Assert.NotNull(metricsType);
        Assert.NotNull(metricsResultType);

        var now = DateTime.UtcNow;
        var result = Activator.CreateInstance(resultType!, new object?[] {
            assistantText,
            toolCalls,
            toolOutputs,
            null,
            toolRounds,
            noToolExecutionRetries
        });
        Assert.NotNull(result);

        var metrics = Activator.CreateInstance(metricsType!, new object?[] {
            now,
            null,
            now,
            1L,
            null,
            null,
            toolCalls.Count,
            toolRounds,
            noToolExecutionRetries
        });
        Assert.NotNull(metrics);

        var metricsResult = Activator.CreateInstance(metricsResultType!, new[] { result, metrics });
        Assert.NotNull(metricsResult);
        return metricsResult!;
    }

    private static IReadOnlyList<string> InvokeEvaluateScenarioAssertions(object turn, object metricsResult) {
        var programType = ResolveHostProgramType();
        var method = programType.GetMethod("EvaluateScenarioAssertions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new[] { turn, metricsResult });
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(result);
        return enumerable.Cast<object>().Select(value => value?.ToString() ?? string.Empty).ToList();
    }

    private static Type ResolveHostProgramType() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var hostProgramType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program", throwOnError: true);
        Assert.NotNull(hostProgramType);
        return hostProgramType!;
    }
}
