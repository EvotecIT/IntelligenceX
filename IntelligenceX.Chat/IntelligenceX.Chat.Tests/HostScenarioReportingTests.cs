using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostScenarioReportingTests {
    [Fact]
    public void BuildScenarioReportMarkdown_IncludesPhaseTimingRollupsSummary() {
        var report = BuildScenarioRunReport(new[] {
            BuildScenarioTurnRun(
                index: 1,
                label: "Turn 1",
                user: "Run model planning.",
                assistantText: "Completed turn 1.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 100, EventCount = 1 },
                    new TurnPhaseTimingDto { Phase = "tool_execute", DurationMs = 450, EventCount = 2 }
                }),
            BuildScenarioTurnRun(
                index: 2,
                label: "Turn 2",
                user: "Run model planning again.",
                assistantText: "Completed turn 2.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 200, EventCount = 2 }
                }),
            BuildScenarioTurnRun(
                index: 3,
                label: "Turn 3",
                user: "Run extended planning.",
                assistantText: "Completed turn 3.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 800, EventCount = 1 },
                    new TurnPhaseTimingDto { Phase = "tool_execute", DurationMs = 300, EventCount = 1 }
                })
        });

        var markdown = InvokeBuildScenarioReportMarkdown(report);

        Assert.Contains("Phase timing rollups:", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model_plan:3 samples p50=200ms p95=800ms max=800ms", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tool_execute:2 samples p50=300ms p95=450ms max=450ms", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteScenarioReportJson_EmitsPhaseTimingRollupsPercentiles() {
        var report = BuildScenarioRunReport(new[] {
            BuildScenarioTurnRun(
                index: 1,
                label: "Turn 1",
                user: "Run model planning.",
                assistantText: "Completed turn 1.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 100, EventCount = 1 }
                }),
            BuildScenarioTurnRun(
                index: 2,
                label: "Turn 2",
                user: "Run model planning again.",
                assistantText: "Completed turn 2.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 200, EventCount = 2 }
                }),
            BuildScenarioTurnRun(
                index: 3,
                label: "Turn 3",
                user: "Run extended planning.",
                assistantText: "Completed turn 3.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 800, EventCount = 1 }
                })
        });

        var reportPath = Path.Combine(Path.GetTempPath(), $"ix-chat-phase-rollups-{Guid.NewGuid():N}.json");
        try {
            InvokeWriteScenarioReportJson(reportPath, report);
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            Assert.True(root.TryGetProperty("phase_timing_rollups", out var rollupsElement));
            Assert.Equal(JsonValueKind.Array, rollupsElement.ValueKind);

            var modelPlanRollup = rollupsElement.EnumerateArray()
                .FirstOrDefault(element =>
                    element.TryGetProperty("phase", out var phaseElement)
                    && string.Equals(phaseElement.GetString(), "model_plan", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(JsonValueKind.Object, modelPlanRollup.ValueKind);
            Assert.Equal(3, modelPlanRollup.GetProperty("samples").GetInt32());
            Assert.Equal(1100, modelPlanRollup.GetProperty("total_duration_ms").GetInt64());
            Assert.Equal(4, modelPlanRollup.GetProperty("total_events").GetInt64());
            Assert.Equal(367, modelPlanRollup.GetProperty("average_duration_ms").GetInt64());
            Assert.Equal(200, modelPlanRollup.GetProperty("p50_duration_ms").GetInt64());
            Assert.Equal(800, modelPlanRollup.GetProperty("p95_duration_ms").GetInt64());
            Assert.Equal(800, modelPlanRollup.GetProperty("max_duration_ms").GetInt64());
        } finally {
            try {
                if (File.Exists(reportPath)) {
                    File.Delete(reportPath);
                }
            } catch {
                // Best-effort cleanup for temp test artifacts.
            }
        }
    }

    [Fact]
    public void BuildScenarioReportMarkdown_IncludesScenarioRollupAssertionFailures() {
        var report = BuildScenarioRunReport(
            new[] {
                BuildScenarioTurnRun(
                    index: 1,
                    label: "Turn 1",
                    user: "Run model planning.",
                    assistantText: "Completed turn 1.",
                    phaseTimings: new[] {
                        new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 800, EventCount = 1 }
                    })
            },
            rollupAssertionFailures: new[] {
                "Expected scenario rollup phase 'model_plan' p95 <= 500ms; observed 800ms across 1 sample(s)."
            });

        var markdown = InvokeBuildScenarioReportMarkdown(report);

        Assert.Contains("Rollup assertion failures: 1", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Scenario Rollup Assertion Failures", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("model_plan", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("p95 <= 500ms", markdown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteScenarioReportJson_EmitsScenarioRollupAssertionFailures() {
        var report = BuildScenarioRunReport(
            new[] {
                BuildScenarioTurnRun(
                    index: 1,
                    label: "Turn 1",
                    user: "Run model planning.",
                    assistantText: "Completed turn 1.",
                    phaseTimings: new[] {
                        new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 800, EventCount = 1 }
                    })
            },
            rollupAssertionFailures: new[] {
                "Expected scenario rollup phase 'model_plan' p95 <= 500ms; observed 800ms across 1 sample(s)."
            });

        var reportPath = Path.Combine(Path.GetTempPath(), $"ix-chat-rollup-assertions-{Guid.NewGuid():N}.json");
        try {
            InvokeWriteScenarioReportJson(reportPath, report);
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;

            Assert.True(root.TryGetProperty("rollup_assertion_failures", out var failuresElement));
            Assert.Equal(JsonValueKind.Array, failuresElement.ValueKind);
            Assert.Single(failuresElement.EnumerateArray());
            Assert.Contains("model_plan", failuresElement[0].GetString(), StringComparison.OrdinalIgnoreCase);
        } finally {
            try {
                if (File.Exists(reportPath)) {
                    File.Delete(reportPath);
                }
            } catch {
                // Best-effort cleanup for temp test artifacts.
            }
        }
    }

    [Fact]
    public void EvaluateScenarioRollupAssertions_FailsWhenP95ExceedsConfiguredLimit() {
        const string json = """
{
  "name": "scenario-rollup-guardrail-fail",
  "max_phase_p95_duration_ms": {
    "model_plan": 500
  },
  "turns": [
    {
      "user": "Run turn one."
    },
    {
      "user": "Run turn two."
    },
    {
      "user": "Run turn three."
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "scenario-rollup-guardrail-fail");
        var turnRuns = new[] {
            BuildScenarioTurnRun(
                index: 1,
                label: "Turn 1",
                user: "Run turn one.",
                assistantText: "Completed turn one.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 100, EventCount = 1 }
                }),
            BuildScenarioTurnRun(
                index: 2,
                label: "Turn 2",
                user: "Run turn two.",
                assistantText: "Completed turn two.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 700, EventCount = 1 }
                }),
            BuildScenarioTurnRun(
                index: 3,
                label: "Turn 3",
                user: "Run turn three.",
                assistantText: "Completed turn three.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 200, EventCount = 1 }
                })
        };

        var failures = InvokeEvaluateScenarioRollupAssertions(scenario, turnRuns);

        Assert.Contains(failures, value => value.Contains("model_plan", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("p95 <= 500ms", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenarioRollupAssertions_FailsWhenConfiguredPhaseIsMissing() {
        const string json = """
{
  "name": "scenario-rollup-guardrail-missing",
  "max_phase_p95_duration_ms": {
    "lane_wait": 250
  },
  "turns": [
    {
      "user": "Run turn one."
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "scenario-rollup-guardrail-missing");
        var turnRuns = new[] {
            BuildScenarioTurnRun(
                index: 1,
                label: "Turn 1",
                user: "Run turn one.",
                assistantText: "Completed turn one.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 100, EventCount = 1 }
                })
        };

        var failures = InvokeEvaluateScenarioRollupAssertions(scenario, turnRuns);

        Assert.Contains(failures, value => value.Contains("lane_wait", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(failures, value => value.Contains("to be present", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EvaluateScenarioRollupAssertions_PassesWhenP95IsWithinLimit() {
        const string json = """
{
  "name": "scenario-rollup-guardrail-pass",
  "max_phase_p95_duration_ms": {
    "model_plan": 700
  },
  "turns": [
    {
      "user": "Run turn one."
    },
    {
      "user": "Run turn two."
    },
    {
      "user": "Run turn three."
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "scenario-rollup-guardrail-pass");
        var turnRuns = new[] {
            BuildScenarioTurnRun(
                index: 1,
                label: "Turn 1",
                user: "Run turn one.",
                assistantText: "Completed turn one.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 100, EventCount = 1 }
                }),
            BuildScenarioTurnRun(
                index: 2,
                label: "Turn 2",
                user: "Run turn two.",
                assistantText: "Completed turn two.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 700, EventCount = 1 }
                }),
            BuildScenarioTurnRun(
                index: 3,
                label: "Turn 3",
                user: "Run turn three.",
                assistantText: "Completed turn three.",
                phaseTimings: new[] {
                    new TurnPhaseTimingDto { Phase = "model_plan", DurationMs = 200, EventCount = 1 }
                })
        };

        var failures = InvokeEvaluateScenarioRollupAssertions(scenario, turnRuns);

        Assert.Empty(failures);
    }

    private static object BuildScenarioRunReport(IReadOnlyList<object> turnRuns, IReadOnlyList<string>? rollupAssertionFailures = null) {
        var programType = ResolveHostProgramType();
        var turnRunType = programType.Assembly.GetType("IntelligenceX.Chat.Host.Program+ScenarioTurnRun", throwOnError: true);
        var runReportType = programType.Assembly.GetType("IntelligenceX.Chat.Host.Program+ScenarioRunReport", throwOnError: true);
        Assert.NotNull(turnRunType);
        Assert.NotNull(runReportType);

        var startedAtUtc = new DateTime(2026, 3, 5, 20, 0, 0, DateTimeKind.Utc);
        var completedAtUtc = startedAtUtc.AddMinutes(1);
        var typedTurnRuns = Array.CreateInstance(turnRunType!, turnRuns.Count);
        for (var i = 0; i < turnRuns.Count; i++) {
            typedTurnRuns.SetValue(turnRuns[i], i);
        }
        var report = Activator.CreateInstance(runReportType!, new object?[] {
            "phase-rollup-scenario",
            "scenario.json",
            startedAtUtc,
            completedAtUtc,
            false,
            typedTurnRuns,
            rollupAssertionFailures ?? Array.Empty<string>()
        });
        Assert.NotNull(report);
        return report!;
    }

    private static object BuildScenarioTurnRun(
        int index,
        string label,
        string user,
        string assistantText,
        IReadOnlyList<TurnPhaseTimingDto> phaseTimings) {
        var programType = ResolveHostProgramType();
        var turnRunType = programType.Assembly.GetType("IntelligenceX.Chat.Host.Program+ScenarioTurnRun", throwOnError: true);
        Assert.NotNull(turnRunType);

        var startedAtUtc = new DateTime(2026, 3, 5, 20, 0, 0, DateTimeKind.Utc).AddSeconds(index * 5);
        var completedAtUtc = startedAtUtc.AddSeconds(1);
        var metricsResult = BuildMetricsResult(
            assistantText,
            Array.Empty<ToolCall>(),
            Array.Empty<ToolOutput>(),
            toolRounds: 0,
            noToolExecutionRetries: 0,
            phaseTimings: phaseTimings);
        var turnRun = Activator.CreateInstance(turnRunType!, new object?[] {
            index,
            label,
            user,
            startedAtUtc,
            completedAtUtc,
            metricsResult,
            null,
            Array.Empty<string>()
        });
        Assert.NotNull(turnRun);
        return turnRun!;
    }

    private static object BuildMetricsResult(
        string assistantText,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<ToolOutput> toolOutputs,
        int toolRounds,
        int noToolExecutionRetries,
        IReadOnlyList<TurnPhaseTimingDto>? phaseTimings = null) {
        var programType = ResolveHostProgramType();
        var hostAssembly = programType.Assembly;
        var resultType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplTurnResult", throwOnError: true);
        var metricsType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplTurnMetrics", throwOnError: true);
        var metricsResultType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program+ReplTurnMetricsResult", throwOnError: true);

        Assert.NotNull(resultType);
        Assert.NotNull(metricsType);
        Assert.NotNull(metricsResultType);

        var now = new DateTime(2026, 3, 5, 20, 0, 0, DateTimeKind.Utc);
        var result = Activator.CreateInstance(resultType!, new object?[] {
            assistantText,
            toolCalls,
            toolOutputs,
            null,
            toolRounds,
            noToolExecutionRetries
        });
        Assert.NotNull(result);

        object? metrics;
        if (phaseTimings is { Count: > 0 }) {
            metrics = Activator.CreateInstance(metricsType!, new object?[] {
                now,
                null,
                now,
                1L,
                null,
                null,
                toolCalls.Count,
                toolRounds,
                noToolExecutionRetries,
                phaseTimings
            });
        } else {
            metrics = Activator.CreateInstance(metricsType!, new object?[] {
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
        }

        Assert.NotNull(metrics);

        var metricsResult = Activator.CreateInstance(metricsResultType!, new[] { result, metrics });
        Assert.NotNull(metricsResult);
        return metricsResult!;
    }

    private static string InvokeBuildScenarioReportMarkdown(object report) {
        var programType = ResolveHostProgramType();
        var method = programType.GetMethod("BuildScenarioReportMarkdown", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var value = method!.Invoke(null, new[] { report });
        return Assert.IsType<string>(value);
    }

    private static void InvokeWriteScenarioReportJson(string reportPath, object report) {
        var programType = ResolveHostProgramType();
        var method = programType.GetMethod("WriteScenarioReportJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        method!.Invoke(null, new[] { reportPath, report });
    }

    private static object InvokeParseScenarioDefinition(string raw, string fallbackName) {
        var programType = ResolveHostProgramType();
        var parseMethod = programType.GetMethod("ParseChatScenarioDefinition", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(null, new object?[] { raw, fallbackName });
        Assert.NotNull(result);
        return result!;
    }

    private static IReadOnlyList<string> InvokeEvaluateScenarioRollupAssertions(object scenario, IReadOnlyList<object> turnRuns) {
        var programType = ResolveHostProgramType();
        var evaluateMethod = programType.GetMethod("EvaluateScenarioRollupAssertions", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(evaluateMethod);

        var turnRunType = programType.Assembly.GetType("IntelligenceX.Chat.Host.Program+ScenarioTurnRun", throwOnError: true);
        Assert.NotNull(turnRunType);
        var typedTurnRuns = Array.CreateInstance(turnRunType!, turnRuns.Count);
        for (var i = 0; i < turnRuns.Count; i++) {
            typedTurnRuns.SetValue(turnRuns[i], i);
        }

        var result = evaluateMethod!.Invoke(null, new object?[] { scenario, typedTurnRuns });
        var enumerable = Assert.IsAssignableFrom<System.Collections.IEnumerable>(result);
        return enumerable.Cast<object>()
            .Select(static value => value?.ToString() ?? string.Empty)
            .ToList();
    }

    private static Type ResolveHostProgramType() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var hostProgramType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program", throwOnError: true);
        Assert.NotNull(hostProgramType);
        return hostProgramType!;
    }
}
