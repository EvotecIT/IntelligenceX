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

    private static object BuildScenarioRunReport(IReadOnlyList<object> turnRuns) {
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
            typedTurnRuns
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

    private static Type ResolveHostProgramType() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var hostProgramType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program", throwOnError: true);
        Assert.NotNull(hostProgramType);
        return hostProgramType!;
    }
}
