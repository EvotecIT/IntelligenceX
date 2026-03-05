using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IxJsonArray = IntelligenceX.Json.JsonArray;
using IxJsonObject = IntelligenceX.Json.JsonObject;
using IxJsonValue = IntelligenceX.Json.JsonValue;
using IxJsonValueKind = IntelligenceX.Json.JsonValueKind;
using JsonLite = IntelligenceX.Json.JsonLite;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static string ResolveScenarioReportPath(ReplOptions options, string scenarioPath, string scenarioName, DateTime startedAtUtc) {
        var configured = options.ScenarioOutputFile;
        if (!string.IsNullOrWhiteSpace(configured)) {
            var expanded = Path.GetFullPath(configured.Trim());
            var treatAsDirectory =
                expanded.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || expanded.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(Path.GetExtension(expanded));
            if (treatAsDirectory) {
                Directory.CreateDirectory(expanded);
                return Path.Combine(expanded, BuildScenarioReportFileName(scenarioName, startedAtUtc));
            }

            var parent = Path.GetDirectoryName(expanded);
            if (!string.IsNullOrWhiteSpace(parent)) {
                Directory.CreateDirectory(parent);
            }
            return expanded;
        }

        var artifactsDir = Path.Combine(Environment.CurrentDirectory, "artifacts", "chat-scenarios");
        Directory.CreateDirectory(artifactsDir);
        var fallbackName = string.IsNullOrWhiteSpace(scenarioName) ? Path.GetFileNameWithoutExtension(scenarioPath) : scenarioName;
        return Path.Combine(artifactsDir, BuildScenarioReportFileName(fallbackName, startedAtUtc));
    }

    private static string ResolveScenarioJsonReportPath(string markdownReportPath) {
        var markdownPath = (markdownReportPath ?? string.Empty).Trim();
        if (markdownPath.Length == 0) {
            throw new ArgumentException("Scenario markdown report path is required.", nameof(markdownReportPath));
        }

        var extension = Path.GetExtension(markdownPath);
        if (string.IsNullOrWhiteSpace(extension)) {
            return markdownPath + ".json";
        }

        return Path.ChangeExtension(markdownPath, ".json");
    }

    private static string BuildScenarioReportFileName(string scenarioName, DateTime startedAtUtc) {
        var stem = SanitizeScenarioName(scenarioName);
        return $"{stem}-{startedAtUtc:yyyyMMdd-HHmmss}.md";
    }

    private static string SanitizeScenarioName(string scenarioName) {
        var name = string.IsNullOrWhiteSpace(scenarioName) ? "scenario" : scenarioName.Trim();
        var builder = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++) {
            var c = name[i];
            if (char.IsLetterOrDigit(c)) {
                builder.Append(char.ToLowerInvariant(c));
                continue;
            }
            builder.Append('-');
        }

        var compact = builder.ToString().Trim('-');
        if (compact.Length == 0) {
            compact = "scenario";
        }

        while (compact.Contains("--", StringComparison.Ordinal)) {
            compact = compact.Replace("--", "-", StringComparison.Ordinal);
        }
        return compact;
    }

    private static void WriteScenarioReportMarkdown(string reportPath, ScenarioRunReport report) {
        var markdown = BuildScenarioReportMarkdown(report);
        File.WriteAllText(reportPath, markdown);
    }

    private static void WriteScenarioReportJson(string reportPath, ScenarioRunReport report) {
        var payload = new {
            schema_version = "ix_chat_scenario_report_v1",
            name = report.ScenarioName,
            source = report.ScenarioSourcePath,
            started_utc = report.StartedAtUtc,
            completed_utc = report.CompletedAtUtc,
            continue_on_error = report.ContinueOnError,
            passed_turns = report.TurnRuns.Count(static turn => turn.Success),
            total_turns = report.TurnRuns.Count,
            turns = report.TurnRuns.Select(turn => {
                var toolCalls = turn.Result?.Result.ToolCalls
                    .Select(call => new {
                        call_id = call.CallId,
                        name = call.Name,
                        input = call.Input,
                        signature = BuildToolCallSignature(call)
                    })
                    .ToArray()
                    ?? Array.Empty<object>();
                var toolOutputs = turn.Result?.Result.ToolOutputs
                    .Select(output => {
                        var isError = TryReadToolOutputErrorCode(output.Output, out var errorCode);
                        var hasOk = TryReadToolOutputOk(output.Output, out var ok);
                        return new {
                            call_id = output.CallId,
                            output = output.Output,
                            ok = hasOk ? ok : (bool?)null,
                            is_error = isError,
                            error_code = string.IsNullOrWhiteSpace(errorCode) ? null : errorCode
                        };
                    })
                    .ToArray()
                    ?? Array.Empty<object>();
                return new {
                    index = turn.Index,
                    label = turn.Label,
                    success = turn.Success,
                    started_utc = turn.StartedAtUtc,
                    completed_utc = turn.CompletedAtUtc,
                    user = turn.User,
                    assistant = turn.Result?.Result.Text ?? string.Empty,
                    metrics = turn.Result is null
                        ? null
                        : new {
                            duration_ms = turn.Result.Metrics.DurationMs,
                            ttft_ms = turn.Result.Metrics.TtftMs,
                            tool_calls = turn.Result.Metrics.ToolCallsCount,
                            tool_rounds = turn.Result.Metrics.ToolRounds,
                            no_tool_retries = turn.Result.Metrics.NoToolExecutionRetries,
                            phase_timings = turn.Result.Metrics.PhaseTimings.Count == 0
                                ? null
                                : turn.Result.Metrics.PhaseTimings
                                    .Select(phaseTiming => new {
                                        phase = phaseTiming.Phase,
                                        duration_ms = phaseTiming.DurationMs,
                                        event_count = phaseTiming.EventCount
                                    })
                                    .ToArray(),
                            usage = turn.Result.Result.Usage is null
                                ? null
                                : new {
                                    input_tokens = turn.Result.Result.Usage.InputTokens,
                                    output_tokens = turn.Result.Result.Usage.OutputTokens,
                                    total_tokens = turn.Result.Result.Usage.TotalTokens,
                                    cached_input_tokens = turn.Result.Result.Usage.CachedInputTokens,
                                    reasoning_tokens = turn.Result.Result.Usage.ReasoningTokens
                                }
                        },
                    assertion_failures = turn.AssertionFailures,
                    exception = turn.Exception?.ToString(),
                    tool_calls = toolCalls,
                    tool_outputs = toolOutputs
                };
            })
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions {
            WriteIndented = true
        });
        File.WriteAllText(reportPath, json);
    }

    private static string BuildScenarioReportMarkdown(ScenarioRunReport report) {
        var sb = new StringBuilder();
        sb.AppendLine("# Chat Scenario Report");
        sb.AppendLine();
        sb.AppendLine($"- Name: {report.ScenarioName}");
        sb.AppendLine($"- Source: `{report.ScenarioSourcePath}`");
        sb.AppendLine($"- Started (UTC): {report.StartedAtUtc:yyyy-MM-ddTHH:mm:ss.fffZ}");
        sb.AppendLine($"- Completed (UTC): {report.CompletedAtUtc:yyyy-MM-ddTHH:mm:ss.fffZ}");
        sb.AppendLine($"- Continue on error: {report.ContinueOnError}");
        sb.AppendLine($"- Passed turns: {report.TurnRuns.Count(static t => t.Success)}/{report.TurnRuns.Count}");
        sb.AppendLine();

        for (var i = 0; i < report.TurnRuns.Count; i++) {
            var turn = report.TurnRuns[i];
            sb.AppendLine($"## {turn.Index}. {turn.Label}");
            sb.AppendLine();
            sb.AppendLine($"- Success: {turn.Success}");
            sb.AppendLine($"- Started (UTC): {turn.StartedAtUtc:yyyy-MM-ddTHH:mm:ss.fffZ}");
            sb.AppendLine($"- Completed (UTC): {turn.CompletedAtUtc:yyyy-MM-ddTHH:mm:ss.fffZ}");
            if (turn.Result is not null) {
                sb.AppendLine($"- Duration ms: {turn.Result.Metrics.DurationMs}");
                sb.AppendLine($"- TTFT ms: {(turn.Result.Metrics.TtftMs.HasValue ? turn.Result.Metrics.TtftMs.Value.ToString() : "-")}");
                sb.AppendLine($"- Tool calls: {turn.Result.Metrics.ToolCallsCount}");
                sb.AppendLine($"- Tool rounds: {turn.Result.Metrics.ToolRounds}");
                sb.AppendLine($"- No-tool retries: {turn.Result.Metrics.NoToolExecutionRetries}");
                if (turn.Result.Metrics.PhaseTimings.Count > 0) {
                    var phaseTimingSummary = turn.Result.Metrics.PhaseTimings
                        .OrderBy(static phaseTiming => phaseTiming.Phase, StringComparer.OrdinalIgnoreCase)
                        .Select(static phaseTiming => $"{phaseTiming.Phase}={Math.Max(0, phaseTiming.DurationMs)}ms/{Math.Max(0, phaseTiming.EventCount)}ev")
                        .ToArray();
                    sb.AppendLine($"- Phase timings: {string.Join(", ", phaseTimingSummary)}");
                }
                var toolNames = turn.Result.Result.ToolCalls
                    .Select(static c => (c.Name ?? string.Empty).Trim())
                    .Where(static n => n.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                sb.AppendLine($"- Tool names: {(toolNames.Length == 0 ? "-" : string.Join(", ", toolNames))}");
                var (toolErrorCount, toolErrorCodes) = SummarizeToolOutputErrors(turn.Result.Result.ToolOutputs);
                var errorCodesText = toolErrorCodes.Count == 0
                    ? "-"
                    : string.Join(", ", toolErrorCodes.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase));
                sb.AppendLine($"- Tool error outputs: {toolErrorCount} (codes: {errorCodesText})");
                if (turn.Result.Result.Usage is not null) {
                    sb.AppendLine($"- Tokens (in/out/total): {FormatToken(turn.Result.Result.Usage.InputTokens)}/{FormatToken(turn.Result.Result.Usage.OutputTokens)}/{FormatToken(turn.Result.Result.Usage.TotalTokens)}");
                }
            }
            sb.AppendLine();
            sb.AppendLine("### User");
            sb.AppendLine();
            sb.AppendLine("```text");
            sb.AppendLine(turn.User);
            sb.AppendLine("```");
            sb.AppendLine();
            if (turn.Result is not null) {
                sb.AppendLine("### Assistant");
                sb.AppendLine();
                sb.AppendLine("```text");
                sb.AppendLine(turn.Result.Result.Text ?? string.Empty);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            if (turn.AssertionFailures.Count > 0) {
                sb.AppendLine("### Assertion Failures");
                sb.AppendLine();
                foreach (var assertionFailure in turn.AssertionFailures) {
                    sb.AppendLine($"- {assertionFailure}");
                }
                sb.AppendLine();
            }

            if (turn.Exception is not null) {
                sb.AppendLine("### Exception");
                sb.AppendLine();
                sb.AppendLine("```text");
                sb.AppendLine(turn.Exception.ToString());
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool TryReadToolOutputOk(string output, out bool ok) {
        ok = false;
        if (string.IsNullOrWhiteSpace(output)) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ok", out var okElement)
                || (okElement.ValueKind != JsonValueKind.True && okElement.ValueKind != JsonValueKind.False)) {
                return false;
            }

            ok = okElement.GetBoolean();
            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static string FormatToken(int? value) {
        return value.HasValue ? Math.Max(0, value.Value).ToString() : "-";
    }
}
