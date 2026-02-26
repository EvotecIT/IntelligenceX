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
                    assertContainsAny: Array.Empty<string>(),
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
            var assertContainsAny = ReadScenarioStringList(element, "assert_contains_any");
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
                assertContainsAny,
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
                assertContainsAny: Array.Empty<string>(),
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
            assertContainsAny,
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

}
