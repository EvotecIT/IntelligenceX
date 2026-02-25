using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostScenarioCompactionSoakTests {
    [Fact]
    public void AdCompactionSoakScenarios_UseLongRunStrictContracts() {
        var scenarioDir = ResolveScenarioDirectory();
        var files = Directory.GetFiles(scenarioDir, "ad-compaction-soak-*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);

        foreach (var file in files) {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var fileName = Path.GetFileName(file);

            var tags = ReadTagSet(root);
            Assert.Contains("ad", tags);
            Assert.Contains("strict", tags);
            Assert.Contains("live", tags);
            Assert.Contains("compaction", tags);
            Assert.Contains("soak", tags);

            var defaults = RequireProperty(root, "defaults");
            Assert.Equal(JsonValueKind.Object, defaults.ValueKind);
            Assert.True(ReadRequiredBoolean(defaults, "assert_clean_completion"));
            Assert.True(ReadRequiredBoolean(defaults, "assert_tool_call_output_pairing"));
            Assert.True(ReadRequiredBoolean(defaults, "assert_no_duplicate_tool_call_ids"));
            Assert.True(ReadRequiredBoolean(defaults, "assert_no_duplicate_tool_output_call_ids"));
            Assert.Equal(0, ReadRequiredInt32(defaults, "max_no_tool_execution_retries"));
            Assert.Equal(1, ReadRequiredInt32(defaults, "max_duplicate_tool_call_signatures"));

            var turns = RequireProperty(root, "turns");
            Assert.Equal(JsonValueKind.Array, turns.ValueKind);
            var turnList = turns.EnumerateArray().ToArray();
            Assert.True(turnList.Length >= 20, $"{fileName}: expected at least 20 turns.");

            var toolContractTurnCount = 0;
            var crossDcTurnCount = 0;
            foreach (var turn in turnList) {
                var user = ReadOptionalString(turn, "user");
                Assert.False(string.IsNullOrWhiteSpace(user), $"{fileName}: every turn must include non-empty user text.");

                if (HasToolContract(turn)) {
                    toolContractTurnCount++;
                }

                var minToolCalls = ReadOptionalInt32(turn, "min_tool_calls");
                if (minToolCalls < 2) {
                    continue;
                }

                var minimumDistinctValues = ReadNonNegativeIntMap(turn, "min_distinct_tool_input_values");
                if (minimumDistinctValues.TryGetValue("machine_name", out var minMachineNameValues) && minMachineNameValues >= 2) {
                    crossDcTurnCount++;
                }
            }

            Assert.True(toolContractTurnCount >= 10, $"{fileName}: expected at least 10 tool-contract turns.");
            Assert.True(crossDcTurnCount >= 5, $"{fileName}: expected at least 5 cross-DC continuation turns with machine diversity.");

            var finalTurn = turnList[^1];
            Assert.True(ReadRequiredBoolean(finalTurn, "assert_no_questions"));

            var finalNotContains = ReadStringList(finalTurn, "assert_not_contains");
            Assert.Contains(finalNotContains, static value => value.IndexOf("Partial response shown above", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Contains(finalNotContains, static value => value.IndexOf("turn ended before completion", StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    private static string ResolveScenarioDirectory() {
        var current = Path.GetFullPath(AppContext.BaseDirectory);
        while (!string.IsNullOrWhiteSpace(current)) {
            var candidate = Path.Combine(current, "IntelligenceX.Chat", "scenarios");
            if (Directory.Exists(candidate)) {
                return candidate;
            }

            var parent = Directory.GetParent(current);
            if (parent is null) {
                break;
            }

            current = parent.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate 'IntelligenceX.Chat/scenarios' from test runtime path.");
    }

    private static JsonElement RequireProperty(JsonElement root, string name) {
        if (!root.TryGetProperty(name, out var value)) {
            throw new InvalidDataException($"Missing required property '{name}'.");
        }

        return value;
    }

    private static string ReadOptionalString(JsonElement root, string name) {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static int ReadOptionalInt32(JsonElement root, string name) {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed)) {
            return 0;
        }

        return parsed;
    }

    private static bool ReadRequiredBoolean(JsonElement root, string name) {
        var value = RequireProperty(root, name);
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) {
            return value.GetBoolean();
        }

        throw new InvalidDataException($"Property '{name}' must be a boolean.");
    }

    private static int ReadRequiredInt32(JsonElement root, string name) {
        var value = RequireProperty(root, name);
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)) {
            return parsed;
        }

        throw new InvalidDataException($"Property '{name}' must be an integer.");
    }

    private static IReadOnlyDictionary<string, int> ReadNonNegativeIntMap(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind == JsonValueKind.Null) {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        if (values.ValueKind != JsonValueKind.Object) {
            throw new InvalidDataException($"Property '{propertyName}' must be an object.");
        }

        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in values.EnumerateObject()) {
            var key = (property.Name ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            var value = property.Value;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed)) {
                throw new InvalidDataException($"Property '{propertyName}.{key}' must be an integer.");
            }

            if (parsed < 0) {
                throw new InvalidDataException($"Property '{propertyName}.{key}' must be >= 0.");
            }

            result[key] = parsed;
        }

        return result;
    }

    private static IReadOnlyList<string> ReadStringList(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var value in values.EnumerateArray()) {
            if (value.ValueKind != JsonValueKind.String) {
                continue;
            }

            var candidate = (value.GetString() ?? string.Empty).Trim();
            if (candidate.Length > 0) {
                result.Add(candidate);
            }
        }

        return result;
    }

    private static HashSet<string> ReadTagSet(JsonElement root) {
        if (!root.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in tags.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.String) {
                continue;
            }

            var value = (element.GetString() ?? string.Empty).Trim();
            if (value.Length > 0) {
                values.Add(value);
            }
        }

        return values;
    }

    private static bool HasToolContract(JsonElement turn) {
        if (ReadOptionalInt32(turn, "min_tool_calls") > 0 || ReadOptionalInt32(turn, "min_tool_rounds") > 0) {
            return true;
        }

        return ReadStringList(turn, "require_tools").Count > 0
               || ReadStringList(turn, "require_any_tools").Count > 0
               || ReadStringList(turn, "assert_tool_output_contains").Count > 0
               || ReadStringList(turn, "assert_tool_output_not_contains").Count > 0
               || ReadStringList(turn, "forbid_tool_error_codes").Count > 0
               || (turn.TryGetProperty("assert_no_tool_errors", out var noErrors)
                   && noErrors.ValueKind is JsonValueKind.True or JsonValueKind.False
                   && noErrors.GetBoolean());
    }
}
