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
    private static async Task<int> RunScenarioFileAsync(ReplSession session, ReplOptions options, CancellationToken cancellationToken) {
        if (session is null) {
            throw new ArgumentNullException(nameof(session));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.ScenarioFile)) {
            throw new ArgumentException("Scenario file path is required.", nameof(options));
        }

        var scenarioPath = Path.GetFullPath(options.ScenarioFile.Trim());
        ChatScenarioDefinition scenario;
        try {
            scenario = LoadChatScenarioDefinition(scenarioPath);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load scenario '{scenarioPath}': {ex.Message}");
            return 2;
        }

        if (scenario.Turns.Count == 0) {
            Console.Error.WriteLine($"Scenario '{scenarioPath}' has no turns.");
            return 2;
        }

        Console.WriteLine($"Scenario mode: {scenario.Name} ({scenario.Turns.Count} turn{(scenario.Turns.Count == 1 ? string.Empty : "s")})");
        Console.WriteLine($"Scenario source: {scenarioPath}");
        Console.WriteLine("Runtime path: host-repl (direct chat loop, not Chat.Service sidecar).");
        Console.WriteLine($"Continue on error: {(options.ScenarioContinueOnError ? "on" : "off")}");
        Console.WriteLine();

        var startedAtUtc = DateTime.UtcNow;
        var turnRuns = new List<ScenarioTurnRun>(scenario.Turns.Count);
        var failed = false;

        for (var i = 0; i < scenario.Turns.Count; i++) {
            cancellationToken.ThrowIfCancellationRequested();

            var turn = scenario.Turns[i];
            var label = string.IsNullOrWhiteSpace(turn.Name) ? $"Turn {i + 1}" : turn.Name.Trim();
            Console.WriteLine($"[{i + 1}/{scenario.Turns.Count}] {label}");
            Console.WriteLine($"> user: {turn.User}");

            var turnStartedAtUtc = DateTime.UtcNow;
            ReplTurnMetricsResult? metricsResult = null;
            Exception? failure = null;
            try {
                var prompt = BuildScenarioTurnPrompt(turn);
                metricsResult = await session.AskWithMetricsAsync(prompt, cancellationToken).ConfigureAwait(false);
                WriteTurnResult(metricsResult.Result, options);
            } catch (Exception ex) {
                failure = ex;
                WriteTurnFailure(ex);
            }

            var assertionFailures = EvaluateScenarioAssertions(turn, metricsResult);
            if (assertionFailures.Count > 0) {
                foreach (var assertionFailure in assertionFailures) {
                    Console.WriteLine("Assertion failed: " + assertionFailure);
                }
            }

            var success = failure is null && assertionFailures.Count == 0;
            failed |= !success;
            turnRuns.Add(new ScenarioTurnRun(
                index: i + 1,
                label: label,
                user: turn.User,
                startedAtUtc: turnStartedAtUtc,
                completedAtUtc: DateTime.UtcNow,
                result: metricsResult,
                exception: failure,
                assertionFailures: assertionFailures));

            Console.WriteLine();
            if (!success && !options.ScenarioContinueOnError) {
                Console.WriteLine("Stopping scenario early because a turn failed and --scenario-continue-on-error is not enabled.");
                Console.WriteLine();
                break;
            }
        }

        var completedAtUtc = DateTime.UtcNow;
        var reportPath = ResolveScenarioReportPath(options, scenarioPath, scenario.Name, startedAtUtc);
        var report = new ScenarioRunReport(
            scenarioName: scenario.Name,
            scenarioSourcePath: scenarioPath,
            startedAtUtc: startedAtUtc,
            completedAtUtc: completedAtUtc,
            continueOnError: options.ScenarioContinueOnError,
            turnRuns: turnRuns);
        try {
            WriteScenarioReportMarkdown(reportPath, report);
            Console.WriteLine($"Scenario report saved: {reportPath}");
            var jsonReportPath = ResolveScenarioJsonReportPath(reportPath);
            WriteScenarioReportJson(jsonReportPath, report);
            Console.WriteLine($"Scenario ledger saved: {jsonReportPath}");
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to save scenario artifacts '{reportPath}': {ex.Message}");
            failed = true;
        }

        var passed = turnRuns.Count(static t => t.Success);
        var total = turnRuns.Count;
        Console.WriteLine($"Scenario summary: {passed}/{total} turns passed.");
        return failed ? 1 : 0;
    }

    private static string BuildScenarioTurnPrompt(ChatScenarioTurn turn) {
        if (turn is null) {
            return string.Empty;
        }

        var minToolCalls = Math.Max(0, turn.MinToolCalls ?? 0);
        var minToolRounds = Math.Max(0, turn.MinToolRounds ?? 0);
        var forbidsAllTools = turn.ForbidTools.Any(static value => string.Equals((value ?? string.Empty).Trim(), "*", StringComparison.Ordinal));
        var requiresToolExecution = minToolCalls > 0
                                    || minToolRounds > 0
                                    || turn.RequireTools.Count > 0
                                    || turn.RequireAnyTools.Count > 0
                                    || turn.MinDistinctToolInputValues.Count > 0
                                    || turn.ForbidToolInputValues.Count > 0;
        var requiresNoToolExecution = forbidsAllTools && !requiresToolExecution;
        var requiresTimestampShape = turn.AssertContains.Any(static value => value.Contains("UTC", StringComparison.OrdinalIgnoreCase))
                                     || turn.AssertMatchesRegex.Any(static value => value.Contains(@"\d{4}-\d{2}-\d{2}", StringComparison.Ordinal));
        var requiresEventLogTool = TurnRequiresToolPrefix(turn, "eventlog_");
        var requiresDomainDetectiveTool = TurnRequiresToolPrefix(turn, "domaindetective_");
        if (!requiresToolExecution && !requiresNoToolExecution) {
            return turn.User;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[Scenario execution contract]");
        sb.AppendLine("ix:scenario-execution:v1");
        sb.AppendLine($"requires_tool_execution: {FormatScenarioContractBool(requiresToolExecution)}");
        sb.AppendLine($"requires_no_tool_execution: {FormatScenarioContractBool(requiresNoToolExecution)}");
        sb.AppendLine($"min_tool_calls: {minToolCalls}");
        sb.AppendLine($"min_tool_rounds: {minToolRounds}");
        sb.AppendLine($"required_tools_all: {FormatScenarioContractCsv(turn.RequireTools)}");
        sb.AppendLine($"required_tools_any: {FormatScenarioContractCsv(turn.RequireAnyTools)}");
        sb.AppendLine($"forbidden_tools: {FormatScenarioContractCsv(turn.ForbidTools)}");
        sb.AppendLine($"distinct_tool_inputs: {FormatScenarioContractDistinctInputRequirements(turn.MinDistinctToolInputValues)}");
        sb.AppendLine($"forbidden_tool_inputs: {FormatScenarioContractForbiddenInputRequirements(turn.ForbidToolInputValues)}");
        if (requiresNoToolExecution) {
            sb.AppendLine("This scenario turn requires a response without tool execution.");
            sb.AppendLine("- Do not execute any tools in this turn.");
            sb.AppendLine("- Resolve ambiguity or provide the requested summary directly from current context.");
            if (turn.AssertNoQuestions) {
                sb.AppendLine("- Do not ask any follow-up questions in the final response for this turn.");
                sb.AppendLine("- Do not include question-mark punctuation (`?`, `？`, `¿`, `؟`) anywhere in the final response.");
            }
            if (turn.AssertContains.Count > 0) {
                sb.AppendLine("- Final response must include these literals: " + string.Join(", ", turn.AssertContains) + ".");
            }
            if (turn.AssertContainsAny.Count > 0) {
                sb.AppendLine("- Final response must include at least one of these literals: " + string.Join(", ", turn.AssertContainsAny) + ".");
            }
            if (turn.AssertNotContains.Count > 0) {
                sb.AppendLine("- Final response must NOT include these literals (do not repeat them, even to negate them): " + string.Join(", ", turn.AssertNotContains) + ".");
            }
            sb.AppendLine();
            sb.AppendLine("User request:");
            sb.AppendLine(turn.User);
            return sb.ToString();
        }

        sb.AppendLine("This scenario turn requires tool execution before the final response.");

        if (minToolCalls > 0) {
            sb.AppendLine($"- Minimum tool calls in this turn: {minToolCalls}.");
        }

        if (minToolRounds > 0) {
            sb.AppendLine($"- Minimum tool rounds in this turn: {minToolRounds}.");
        }

        if (turn.RequireTools.Count > 0) {
            sb.AppendLine("- Required tool calls (all): " + string.Join(", ", turn.RequireTools) + ".");
        }

        if (turn.RequireAnyTools.Count > 0) {
            sb.AppendLine("- Required tool calls (at least one): " + string.Join(", ", turn.RequireAnyTools) + ".");
        }

        if (turn.ForbidTools.Count > 0) {
            sb.AppendLine("- Forbidden tool calls: " + string.Join(", ", turn.ForbidTools) + ".");
        }

        if (turn.MinDistinctToolInputValues.Count > 0) {
            var requirements = turn.MinDistinctToolInputValues
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => pair.Key + ">=" + Math.Max(0, pair.Value))
                .ToArray();
            sb.AppendLine("- Distinct tool input value requirements: " + string.Join(", ", requirements) + ".");
        }

        if (turn.ForbidToolInputValues.Count > 0) {
            var requirements = turn.ForbidToolInputValues
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => pair.Key + "!=" + string.Join("|", pair.Value
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)))
                .Where(static requirement => !requirement.EndsWith("!=", StringComparison.Ordinal))
                .ToArray();
            if (requirements.Length > 0) {
                sb.AppendLine("- Forbidden tool input values: " + string.Join(", ", requirements) + ".");
            }
        }

        sb.AppendLine("- Do not ask for permission/confirmation before the first required tool call.");
        sb.AppendLine("- Hard requirement: execute at least one qualifying tool call before any narrative prose in this turn.");
        sb.AppendLine("- Make at least one best-effort qualifying tool call in this turn, then summarize results.");
        sb.AppendLine("- Required tool-call constraints are strict for this turn: execute at least one qualifying required tool before final response.");
        sb.AppendLine("- *_pack_info tools are orientation-only and do not satisfy required tool-call assertions unless explicitly listed.");
        sb.AppendLine("- Infer missing read-only inputs from prior tool outputs when reasonably available.");
        sb.AppendLine("- If time window is missing and needed for read-only evidence correlation, default to last_24_hours_utc.");
        sb.AppendLine("- Do not use blocker-preface phrasing like \"I can do that, but\"; execute best-effort tools first.");
        sb.AppendLine("- When timestamps are requested, use strict ISO-8601 UTC with T and trailing Z (for example 2026-02-24T17:20:10Z), and include the exact uppercase token 'UTC' at least once.");
        sb.AppendLine("- For optional projection arguments (columns/sort_by), use only supported fields; if uncertain, omit projection arguments.");
        sb.AppendLine("- For eventlog_named_events_query, use names from eventlog_named_events_catalog; if uncertain, prefer eventlog_live_query with explicit event_ids.");
        if (turn.AssertNoQuestions) {
            sb.AppendLine("- Do not ask any follow-up questions in the final response for this turn.");
            sb.AppendLine("- Do not include question-mark punctuation (`?`, `？`, `¿`, `؟`) anywhere in the final response.");
        }
        if (requiresTimestampShape) {
            sb.AppendLine("- Hard requirement: final response must include at least one timestamp matching regex \\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2} and include 'UTC'.");
            sb.AppendLine("- If evidence rows are empty, include the queried UTC window boundaries in strict ISO-8601 (T + Z) so timestamp shape is still present.");
            sb.AppendLine("- If the first query returns no matching evidence, automatically broaden lookback once before finalizing.");
        }
        if (requiresEventLogTool) {
            sb.AppendLine("- If Event Log machine_name is missing, default to the first discovered/source DC from prior turns.");
            sb.AppendLine("- eventlog_pack_info alone is insufficient; execute at least one eventlog_*query* or eventlog_*stats* call in this turn.");
        }
        if (requiresDomainDetectiveTool) {
            sb.AppendLine("- For domaindetective_domain_summary checks[], use only supported check names (for example DNSHEALTH, SOA, NS, MX, SPF, DMARC, DKIM, DNSSEC, TTL, CAA).");
            sb.AppendLine("- Do not invent check names like NameServers; use NS.");
        }
        if (turn.AssertContains.Count > 0) {
            sb.AppendLine("- Final response must include these literals: " + string.Join(", ", turn.AssertContains) + ".");
        }
        if (turn.AssertContainsAny.Count > 0) {
            sb.AppendLine("- Final response must include at least one of these literals: " + string.Join(", ", turn.AssertContainsAny) + ".");
        }
        if (turn.AssertNotContains.Count > 0) {
            sb.AppendLine("- Final response must NOT include these literals (do not repeat them, even to negate them): " + string.Join(", ", turn.AssertNotContains) + ".");
        }
        sb.AppendLine("If a best-effort tool call fails, include the exact blocker/error and minimal missing input once.");
        sb.AppendLine();
        sb.AppendLine("User request:");
        sb.AppendLine(turn.User);
        return sb.ToString();
    }

    private static string FormatScenarioContractBool(bool value) {
        return value ? "true" : "false";
    }

    private static string FormatScenarioContractCsv(IReadOnlyList<string> values) {
        if (values is null || values.Count == 0) {
            return "none";
        }

        var normalized = values
            .Select(static value => (value ?? string.Empty).Trim())
            .Where(static value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) {
            return "none";
        }

        return string.Join(", ", normalized);
    }

    private static string FormatScenarioContractForbiddenInputRequirements(IReadOnlyDictionary<string, IReadOnlyList<string>> requirements) {
        if (requirements is null || requirements.Count == 0) {
            return "none";
        }

        var normalized = requirements
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => {
                var values = pair.Value
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return values.Length == 0
                    ? string.Empty
                    : pair.Key + "!=" + string.Join("|", values);
            })
            .Where(static value => value.Length > 0)
            .ToArray();
        if (normalized.Length == 0) {
            return "none";
        }

        return string.Join(", ", normalized);
    }

    private static string FormatScenarioContractDistinctInputRequirements(IReadOnlyDictionary<string, int> requirements) {
        if (requirements is null || requirements.Count == 0) {
            return "none";
        }

        var normalized = requirements
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => pair.Key + ">=" + Math.Max(0, pair.Value))
            .ToArray();
        if (normalized.Length == 0) {
            return "none";
        }

        return string.Join(", ", normalized);
    }

    private static bool TurnRequiresToolPrefix(ChatScenarioTurn turn, string prefix) {
        if (turn is null || string.IsNullOrWhiteSpace(prefix)) {
            return false;
        }

        static bool MatchesPrefix(IReadOnlyList<string> values, string expectedPrefix) {
            for (var i = 0; i < values.Count; i++) {
                var value = (values[i] ?? string.Empty).Trim();
                if (value.Length == 0) {
                    continue;
                }

                var normalized = value;
                if (normalized.EndsWith("*", StringComparison.Ordinal)) {
                    normalized = normalized.Substring(0, normalized.Length - 1);
                }

                if (normalized.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        return MatchesPrefix(turn.RequireTools, prefix)
               || MatchesPrefix(turn.RequireAnyTools, prefix);
    }

    private static bool TurnHasToolContract(
        int? minToolCalls,
        int? minToolRounds,
        IReadOnlyList<string> requireTools,
        IReadOnlyList<string> requireAnyTools,
        IReadOnlyDictionary<string, int> minDistinctToolInputValues,
        IReadOnlyDictionary<string, IReadOnlyList<string>> forbidToolInputValues,
        IReadOnlyList<string> assertToolOutputContains,
        IReadOnlyList<string> assertToolOutputNotContains,
        bool assertNoToolErrors,
        IReadOnlyList<string> forbidToolErrorCodes) {
        return Math.Max(0, minToolCalls ?? 0) > 0
               || Math.Max(0, minToolRounds ?? 0) > 0
               || requireTools.Count > 0
               || requireAnyTools.Count > 0
               || minDistinctToolInputValues.Count > 0
               || forbidToolInputValues.Count > 0
               || assertToolOutputContains.Count > 0
               || assertToolOutputNotContains.Count > 0
               || assertNoToolErrors
               || forbidToolErrorCodes.Count > 0;
    }

}
