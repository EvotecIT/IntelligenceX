using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostScenarioCatalogStrictnessTests {
    [Fact]
    public void AdTenTurnScenarios_AreStrictByDefault() {
        var scenarioDir = ResolveScenarioDirectory();
        var scenarioFiles = Directory.GetFiles(scenarioDir, "ad-*-10-turn.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(scenarioFiles);

        foreach (var file in scenarioFiles) {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            var turns = RequireProperty(root, "turns");
            Assert.Equal(JsonValueKind.Array, turns.ValueKind);
            Assert.Equal(10, turns.GetArrayLength());

            var tags = ReadTagSet(root);
            Assert.Contains("ad", tags);
            Assert.Contains("strict", tags);
            Assert.Contains("live", tags);

            var defaults = RequireProperty(root, "defaults");
            Assert.Equal(JsonValueKind.Object, defaults.ValueKind);
            Assert.True(ReadRequiredBoolean(defaults, "assert_clean_completion"));
            Assert.True(ReadRequiredBoolean(defaults, "assert_tool_call_output_pairing"));
            Assert.True(ReadRequiredBoolean(defaults, "assert_no_duplicate_tool_call_ids"));
            Assert.True(ReadRequiredBoolean(defaults, "assert_no_duplicate_tool_output_call_ids"));
            Assert.Equal(0, ReadRequiredInt32(defaults, "max_no_tool_execution_retries"));
            Assert.Equal(1, ReadRequiredInt32(defaults, "max_duplicate_tool_call_signatures"));
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

    private static JsonElement RequireProperty(JsonElement root, string name) {
        if (!root.TryGetProperty(name, out var value)) {
            throw new InvalidDataException($"Missing required property '{name}'.");
        }

        return value;
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
}
