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
                metricsResult = await session.AskWithMetricsAsync(turn.User, cancellationToken).ConfigureAwait(false);
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
        try {
            WriteScenarioReportMarkdown(
                reportPath,
                new ScenarioRunReport(
                    scenarioName: scenario.Name,
                    scenarioSourcePath: scenarioPath,
                    startedAtUtc: startedAtUtc,
                    completedAtUtc: completedAtUtc,
                    continueOnError: options.ScenarioContinueOnError,
                    turnRuns: turnRuns));
            Console.WriteLine($"Scenario report saved: {reportPath}");
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to save scenario report '{reportPath}': {ex.Message}");
            failed = true;
        }

        var passed = turnRuns.Count(static t => t.Success);
        var total = turnRuns.Count;
        Console.WriteLine($"Scenario summary: {passed}/{total} turns passed.");
        return failed ? 1 : 0;
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
            if (!root.TryGetProperty("turns", out var turnsElement) || turnsElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException("Scenario JSON object must include a 'turns' array.");
            }
            turns = ParseChatScenarioTurnsFromJsonArray(turnsElement);
        } else if (root.ValueKind == JsonValueKind.Array) {
            scenarioName = fallbackName;
            turns = ParseChatScenarioTurnsFromJsonArray(root);
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

    private static IReadOnlyList<ChatScenarioTurn> ParseChatScenarioTurnsFromJsonArray(JsonElement arrayElement) {
        var turns = new List<ChatScenarioTurn>();
        var turnIndex = 0;
        foreach (var element in arrayElement.EnumerateArray()) {
            turnIndex++;
            if (element.ValueKind == JsonValueKind.String) {
                var userText = (element.GetString() ?? string.Empty).Trim();
                if (userText.Length == 0) {
                    continue;
                }
                turns.Add(new ChatScenarioTurn(
                    name: null,
                    user: userText,
                    assertContains: Array.Empty<string>(),
                    assertNotContains: Array.Empty<string>(),
                    minToolCalls: null,
                    minToolRounds: null,
                    requireTools: Array.Empty<string>(),
                    requireAnyTools: Array.Empty<string>(),
                    forbidTools: Array.Empty<string>(),
                    assertToolOutputContains: Array.Empty<string>(),
                    assertToolOutputNotContains: Array.Empty<string>(),
                    assertNoToolErrors: false,
                    forbidToolErrorCodes: Array.Empty<string>()));
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
            var minToolCalls = ReadScenarioOptionalNonNegativeInt(element, "min_tool_calls");
            var minToolRounds = ReadScenarioOptionalNonNegativeInt(element, "min_tool_rounds");
            var requireTools = ReadScenarioStringList(element, "require_tools");
            var requireAnyTools = ReadScenarioStringList(element, "require_any_tools");
            var forbidTools = ReadScenarioStringList(element, "forbid_tools");
            var assertToolOutputContains = ReadScenarioStringList(element, "assert_tool_output_contains");
            var assertToolOutputNotContains = ReadScenarioStringList(element, "assert_tool_output_not_contains");
            var assertNoToolErrors = ReadScenarioOptionalBoolean(element, "assert_no_tool_errors", defaultValue: false);
            var forbidToolErrorCodes = ReadScenarioStringList(element, "forbid_tool_error_codes");
            turns.Add(new ChatScenarioTurn(
                name,
                user.Trim(),
                assertContains,
                assertNotContains,
                minToolCalls,
                minToolRounds,
                requireTools,
                requireAnyTools,
                forbidTools,
                assertToolOutputContains,
                assertToolOutputNotContains,
                assertNoToolErrors,
                forbidToolErrorCodes));
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
            turns.Add(new ChatScenarioTurn(
                name: $"Turn {turns.Count + 1}",
                user: candidate,
                assertContains: Array.Empty<string>(),
                assertNotContains: Array.Empty<string>(),
                minToolCalls: null,
                minToolRounds: null,
                requireTools: Array.Empty<string>(),
                requireAnyTools: Array.Empty<string>(),
                forbidTools: Array.Empty<string>(),
                assertToolOutputContains: Array.Empty<string>(),
                assertToolOutputNotContains: Array.Empty<string>(),
                assertNoToolErrors: false,
                forbidToolErrorCodes: Array.Empty<string>()));
        }
        return turns;
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

    private static List<string> EvaluateScenarioAssertions(ChatScenarioTurn turn, ReplTurnMetricsResult? turnResult) {
        var failures = new List<string>();
        var assistantText = turnResult?.Result.Text ?? string.Empty;
        var toolCalls = turnResult?.Result.ToolCalls ?? Array.Empty<ToolCall>();
        var toolOutputs = turnResult?.Result.ToolOutputs ?? Array.Empty<ToolOutput>();
        var toolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var toolName = (toolCalls[i].Name ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                toolNames.Add(toolName);
            }
        }
        var (toolErrorCount, toolErrorCodes) = SummarizeToolOutputErrors(toolOutputs);

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

        var toolCallsCount = turnResult?.Metrics.ToolCallsCount ?? 0;
        if (turn.MinToolCalls.HasValue && toolCallsCount < turn.MinToolCalls.Value) {
            failures.Add($"Expected at least {turn.MinToolCalls.Value} tool call(s); observed {toolCallsCount}.");
        }

        var toolRounds = turnResult?.Metrics.ToolRounds ?? 0;
        if (turn.MinToolRounds.HasValue && toolRounds < turn.MinToolRounds.Value) {
            failures.Add($"Expected at least {turn.MinToolRounds.Value} tool round(s); observed {toolRounds}.");
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

        return failures;
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
            int? minToolCalls,
            int? minToolRounds,
            IReadOnlyList<string> requireTools,
            IReadOnlyList<string> requireAnyTools,
            IReadOnlyList<string> forbidTools,
            IReadOnlyList<string> assertToolOutputContains,
            IReadOnlyList<string> assertToolOutputNotContains,
            bool assertNoToolErrors,
            IReadOnlyList<string> forbidToolErrorCodes) {
            Name = name;
            User = user ?? string.Empty;
            AssertContains = assertContains ?? Array.Empty<string>();
            AssertNotContains = assertNotContains ?? Array.Empty<string>();
            MinToolCalls = minToolCalls;
            MinToolRounds = minToolRounds;
            RequireTools = requireTools ?? Array.Empty<string>();
            RequireAnyTools = requireAnyTools ?? Array.Empty<string>();
            ForbidTools = forbidTools ?? Array.Empty<string>();
            AssertToolOutputContains = assertToolOutputContains ?? Array.Empty<string>();
            AssertToolOutputNotContains = assertToolOutputNotContains ?? Array.Empty<string>();
            AssertNoToolErrors = assertNoToolErrors;
            ForbidToolErrorCodes = forbidToolErrorCodes ?? Array.Empty<string>();
        }

        public string? Name { get; }
        public string User { get; }
        public IReadOnlyList<string> AssertContains { get; }
        public IReadOnlyList<string> AssertNotContains { get; }
        public int? MinToolCalls { get; }
        public int? MinToolRounds { get; }
        public IReadOnlyList<string> RequireTools { get; }
        public IReadOnlyList<string> RequireAnyTools { get; }
        public IReadOnlyList<string> ForbidTools { get; }
        public IReadOnlyList<string> AssertToolOutputContains { get; }
        public IReadOnlyList<string> AssertToolOutputNotContains { get; }
        public bool AssertNoToolErrors { get; }
        public IReadOnlyList<string> ForbidToolErrorCodes { get; }
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
