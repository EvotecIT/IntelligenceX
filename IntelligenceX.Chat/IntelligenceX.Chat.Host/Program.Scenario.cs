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
    private static readonly string[] PartialCompletionMarkers = {
        "partial response shown above",
        "turn ended before completion",
        "chat failed:",
        "[execution blocked]",
        "no tool call found for custom tool call output with call_id",
        "no tool output found for function call",
        "unknown parameter: 'input[",
        "(chat_failed)"
    };

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
        var requiresToolExecution = minToolCalls > 0
                                    || minToolRounds > 0
                                    || turn.RequireTools.Count > 0
                                    || turn.RequireAnyTools.Count > 0
                                    || turn.MinDistinctToolInputValues.Count > 0;
        var requiresTimestampShape = turn.AssertContains.Any(static value => value.Contains("UTC", StringComparison.OrdinalIgnoreCase))
                                     || turn.AssertMatchesRegex.Any(static value => value.Contains(@"\d{4}-\d{2}-\d{2}", StringComparison.Ordinal));
        var requiresEventLogTool = TurnRequiresToolPrefix(turn, "eventlog_");
        if (!requiresToolExecution) {
            return turn.User;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[Scenario execution contract]");
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

        sb.AppendLine("- Do not ask for permission/confirmation before the first required tool call.");
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
        if (turn.AssertContains.Count > 0) {
            sb.AppendLine("- Final response must include these literals: " + string.Join(", ", turn.AssertContains) + ".");
        }
        sb.AppendLine("If a best-effort tool call fails, include the exact blocker/error and minimal missing input once.");
        sb.AppendLine();
        sb.AppendLine("User request:");
        sb.AppendLine(turn.User);
        return sb.ToString();
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
        IReadOnlyList<string> assertToolOutputContains,
        IReadOnlyList<string> assertToolOutputNotContains,
        bool assertNoToolErrors,
        IReadOnlyList<string> forbidToolErrorCodes) {
        return Math.Max(0, minToolCalls ?? 0) > 0
               || Math.Max(0, minToolRounds ?? 0) > 0
               || requireTools.Count > 0
               || requireAnyTools.Count > 0
               || minDistinctToolInputValues.Count > 0
               || assertToolOutputContains.Count > 0
               || assertToolOutputNotContains.Count > 0
               || assertNoToolErrors
               || forbidToolErrorCodes.Count > 0;
    }

    private static ChatScenarioDefinition LoadChatScenarioDefinition(string scenarioPath) {
        if (string.IsNullOrWhiteSpace(scenarioPath)) {
            throw new ArgumentException("Scenario path cannot be empty.", nameof(scenarioPath));
        }
        if (!File.Exists(scenarioPath)) {
            throw new FileNotFoundException("Scenario file was not found.", scenarioPath);
        }

        var raw = File.ReadAllText(scenarioPath);
        var fallbackName = Path.GetFileNameWithoutExtension(scenarioPath);
        return ParseChatScenarioDefinition(raw, fallbackName);
    }

    private static ChatScenarioDefinition ParseChatScenarioDefinition(string raw, string fallbackName) {
        var text = raw ?? string.Empty;
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal)) {
            return ParseChatScenarioDefinitionFromJson(text, fallbackName);
        }

        var turns = ParseChatScenarioTurnsFromText(text);
        return new ChatScenarioDefinition(string.IsNullOrWhiteSpace(fallbackName) ? "scenario" : fallbackName, turns);
    }

    private static ChatScenarioDefinition ParseChatScenarioDefinitionFromJson(string json, string fallbackName) {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        string scenarioName;
        IReadOnlyList<ChatScenarioTurn> turns;
        if (root.ValueKind == JsonValueKind.Object) {
            scenarioName = root.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString() ?? string.Empty
                : fallbackName;
            var defaults = root.TryGetProperty("defaults", out var defaultsElement)
                ? ReadScenarioDefaults(defaultsElement)
                : ChatScenarioDefaults.None;
            if (!root.TryGetProperty("turns", out var turnsElement) || turnsElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Scenario JSON object must include a 'turns' array.");
            }
            turns = ParseChatScenarioTurnsFromJsonArray(turnsElement, defaults);
        } else if (root.ValueKind == JsonValueKind.Array) {
            scenarioName = fallbackName;
            turns = ParseChatScenarioTurnsFromJsonArray(root, ChatScenarioDefaults.None);
        } else {
            throw new InvalidOperationException("Scenario JSON must be an object or an array.");
        }

        if (turns.Count == 0) {
            throw new InvalidOperationException("Scenario does not include any turns.");
        }

        return new ChatScenarioDefinition(
            string.IsNullOrWhiteSpace(scenarioName) ? "scenario" : scenarioName.Trim(),
            turns);
    }

    private static ChatScenarioDefaults ReadScenarioDefaults(JsonElement element) {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined) {
            return ChatScenarioDefaults.None;
        }
        if (element.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException("Scenario 'defaults' must be an object.");
        }

        return new ChatScenarioDefaults(
            assertCleanCompletion: ReadScenarioOptionalNullableBoolean(element, "assert_clean_completion"),
            assertToolCallOutputPairing: ReadScenarioOptionalNullableBoolean(element, "assert_tool_call_output_pairing"),
            assertNoDuplicateToolCallIds: ReadScenarioOptionalNullableBoolean(element, "assert_no_duplicate_tool_call_ids"),
            assertNoDuplicateToolOutputCallIds: ReadScenarioOptionalNullableBoolean(element, "assert_no_duplicate_tool_output_call_ids"),
            maxNoToolExecutionRetries: ReadScenarioOptionalNonNegativeInt(element, "max_no_tool_execution_retries"),
            maxDuplicateToolCallSignatures: ReadScenarioOptionalNonNegativeInt(element, "max_duplicate_tool_call_signatures"));
    }

    private static IReadOnlyList<ChatScenarioTurn> ParseChatScenarioTurnsFromJsonArray(JsonElement arrayElement, ChatScenarioDefaults defaults) {
        var turns = new List<ChatScenarioTurn>();
        var effectiveDefaults = defaults ?? ChatScenarioDefaults.None;
        var turnIndex = 0;
        foreach (var element in arrayElement.EnumerateArray()) {
            turnIndex++;
            if (element.ValueKind == JsonValueKind.String) {
                var userText = (element.GetString() ?? string.Empty).Trim();
                if (userText.Length == 0) {
                    continue;
                }
                turns.Add(CreateScenarioTurn(
                    name: null,
                    user: userText,
                    assertContains: Array.Empty<string>(),
                    assertNotContains: Array.Empty<string>(),
                    assertMatchesRegex: Array.Empty<string>(),
                    assertNoQuestions: false,
                    minToolCalls: null,
                    minToolRounds: null,
                    requireTools: Array.Empty<string>(),
                    requireAnyTools: Array.Empty<string>(),
                    forbidTools: Array.Empty<string>(),
                    minDistinctToolInputValues: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                    assertToolOutputContains: Array.Empty<string>(),
                    assertToolOutputNotContains: Array.Empty<string>(),
                    assertNoToolErrors: false,
                    forbidToolErrorCodes: Array.Empty<string>(),
                    defaults: effectiveDefaults));
                continue;
            }

            if (element.ValueKind != JsonValueKind.Object) {
                throw new InvalidOperationException($"Scenario turn #{turnIndex} must be a string or object.");
            }

            var user = ReadScenarioUserText(element);
            if (string.IsNullOrWhiteSpace(user)) {
                throw new InvalidOperationException($"Scenario turn #{turnIndex} is missing user text.");
            }

            var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()
                : null;
            var assertContains = ReadScenarioAssertContains(element);
            var assertNotContains = ReadScenarioAssertNotContains(element);
            var assertMatchesRegex = ReadScenarioStringList(element, "assert_matches_regex");
            var assertNoQuestions = ReadScenarioOptionalBoolean(element, "assert_no_questions", defaultValue: false);
            var minToolCalls = ReadScenarioOptionalNonNegativeInt(element, "min_tool_calls");
            var minToolRounds = ReadScenarioOptionalNonNegativeInt(element, "min_tool_rounds");
            var requireTools = ReadScenarioStringList(element, "require_tools");
            var requireAnyTools = ReadScenarioStringList(element, "require_any_tools");
            var forbidTools = ReadScenarioStringList(element, "forbid_tools");
            var minDistinctToolInputValues = ReadScenarioMinDistinctToolInputValues(element, "min_distinct_tool_input_values");
            var assertToolOutputContains = ReadScenarioStringList(element, "assert_tool_output_contains");
            var assertToolOutputNotContains = ReadScenarioStringList(element, "assert_tool_output_not_contains");
            var assertNoToolErrors = ReadScenarioOptionalBoolean(element, "assert_no_tool_errors", defaultValue: false);
            var forbidToolErrorCodes = ReadScenarioStringList(element, "forbid_tool_error_codes");
            var hasToolContract = TurnHasToolContract(
                minToolCalls,
                minToolRounds,
                requireTools,
                requireAnyTools,
                minDistinctToolInputValues,
                assertToolOutputContains,
                assertToolOutputNotContains,
                assertNoToolErrors,
                forbidToolErrorCodes);
            var assertCleanCompletionDefault = effectiveDefaults.AssertCleanCompletion ?? true;
            var assertCleanCompletion = ReadScenarioOptionalBoolean(element, "assert_clean_completion", defaultValue: assertCleanCompletionDefault);
            var assertToolCallOutputPairing = ReadScenarioOptionalBoolean(
                element,
                "assert_tool_call_output_pairing",
                defaultValue: effectiveDefaults.AssertToolCallOutputPairing ?? hasToolContract);
            var assertNoDuplicateToolCallIds = ReadScenarioOptionalBoolean(
                element,
                "assert_no_duplicate_tool_call_ids",
                defaultValue: effectiveDefaults.AssertNoDuplicateToolCallIds ?? hasToolContract);
            var assertNoDuplicateToolOutputCallIds = ReadScenarioOptionalBoolean(
                element,
                "assert_no_duplicate_tool_output_call_ids",
                defaultValue: effectiveDefaults.AssertNoDuplicateToolOutputCallIds ?? hasToolContract);
            var maxNoToolExecutionRetries = ReadScenarioOptionalNonNegativeInt(element, "max_no_tool_execution_retries")
                                            ?? effectiveDefaults.MaxNoToolExecutionRetries
                                            ?? (hasToolContract ? 0 : null);
            var maxDuplicateToolCallSignatures = ReadScenarioOptionalNonNegativeInt(element, "max_duplicate_tool_call_signatures")
                                                 ?? effectiveDefaults.MaxDuplicateToolCallSignatures
                                                 ?? (hasToolContract ? 1 : null);
            turns.Add(new ChatScenarioTurn(
                name,
                user.Trim(),
                assertContains,
                assertNotContains,
                assertMatchesRegex,
                assertNoQuestions,
                minToolCalls,
                minToolRounds,
                requireTools,
                requireAnyTools,
                forbidTools,
                minDistinctToolInputValues,
                assertToolOutputContains,
                assertToolOutputNotContains,
                assertNoToolErrors,
                forbidToolErrorCodes,
                assertCleanCompletion,
                assertToolCallOutputPairing,
                assertNoDuplicateToolCallIds,
                assertNoDuplicateToolOutputCallIds,
                maxNoToolExecutionRetries,
                maxDuplicateToolCallSignatures));
        }

        return turns;
    }

    private static IReadOnlyList<ChatScenarioTurn> ParseChatScenarioTurnsFromText(string text) {
        var turns = new List<ChatScenarioTurn>();
        var lines = (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var candidate = lines[i].Trim();
            if (candidate.Length == 0) {
                continue;
            }
            if (candidate.StartsWith("#", StringComparison.Ordinal) || candidate.StartsWith("//", StringComparison.Ordinal)) {
                continue;
            }
            if (candidate.StartsWith("- ", StringComparison.Ordinal)) {
                candidate = candidate.Substring(2).Trim();
            }
            if (candidate.Length == 0) {
                continue;
            }
            turns.Add(CreateScenarioTurn(
                name: $"Turn {turns.Count + 1}",
                user: candidate,
                assertContains: Array.Empty<string>(),
                assertNotContains: Array.Empty<string>(),
                assertMatchesRegex: Array.Empty<string>(),
                assertNoQuestions: false,
                minToolCalls: null,
                minToolRounds: null,
                requireTools: Array.Empty<string>(),
                requireAnyTools: Array.Empty<string>(),
                forbidTools: Array.Empty<string>(),
                minDistinctToolInputValues: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                assertToolOutputContains: Array.Empty<string>(),
                assertToolOutputNotContains: Array.Empty<string>(),
                assertNoToolErrors: false,
                forbidToolErrorCodes: Array.Empty<string>(),
                defaults: ChatScenarioDefaults.None));
        }
        return turns;
    }

    private static ChatScenarioTurn CreateScenarioTurn(
        string? name,
        string user,
        IReadOnlyList<string> assertContains,
        IReadOnlyList<string> assertNotContains,
        IReadOnlyList<string> assertMatchesRegex,
        bool assertNoQuestions,
        int? minToolCalls,
        int? minToolRounds,
        IReadOnlyList<string> requireTools,
        IReadOnlyList<string> requireAnyTools,
        IReadOnlyList<string> forbidTools,
        IReadOnlyDictionary<string, int> minDistinctToolInputValues,
        IReadOnlyList<string> assertToolOutputContains,
        IReadOnlyList<string> assertToolOutputNotContains,
        bool assertNoToolErrors,
        IReadOnlyList<string> forbidToolErrorCodes,
        ChatScenarioDefaults defaults) {
        var hasToolContract = TurnHasToolContract(
            minToolCalls,
            minToolRounds,
            requireTools,
            requireAnyTools,
            minDistinctToolInputValues,
            assertToolOutputContains,
            assertToolOutputNotContains,
            assertNoToolErrors,
            forbidToolErrorCodes);
        var effectiveDefaults = defaults ?? ChatScenarioDefaults.None;
        return new ChatScenarioTurn(
            name,
            user,
            assertContains,
            assertNotContains,
            assertMatchesRegex,
            assertNoQuestions,
            minToolCalls,
            minToolRounds,
            requireTools,
            requireAnyTools,
            forbidTools,
            minDistinctToolInputValues,
            assertToolOutputContains,
            assertToolOutputNotContains,
            assertNoToolErrors,
            forbidToolErrorCodes,
            assertCleanCompletion: effectiveDefaults.AssertCleanCompletion ?? true,
            assertToolCallOutputPairing: effectiveDefaults.AssertToolCallOutputPairing ?? hasToolContract,
            assertNoDuplicateToolCallIds: effectiveDefaults.AssertNoDuplicateToolCallIds ?? hasToolContract,
            assertNoDuplicateToolOutputCallIds: effectiveDefaults.AssertNoDuplicateToolOutputCallIds ?? hasToolContract,
            maxNoToolExecutionRetries: effectiveDefaults.MaxNoToolExecutionRetries ?? (hasToolContract ? 0 : null),
            maxDuplicateToolCallSignatures: effectiveDefaults.MaxDuplicateToolCallSignatures ?? (hasToolContract ? 1 : null));
    }

    private static string ReadScenarioUserText(JsonElement element) {
        if (element.TryGetProperty("user", out var userElement) && userElement.ValueKind == JsonValueKind.String) {
            return userElement.GetString() ?? string.Empty;
        }
        if (element.TryGetProperty("prompt", out var promptElement) && promptElement.ValueKind == JsonValueKind.String) {
            return promptElement.GetString() ?? string.Empty;
        }
        if (element.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String) {
            return textElement.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static IReadOnlyList<string> ReadScenarioAssertContains(JsonElement element) {
        return ReadScenarioStringList(element, "assert_contains");
    }

    private static IReadOnlyList<string> ReadScenarioAssertNotContains(JsonElement element) {
        return ReadScenarioStringList(element, "assert_not_contains");
    }

    private static IReadOnlyList<string> ReadScenarioStringList(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var valueElement)) {
            return Array.Empty<string>();
        }

        if (valueElement.ValueKind == JsonValueKind.String) {
            var single = (valueElement.GetString() ?? string.Empty).Trim();
            return single.Length == 0 ? Array.Empty<string>() : new[] { single };
        }

        if (valueElement.ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException($"'{propertyName}' must be a string or array of strings.");
        }

        var assertions = new List<string>();
        foreach (var item in valueElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.String) {
                throw new InvalidOperationException($"'{propertyName}' array must contain only strings.");
            }

            var value = (item.GetString() ?? string.Empty).Trim();
            if (value.Length > 0) {
                assertions.Add(value);
            }
        }
        return assertions;
    }

    private static IReadOnlyDictionary<string, int> ReadScenarioMinDistinctToolInputValues(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var valueElement)) {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        if (valueElement.ValueKind == JsonValueKind.Null || valueElement.ValueKind == JsonValueKind.Undefined) {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        if (valueElement.ValueKind != JsonValueKind.Object) {
            throw new InvalidOperationException($"'{propertyName}' must be an object mapping input keys to integers >= 0.");
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in valueElement.EnumerateObject()) {
            var key = (property.Name ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            int parsed;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var numberValue)) {
                parsed = numberValue;
            } else if (property.Value.ValueKind == JsonValueKind.String
                       && int.TryParse(property.Value.GetString(), out var stringValue)) {
                parsed = stringValue;
            } else {
                throw new InvalidOperationException($"'{propertyName}.{key}' must be an integer >= 0.");
            }

            if (parsed < 0) {
                throw new InvalidOperationException($"'{propertyName}.{key}' must be >= 0.");
            }

            result[key] = parsed;
        }

        return result;
    }

    private static int? ReadScenarioOptionalNonNegativeInt(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var intElement)) {
            return null;
        }

        if (intElement.ValueKind == JsonValueKind.Number && intElement.TryGetInt32(out var numberValue)) {
            if (numberValue < 0) {
                throw new InvalidOperationException($"'{propertyName}' must be >= 0.");
            }
            return numberValue;
        }

        if (intElement.ValueKind == JsonValueKind.String
            && int.TryParse(intElement.GetString(), out var stringValue)) {
            if (stringValue < 0) {
                throw new InvalidOperationException($"'{propertyName}' must be >= 0.");
            }
            return stringValue;
        }

        throw new InvalidOperationException($"'{propertyName}' must be an integer >= 0.");
    }

    private static bool ReadScenarioOptionalBoolean(JsonElement element, string propertyName, bool defaultValue) {
        if (!element.TryGetProperty(propertyName, out var boolElement) || boolElement.ValueKind == JsonValueKind.Null) {
            return defaultValue;
        }

        if (boolElement.ValueKind == JsonValueKind.True) {
            return true;
        }
        if (boolElement.ValueKind == JsonValueKind.False) {
            return false;
        }

        throw new InvalidOperationException($"'{propertyName}' must be a boolean.");
    }

    private static bool? ReadScenarioOptionalNullableBoolean(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var boolElement) || boolElement.ValueKind == JsonValueKind.Null) {
            return null;
        }
        if (boolElement.ValueKind == JsonValueKind.True) {
            return true;
        }
        if (boolElement.ValueKind == JsonValueKind.False) {
            return false;
        }

        throw new InvalidOperationException($"'{propertyName}' must be a boolean.");
    }

    private static List<string> EvaluateScenarioAssertions(ChatScenarioTurn turn, ReplTurnMetricsResult? turnResult) {
        var failures = new List<string>();
        var assistantText = turnResult?.Result.Text ?? string.Empty;
        var toolCalls = turnResult?.Result.ToolCalls ?? Array.Empty<ToolCall>();
        var toolOutputs = turnResult?.Result.ToolOutputs ?? Array.Empty<ToolOutput>();
        var noToolExecutionRetries = turnResult?.Result.NoToolExecutionRetries ?? 0;
        var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var toolName = (toolCalls[i].Name ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                toolNames.Add(toolName);
            }
        }
        var (toolErrorCount, toolErrorCodes) = SummarizeToolOutputErrors(toolOutputs);
        var toolCallIdCounts = BuildToolCallIdCounts(toolCalls);
        var toolOutputCallIdCounts = BuildToolOutputCallIdCounts(toolOutputs);
        var toolCallSignatureCounts = BuildToolCallSignatureCounts(toolCalls);

        if (turn.AssertCleanCompletion && ContainsPartialCompletionMarker(assistantText)) {
            failures.Add("Expected clean completion, but assistant output contains partial/transport failure markers.");
        }

        foreach (var expected in turn.AssertContains) {
            if (assistantText.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0) {
                continue;
            }
            failures.Add($"Expected assistant output to contain '{expected}'.");
        }

        foreach (var disallowed in turn.AssertNotContains) {
            if (assistantText.IndexOf(disallowed, StringComparison.OrdinalIgnoreCase) < 0) {
                continue;
            }

            failures.Add($"Expected assistant output to not contain '{disallowed}'.");
        }

        foreach (var pattern in turn.AssertMatchesRegex) {
            try {
                if (Regex.IsMatch(assistantText, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) {
                    continue;
                }

                failures.Add($"Expected assistant output to match regex '{pattern}'.");
            } catch (ArgumentException ex) {
                failures.Add($"Invalid regex in assert_matches_regex '{pattern}': {ex.Message}");
            }
        }

        if (turn.AssertNoQuestions && ContainsQuestionSignal(assistantText)) {
            failures.Add("Expected assistant output to not contain question markers.");
        }

        var toolCallsCount = turnResult?.Metrics.ToolCallsCount ?? 0;
        if (turn.MinToolCalls.HasValue && toolCallsCount < turn.MinToolCalls.Value) {
            failures.Add($"Expected at least {turn.MinToolCalls.Value} tool call(s); observed {toolCallsCount}.");
        }

        var toolRounds = turnResult?.Metrics.ToolRounds ?? 0;
        if (turn.MinToolRounds.HasValue && toolRounds < turn.MinToolRounds.Value) {
            failures.Add($"Expected at least {turn.MinToolRounds.Value} tool round(s); observed {toolRounds}.");
        }

        if (turn.MinDistinctToolInputValues.Count > 0) {
            foreach (var requirement in turn.MinDistinctToolInputValues) {
                var inputKey = requirement.Key;
                var minDistinct = Math.Max(0, requirement.Value);
                if (minDistinct == 0) {
                    continue;
                }

                var observedValues = CollectDistinctToolInputValuesByKey(toolCalls, inputKey);
                if (observedValues.Count >= minDistinct) {
                    continue;
                }

                failures.Add(
                    $"Expected at least {minDistinct} distinct '{inputKey}' tool input value(s); observed {observedValues.Count}."
                    + " Values: " + FormatValuesForAssertion(observedValues.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray()) + ".");
            }
        }

        if (turn.MaxNoToolExecutionRetries.HasValue && noToolExecutionRetries > turn.MaxNoToolExecutionRetries.Value) {
            failures.Add(
                $"Expected at most {turn.MaxNoToolExecutionRetries.Value} no-tool execution retry attempt(s); observed {noToolExecutionRetries}.");
        }

        foreach (var requiredTool in turn.RequireTools) {
            if (ToolNameSetContains(toolNames, requiredTool)) {
                continue;
            }

            failures.Add($"Expected tool call '{requiredTool}' (exact or wildcard), but it was not executed.");
        }

        if (turn.RequireAnyTools.Count > 0) {
            var matchedAnyRequiredTool = false;
            foreach (var requiredTool in turn.RequireAnyTools) {
                if (!ToolNameSetContains(toolNames, requiredTool)) {
                    continue;
                }

                matchedAnyRequiredTool = true;
                break;
            }

            if (!matchedAnyRequiredTool) {
                failures.Add("Expected at least one of these tool calls: " + string.Join(", ", turn.RequireAnyTools) + ".");
            }
        }

        foreach (var forbiddenTool in turn.ForbidTools) {
            if (!ToolNameSetContains(toolNames, forbiddenTool)) {
                continue;
            }

            failures.Add($"Expected tool call '{forbiddenTool}' (exact or wildcard) to be absent, but it executed.");
        }

        foreach (var expected in turn.AssertToolOutputContains) {
            if (AnyToolOutputContains(toolOutputs, expected)) {
                continue;
            }

            failures.Add($"Expected tool output to contain '{expected}'.");
        }

        foreach (var disallowed in turn.AssertToolOutputNotContains) {
            if (!AnyToolOutputContains(toolOutputs, disallowed)) {
                continue;
            }

            failures.Add($"Expected tool output to not contain '{disallowed}'.");
        }

        if (turn.AssertNoToolErrors && toolErrorCount > 0) {
            var codeList = toolErrorCodes.Count == 0
                ? "-"
                : string.Join(", ", toolErrorCodes.OrderBy(static c => c, StringComparer.OrdinalIgnoreCase));
            failures.Add($"Expected no tool output errors, but observed {toolErrorCount} error output(s). Codes: {codeList}.");
        }

        foreach (var forbiddenErrorCode in turn.ForbidToolErrorCodes) {
            if (!ToolNameSetContains(toolErrorCodes, forbiddenErrorCode)) {
                continue;
            }

            failures.Add($"Expected tool error code '{forbiddenErrorCode}' (exact or wildcard) to be absent, but it was observed.");
        }

        if (turn.AssertNoDuplicateToolCallIds) {
            var duplicateCallIds = toolCallIdCounts
                .Where(static pair => pair.Value > 1)
                .Select(static pair => pair.Key)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (duplicateCallIds.Length > 0) {
                failures.Add("Expected unique tool call IDs, but duplicates were observed: " + FormatValuesForAssertion(duplicateCallIds) + ".");
            }
        }

        if (turn.AssertNoDuplicateToolOutputCallIds) {
            var duplicateOutputCallIds = toolOutputCallIdCounts
                .Where(static pair => pair.Value > 1)
                .Select(static pair => pair.Key)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (duplicateOutputCallIds.Length > 0) {
                failures.Add("Expected unique tool output call IDs, but duplicates were observed: " + FormatValuesForAssertion(duplicateOutputCallIds) + ".");
            }
        }

        if (turn.AssertToolCallOutputPairing) {
            var callIds = new HashSet<string>(toolCallIdCounts.Keys, StringComparer.OrdinalIgnoreCase);
            var outputCallIds = new HashSet<string>(toolOutputCallIdCounts.Keys, StringComparer.OrdinalIgnoreCase);
            callIds.Remove(string.Empty);
            outputCallIds.Remove(string.Empty);

            var missingOutputs = callIds
                .Where(id => !outputCallIds.Contains(id))
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (missingOutputs.Length > 0) {
                failures.Add("Expected a tool output for each tool call ID; missing output(s) for: " + FormatValuesForAssertion(missingOutputs) + ".");
            }

            var orphanOutputs = outputCallIds
                .Where(id => !callIds.Contains(id))
                .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (orphanOutputs.Length > 0) {
                failures.Add("Expected tool outputs to map to in-turn tool calls; orphan output ID(s): " + FormatValuesForAssertion(orphanOutputs) + ".");
            }

            var emptyOutputCallIds = toolOutputs.Count(static output => string.IsNullOrWhiteSpace(output.CallId));
            if (emptyOutputCallIds > 0) {
                failures.Add($"Expected tool output call_id to be present; observed {emptyOutputCallIds} output(s) without call_id.");
            }
        }

        if (turn.MaxDuplicateToolCallSignatures.HasValue) {
            var maxAllowed = Math.Max(0, turn.MaxDuplicateToolCallSignatures.Value);
            var duplicates = toolCallSignatureCounts
                .Where(pair => pair.Value > maxAllowed)
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key} x{pair.Value}")
                .ToArray();
            if (duplicates.Length > 0) {
                failures.Add(
                    $"Expected each tool call signature to appear at most {maxAllowed} time(s), but observed duplicates: "
                    + FormatValuesForAssertion(duplicates) + ".");
            }
        }

        return failures;
    }

    private static bool ContainsPartialCompletionMarker(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return false;
        }

        var value = text.Trim();
        for (var i = 0; i < PartialCompletionMarkers.Length; i++) {
            if (value.IndexOf(PartialCompletionMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, int> BuildToolCallIdCounts(IReadOnlyList<ToolCall> toolCalls) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var callId = (toolCalls[i].CallId ?? string.Empty).Trim();
            if (!counts.TryGetValue(callId, out var count)) {
                counts[callId] = 1;
                continue;
            }

            counts[callId] = count + 1;
        }

        return counts;
    }

    private static Dictionary<string, int> BuildToolOutputCallIdCounts(IReadOnlyList<ToolOutput> toolOutputs) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolOutputs.Count; i++) {
            var callId = (toolOutputs[i].CallId ?? string.Empty).Trim();
            if (!counts.TryGetValue(callId, out var count)) {
                counts[callId] = 1;
                continue;
            }

            counts[callId] = count + 1;
        }

        return counts;
    }

    private static Dictionary<string, int> BuildToolCallSignatureCounts(IReadOnlyList<ToolCall> toolCalls) {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var signature = BuildToolCallSignature(toolCalls[i]);
            if (signature.Length == 0) {
                continue;
            }

            if (!counts.TryGetValue(signature, out var count)) {
                counts[signature] = 1;
                continue;
            }

            counts[signature] = count + 1;
        }

        return counts;
    }

    private static HashSet<string> CollectDistinctToolInputValuesByKey(IReadOnlyList<ToolCall> toolCalls, string inputKey) {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (toolCalls.Count == 0 || string.IsNullOrWhiteSpace(inputKey)) {
            return values;
        }

        var normalizedKey = inputKey.Trim();
        for (var i = 0; i < toolCalls.Count; i++) {
            var args = toolCalls[i].Arguments;
            if (args is null) {
                continue;
            }

            if (!TryReadToolInputValueByKey(args, normalizedKey, out var value) || string.IsNullOrWhiteSpace(value)) {
                continue;
            }

            values.Add(value.Trim());
        }

        return values;
    }

    private static bool TryReadToolInputValueByKey(IxJsonObject arguments, string inputKey, out string value) {
        value = string.Empty;
        if (arguments is null || string.IsNullOrWhiteSpace(inputKey)) {
            return false;
        }

        var normalizedKey = inputKey.Trim();
        if (arguments.TryGetValue(normalizedKey, out var exactValue) && TryNormalizeToolInputValue(exactValue, out value)) {
            return true;
        }

        foreach (var pair in arguments) {
            if (!string.Equals(pair.Key, normalizedKey, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (TryNormalizeToolInputValue(pair.Value, out value)) {
                return true;
            }
        }

        return false;
    }

    private static bool TryNormalizeToolInputValue(IxJsonValue? value, out string normalized) {
        normalized = string.Empty;
        if (value is null) {
            return false;
        }

        switch (value.Kind) {
            case IxJsonValueKind.String:
                normalized = (value.AsString() ?? string.Empty).Trim();
                return normalized.Length > 0;
            case IxJsonValueKind.Number:
            case IxJsonValueKind.Boolean:
                normalized = value.ToString().Trim();
                return normalized.Length > 0;
            default:
                return false;
        }
    }

    private static string BuildToolCallSignature(ToolCall call) {
        var toolName = (call.Name ?? string.Empty).Trim();
        if (toolName.Length == 0) {
            return string.Empty;
        }

        var args = SerializeCanonicalToolArguments(call.Arguments);
        return toolName + " " + args;
    }

    private static string SerializeCanonicalToolArguments(IxJsonObject? arguments) {
        if (arguments is null) {
            return "{}";
        }

        var canonical = CanonicalizeJsonObject(arguments);
        return JsonLite.Serialize(canonical);
    }

    private static IxJsonObject CanonicalizeJsonObject(IxJsonObject source) {
        var result = new IxJsonObject(StringComparer.Ordinal);
        var keys = source
            .Select(static pair => pair.Key)
            .OrderBy(static key => key, StringComparer.Ordinal);
        foreach (var key in keys) {
            if (!source.TryGetValue(key, out var value) || value is null) {
                result.Add(key, IxJsonValue.Null);
                continue;
            }

            result.Add(key, CanonicalizeJsonValue(value));
        }

        return result;
    }

    private static IxJsonArray CanonicalizeJsonArray(IxJsonArray source) {
        var result = new IxJsonArray();
        foreach (var value in source) {
            result.Add(CanonicalizeJsonValue(value));
        }
        return result;
    }

    private static IxJsonValue CanonicalizeJsonValue(IxJsonValue value) {
        if (value is null) {
            return IxJsonValue.Null;
        }

        return value.Kind switch {
            IxJsonValueKind.Object => IxJsonValue.From(CanonicalizeJsonObject(value.AsObject() ?? new IxJsonObject(StringComparer.Ordinal))),
            IxJsonValueKind.Array => IxJsonValue.From(CanonicalizeJsonArray(value.AsArray() ?? new IxJsonArray())),
            _ => value
        };
    }

    private static string FormatValuesForAssertion(IReadOnlyList<string> values, int maxItems = 6) {
        if (values.Count == 0) {
            return "-";
        }

        var capped = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Take(Math.Max(1, maxItems))
            .ToArray();
        if (capped.Length == 0) {
            return "-";
        }

        var suffix = values.Count > capped.Length ? ", ..." : string.Empty;
        return string.Join(", ", capped) + suffix;
    }

    private static bool ToolNameSetContains(IReadOnlyCollection<string> toolNames, string expectedNameOrPattern) {
        if (toolNames.Count == 0) {
            return false;
        }

        var expected = (expectedNameOrPattern ?? string.Empty).Trim();
        if (expected.Length == 0) {
            return false;
        }

        foreach (var toolName in toolNames) {
            if (ToolNameMatches(toolName, expected)) {
                return true;
            }
        }

        return false;
    }

    private static bool ToolNameMatches(string actualToolName, string expectedNameOrPattern) {
        var actual = (actualToolName ?? string.Empty).Trim();
        var expected = (expectedNameOrPattern ?? string.Empty).Trim();
        if (actual.Length == 0 || expected.Length == 0) {
            return false;
        }

        if (!ContainsWildcard(expected)) {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        return WildcardMatches(actual, expected);
    }

    private static bool ContainsWildcard(string value) {
        return value.IndexOf('*') >= 0 || value.IndexOf('?') >= 0;
    }

    private static bool WildcardMatches(string value, string wildcardPattern) {
        var regexPattern = "^"
            + Regex.Escape(wildcardPattern)
                .Replace("\\*", ".*", StringComparison.Ordinal)
                .Replace("\\?", ".", StringComparison.Ordinal)
            + "$";
        return Regex.IsMatch(value, regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool AnyToolOutputContains(IReadOnlyList<ToolOutput> toolOutputs, string expected) {
        if (toolOutputs.Count == 0 || string.IsNullOrWhiteSpace(expected)) {
            return false;
        }

        for (var i = 0; i < toolOutputs.Count; i++) {
            var output = toolOutputs[i].Output ?? string.Empty;
            if (output.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsQuestionSignal(string text) {
        var value = text ?? string.Empty;
        return value.IndexOf('?', StringComparison.Ordinal) >= 0
               || value.IndexOf('？', StringComparison.Ordinal) >= 0
               || value.IndexOf('¿', StringComparison.Ordinal) >= 0
               || value.IndexOf('؟', StringComparison.Ordinal) >= 0;
    }

    private static (int ErrorCount, HashSet<string> ErrorCodes) SummarizeToolOutputErrors(IReadOnlyList<ToolOutput> toolOutputs) {
        var errorCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errorCount = 0;
        if (toolOutputs.Count == 0) {
            return (errorCount, errorCodes);
        }

        for (var i = 0; i < toolOutputs.Count; i++) {
            if (!TryReadToolOutputErrorCode(toolOutputs[i].Output, out var errorCode)) {
                continue;
            }

            errorCount++;
            if (errorCode.Length > 0) {
                errorCodes.Add(errorCode);
            }
        }

        return (errorCount, errorCodes);
    }

    private static bool TryReadToolOutputErrorCode(string output, out string errorCode) {
        errorCode = string.Empty;
        if (string.IsNullOrWhiteSpace(output)) {
            return false;
        }

        try {
            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.False) {
                return false;
            }

            if (root.TryGetProperty("error_code", out var errorCodeElement) && errorCodeElement.ValueKind == JsonValueKind.String) {
                errorCode = (errorCodeElement.GetString() ?? string.Empty).Trim();
            }

            return true;
        } catch (JsonException) {
            return false;
        }
    }

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

    private sealed class ChatScenarioDefinition {
        public ChatScenarioDefinition(string name, IReadOnlyList<ChatScenarioTurn> turns) {
            Name = string.IsNullOrWhiteSpace(name) ? "scenario" : name.Trim();
            Turns = turns ?? Array.Empty<ChatScenarioTurn>();
        }

        public string Name { get; }
        public IReadOnlyList<ChatScenarioTurn> Turns { get; }
    }

    private sealed class ChatScenarioTurn {
        public ChatScenarioTurn(
            string? name,
            string user,
            IReadOnlyList<string> assertContains,
            IReadOnlyList<string> assertNotContains,
            IReadOnlyList<string> assertMatchesRegex,
            bool assertNoQuestions,
            int? minToolCalls,
            int? minToolRounds,
            IReadOnlyList<string> requireTools,
            IReadOnlyList<string> requireAnyTools,
            IReadOnlyList<string> forbidTools,
            IReadOnlyDictionary<string, int> minDistinctToolInputValues,
            IReadOnlyList<string> assertToolOutputContains,
            IReadOnlyList<string> assertToolOutputNotContains,
            bool assertNoToolErrors,
            IReadOnlyList<string> forbidToolErrorCodes,
            bool assertCleanCompletion,
            bool assertToolCallOutputPairing,
            bool assertNoDuplicateToolCallIds,
            bool assertNoDuplicateToolOutputCallIds,
            int? maxNoToolExecutionRetries,
            int? maxDuplicateToolCallSignatures) {
            Name = name;
            User = user ?? string.Empty;
            AssertContains = assertContains ?? Array.Empty<string>();
            AssertNotContains = assertNotContains ?? Array.Empty<string>();
            AssertMatchesRegex = assertMatchesRegex ?? Array.Empty<string>();
            AssertNoQuestions = assertNoQuestions;
            MinToolCalls = minToolCalls;
            MinToolRounds = minToolRounds;
            RequireTools = requireTools ?? Array.Empty<string>();
            RequireAnyTools = requireAnyTools ?? Array.Empty<string>();
            ForbidTools = forbidTools ?? Array.Empty<string>();
            MinDistinctToolInputValues = minDistinctToolInputValues ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            AssertToolOutputContains = assertToolOutputContains ?? Array.Empty<string>();
            AssertToolOutputNotContains = assertToolOutputNotContains ?? Array.Empty<string>();
            AssertNoToolErrors = assertNoToolErrors;
            ForbidToolErrorCodes = forbidToolErrorCodes ?? Array.Empty<string>();
            AssertCleanCompletion = assertCleanCompletion;
            AssertToolCallOutputPairing = assertToolCallOutputPairing;
            AssertNoDuplicateToolCallIds = assertNoDuplicateToolCallIds;
            AssertNoDuplicateToolOutputCallIds = assertNoDuplicateToolOutputCallIds;
            MaxNoToolExecutionRetries = maxNoToolExecutionRetries;
            MaxDuplicateToolCallSignatures = maxDuplicateToolCallSignatures;
        }

        public string? Name { get; }
        public string User { get; }
        public IReadOnlyList<string> AssertContains { get; }
        public IReadOnlyList<string> AssertNotContains { get; }
        public IReadOnlyList<string> AssertMatchesRegex { get; }
        public bool AssertNoQuestions { get; }
        public int? MinToolCalls { get; }
        public int? MinToolRounds { get; }
        public IReadOnlyList<string> RequireTools { get; }
        public IReadOnlyList<string> RequireAnyTools { get; }
        public IReadOnlyList<string> ForbidTools { get; }
        public IReadOnlyDictionary<string, int> MinDistinctToolInputValues { get; }
        public IReadOnlyList<string> AssertToolOutputContains { get; }
        public IReadOnlyList<string> AssertToolOutputNotContains { get; }
        public bool AssertNoToolErrors { get; }
        public IReadOnlyList<string> ForbidToolErrorCodes { get; }
        public bool AssertCleanCompletion { get; }
        public bool AssertToolCallOutputPairing { get; }
        public bool AssertNoDuplicateToolCallIds { get; }
        public bool AssertNoDuplicateToolOutputCallIds { get; }
        public int? MaxNoToolExecutionRetries { get; }
        public int? MaxDuplicateToolCallSignatures { get; }
    }

    private sealed class ChatScenarioDefaults {
        public static ChatScenarioDefaults None { get; } = new(
            assertCleanCompletion: null,
            assertToolCallOutputPairing: null,
            assertNoDuplicateToolCallIds: null,
            assertNoDuplicateToolOutputCallIds: null,
            maxNoToolExecutionRetries: null,
            maxDuplicateToolCallSignatures: null);

        public ChatScenarioDefaults(
            bool? assertCleanCompletion,
            bool? assertToolCallOutputPairing,
            bool? assertNoDuplicateToolCallIds,
            bool? assertNoDuplicateToolOutputCallIds,
            int? maxNoToolExecutionRetries,
            int? maxDuplicateToolCallSignatures) {
            AssertCleanCompletion = assertCleanCompletion;
            AssertToolCallOutputPairing = assertToolCallOutputPairing;
            AssertNoDuplicateToolCallIds = assertNoDuplicateToolCallIds;
            AssertNoDuplicateToolOutputCallIds = assertNoDuplicateToolOutputCallIds;
            MaxNoToolExecutionRetries = maxNoToolExecutionRetries;
            MaxDuplicateToolCallSignatures = maxDuplicateToolCallSignatures;
        }

        public bool? AssertCleanCompletion { get; }
        public bool? AssertToolCallOutputPairing { get; }
        public bool? AssertNoDuplicateToolCallIds { get; }
        public bool? AssertNoDuplicateToolOutputCallIds { get; }
        public int? MaxNoToolExecutionRetries { get; }
        public int? MaxDuplicateToolCallSignatures { get; }
    }

    private sealed class ScenarioTurnRun {
        public ScenarioTurnRun(
            int index,
            string label,
            string user,
            DateTime startedAtUtc,
            DateTime completedAtUtc,
            ReplTurnMetricsResult? result,
            Exception? exception,
            IReadOnlyList<string> assertionFailures) {
            Index = index;
            Label = label ?? string.Empty;
            User = user ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            Result = result;
            Exception = exception;
            AssertionFailures = assertionFailures ?? Array.Empty<string>();
        }

        public int Index { get; }
        public string Label { get; }
        public string User { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime CompletedAtUtc { get; }
        public ReplTurnMetricsResult? Result { get; }
        public Exception? Exception { get; }
        public IReadOnlyList<string> AssertionFailures { get; }
        public bool Success => Exception is null && AssertionFailures.Count == 0;
    }

    private sealed class ScenarioRunReport {
        public ScenarioRunReport(
            string scenarioName,
            string scenarioSourcePath,
            DateTime startedAtUtc,
            DateTime completedAtUtc,
            bool continueOnError,
            IReadOnlyList<ScenarioTurnRun> turnRuns) {
            ScenarioName = string.IsNullOrWhiteSpace(scenarioName) ? "scenario" : scenarioName.Trim();
            ScenarioSourcePath = scenarioSourcePath ?? string.Empty;
            StartedAtUtc = startedAtUtc;
            CompletedAtUtc = completedAtUtc;
            ContinueOnError = continueOnError;
            TurnRuns = turnRuns ?? Array.Empty<ScenarioTurnRun>();
        }

        public string ScenarioName { get; }
        public string ScenarioSourcePath { get; }
        public DateTime StartedAtUtc { get; }
        public DateTime CompletedAtUtc { get; }
        public bool ContinueOnError { get; }
        public IReadOnlyList<ScenarioTurnRun> TurnRuns { get; }
    }
}
