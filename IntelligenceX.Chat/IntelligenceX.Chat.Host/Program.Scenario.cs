using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

            var assistantText = metricsResult?.Result.Text ?? string.Empty;
            var assertionFailures = EvaluateScenarioAssertions(turn, assistantText);
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
                turns.Add(new ChatScenarioTurn(name: null, user: userText, assertContains: Array.Empty<string>()));
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
            var assertions = ReadScenarioAssertContains(element);
            turns.Add(new ChatScenarioTurn(name, user.Trim(), assertions));
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
            turns.Add(new ChatScenarioTurn(name: $"Turn {turns.Count + 1}", user: candidate, assertContains: Array.Empty<string>()));
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
        if (!element.TryGetProperty("assert_contains", out var assertElement)) {
            return Array.Empty<string>();
        }

        if (assertElement.ValueKind == JsonValueKind.String) {
            var single = (assertElement.GetString() ?? string.Empty).Trim();
            return single.Length == 0 ? Array.Empty<string>() : new[] { single };
        }

        if (assertElement.ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("'assert_contains' must be a string or array of strings.");
        }

        var assertions = new List<string>();
        foreach (var item in assertElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.String) {
                throw new InvalidOperationException("'assert_contains' array must contain only strings.");
            }

            var value = (item.GetString() ?? string.Empty).Trim();
            if (value.Length > 0) {
                assertions.Add(value);
            }
        }
        return assertions;
    }

    private static List<string> EvaluateScenarioAssertions(ChatScenarioTurn turn, string assistantText) {
        var failures = new List<string>();
        if (turn.AssertContains.Count == 0) {
            return failures;
        }

        var haystack = assistantText ?? string.Empty;
        foreach (var expected in turn.AssertContains) {
            if (haystack.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0) {
                continue;
            }
            failures.Add($"Expected assistant output to contain '{expected}'.");
        }

        return failures;
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
        public ChatScenarioTurn(string? name, string user, IReadOnlyList<string> assertContains) {
            Name = name;
            User = user ?? string.Empty;
            AssertContains = assertContains ?? Array.Empty<string>();
        }

        public string? Name { get; }
        public string User { get; }
        public IReadOnlyList<string> AssertContains { get; }
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
