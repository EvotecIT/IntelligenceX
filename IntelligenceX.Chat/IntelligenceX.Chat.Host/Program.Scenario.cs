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
    private const int NoToolExecutionRetryToleranceOnSuccessfulToolTurn = 3;
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
            IReadOnlyList<string> assertContainsAny,
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
            AssertContainsAny = assertContainsAny ?? Array.Empty<string>();
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
        public IReadOnlyList<string> AssertContainsAny { get; }
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
