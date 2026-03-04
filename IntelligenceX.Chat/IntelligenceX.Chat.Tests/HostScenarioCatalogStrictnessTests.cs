using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostScenarioCatalogStrictnessTests {
    [Fact]
    public void TenTurnScenarios_AreStrictByDefault() {
        var scenarioDir = ResolveScenarioDirectory();
        var scenarioFiles = Directory.GetFiles(scenarioDir, "*-10-turn.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(scenarioFiles);

        foreach (var file in scenarioFiles) {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var fileName = Path.GetFileName(file);

            var turns = RequireProperty(root, "turns");
            Assert.Equal(JsonValueKind.Array, turns.ValueKind);
            Assert.Equal(10, turns.GetArrayLength());

            var tags = ReadTagSet(root);
            Assert.Contains("strict", tags);
            Assert.Contains("live", tags);
            if (fileName.StartsWith("ad-", StringComparison.OrdinalIgnoreCase)) {
                Assert.Contains("ad", tags);
            }
            if (fileName.StartsWith("dns-", StringComparison.OrdinalIgnoreCase)) {
                Assert.Contains("dns", tags);
            }

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

    [Fact]
    public void DnsTenTurnScenarios_StayOnOpenSourceDnsToolingPath() {
        var scenarioDir = ResolveScenarioDirectory();
        var scenarioFiles = Directory.GetFiles(scenarioDir, "dns-*-10-turn.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(scenarioFiles);

        foreach (var file in scenarioFiles) {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;

            var tags = ReadTagSet(root);
            Assert.Contains("dns", tags);
            Assert.Contains("strict", tags);
            Assert.Contains("live", tags);

            var turns = RequireProperty(root, "turns");
            Assert.Equal(JsonValueKind.Array, turns.ValueKind);
            Assert.Equal(10, turns.GetArrayLength());

            var requiredPatterns = new List<string>();
            var forbiddenPatterns = new List<string>();
            foreach (var turn in turns.EnumerateArray()) {
                requiredPatterns.AddRange(ReadStringList(turn, "require_tools"));
                requiredPatterns.AddRange(ReadStringList(turn, "require_any_tools"));
                forbiddenPatterns.AddRange(ReadStringList(turn, "forbid_tools"));
            }

            Assert.Contains(requiredPatterns, static pattern => pattern.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(requiredPatterns, static pattern => pattern.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(forbiddenPatterns, static pattern => pattern.StartsWith("ad_", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(forbiddenPatterns, static pattern => pattern.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AdContinuationTurns_EnforceDistinctMachineCoverage() {
        var scenarioDir = ResolveScenarioDirectory();
        var scenarioFiles = Directory.GetFiles(scenarioDir, "ad-*-10-turn.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(scenarioFiles);

        var matchedContinuationTurns = 0;
        foreach (var file in scenarioFiles) {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var turns = RequireProperty(root, "turns");
            Assert.Equal(JsonValueKind.Array, turns.ValueKind);

            foreach (var turn in turns.EnumerateArray()) {
                if (turn.ValueKind != JsonValueKind.Object || !IsCrossDcContinuationTurn(turn)) {
                    continue;
                }

                matchedContinuationTurns++;
                Assert.True(ReadRequiredInt32(turn, "min_tool_calls") >= 2);

                var minimumDistinctInputValues = ReadNonNegativeIntMap(turn, "min_distinct_tool_input_values");
                Assert.True(minimumDistinctInputValues.TryGetValue("machine_name", out var minMachineNameValues));
                Assert.True(minMachineNameValues >= 2);
            }
        }

        Assert.True(matchedContinuationTurns >= 5);
    }

    [Fact]
    public void AdRebootLocalPeerCrossCheckTurn_EnforcesDistinctMachineCoverage() {
        var scenarioDir = ResolveScenarioDirectory();
        var file = Path.Combine(scenarioDir, "ad-reboot-local-10-turn.json");

        Assert.True(File.Exists(file), $"Expected scenario file '{file}' to exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;
        var turns = RequireProperty(root, "turns");
        Assert.Equal(JsonValueKind.Array, turns.ValueKind);

        var crossCheckTurn = turns.EnumerateArray().FirstOrDefault(static turn =>
            turn.ValueKind == JsonValueKind.Object
            && turn.TryGetProperty("name", out var nameElement)
            && nameElement.ValueKind == JsonValueKind.String
            && string.Equals(nameElement.GetString(), "Cross-check peer DCs", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(JsonValueKind.Object, crossCheckTurn.ValueKind);
        Assert.True(ReadRequiredInt32(crossCheckTurn, "min_tool_calls") >= 2);

        var minimumDistinctInputValues = ReadNonNegativeIntMap(crossCheckTurn, "min_distinct_tool_input_values");
        Assert.True(minimumDistinctInputValues.TryGetValue("machine_name", out var minMachineNameValues));
        Assert.True(minMachineNameValues >= 2);
    }

    [Fact]
    public void AdOtherDcsGoAheadScenario_ContinuationTurnsForbidAd0HostInputs() {
        var scenarioDir = ResolveScenarioDirectory();
        var file = Path.Combine(scenarioDir, "ad-other-dcs-go-ahead-followthrough-10-turn.json");

        Assert.True(File.Exists(file), $"Expected scenario file '{file}' to exist.");

        using var document = JsonDocument.Parse(File.ReadAllText(file));
        var root = document.RootElement;
        var turns = RequireProperty(root, "turns");
        Assert.Equal(JsonValueKind.Array, turns.ValueKind);

        var matched = 0;
        foreach (var turn in turns.EnumerateArray()) {
            if (turn.ValueKind != JsonValueKind.Object
                || !turn.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String) {
                continue;
            }

            var name = nameElement.GetString() ?? string.Empty;
            if (!name.Contains("go-ahead", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            matched++;
            var forbiddenInputValues = ReadStringListMap(turn, "forbid_tool_input_values");
            Assert.True(forbiddenInputValues.TryGetValue("machine_name", out var machineNameForbidden));
            Assert.Contains(machineNameForbidden, value => string.Equals(value, "AD0", StringComparison.OrdinalIgnoreCase));
        }

        Assert.True(matched >= 2, "Expected at least two go-ahead continuation turns.");
    }

    [Fact]
    public void MixedDomainAmbiguityScenarios_RequireClarifyBeforeSplitToolPaths() {
        var scenarioDir = ResolveScenarioDirectory();
        var files = Directory.GetFiles(scenarioDir, "mixed-domain-ambiguity-*-10-turn.json", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(files);
        Assert.True(files.Length >= 2, "Expected at least two mixed-domain ambiguity scenarios.");

        foreach (var file in files) {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var root = document.RootElement;
            var fileName = Path.GetFileName(file);

            var tags = ReadTagSet(root);
            Assert.Contains("strict", tags);
            Assert.Contains("live", tags);
            Assert.Contains("domain-ambiguity", tags);

            var turns = RequireProperty(root, "turns");
            Assert.Equal(JsonValueKind.Array, turns.ValueKind);
            Assert.Equal(10, turns.GetArrayLength());

            var turnList = turns.EnumerateArray().ToArray();
            Assert.NotEmpty(turnList);

            var clarifyTurn = turnList[0];
            var clarifyForbidden = ReadStringList(clarifyTurn, "forbid_tools");
            Assert.Contains(clarifyForbidden, pattern => string.Equals(pattern, "*", StringComparison.Ordinal));

            var clarifyContains = ReadStringList(clarifyTurn, "assert_contains")
                .Concat(ReadStringList(clarifyTurn, "assert_contains_any"))
                .Concat(ReadStringList(clarifyTurn, "assert_matches_regex"))
                .ToArray();
            Assert.Contains(clarifyContains, pattern => pattern.IndexOf("ad", StringComparison.OrdinalIgnoreCase) >= 0 || pattern.IndexOf("directory", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Contains(clarifyContains, pattern => pattern.IndexOf("dns", StringComparison.OrdinalIgnoreCase) >= 0);

            var adPathTurnFound = false;
            var dnsPathTurnFound = false;
            foreach (var turn in turnList.Skip(1)) {
                var requiredPatterns = new List<string>();
                requiredPatterns.AddRange(ReadStringList(turn, "require_tools"));
                requiredPatterns.AddRange(ReadStringList(turn, "require_any_tools"));
                var forbiddenPatterns = ReadStringList(turn, "forbid_tools");
                if (requiredPatterns.Count == 0) {
                    continue;
                }

                var requiresAd = requiredPatterns.Any(static pattern => pattern.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
                                                                        || pattern.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase));
                var requiresDns = requiredPatterns.Any(static pattern => pattern.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase)
                                                                         || pattern.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase));
                var forbidsAd = forbiddenPatterns.Any(static pattern => pattern.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
                                                                        || pattern.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase));
                var forbidsDns = forbiddenPatterns.Any(static pattern => pattern.StartsWith("dnsclientx_", StringComparison.OrdinalIgnoreCase)
                                                                         || pattern.StartsWith("domaindetective_", StringComparison.OrdinalIgnoreCase));

                if (requiresAd && forbidsDns) {
                    adPathTurnFound = true;
                }

                if (requiresDns && forbidsAd) {
                    dnsPathTurnFound = true;
                }
            }

            Assert.True(adPathTurnFound, $"{fileName}: expected at least one AD-routed turn that forbids DNS tools.");
            Assert.True(dnsPathTurnFound, $"{fileName}: expected at least one DNS-routed turn that forbids AD/EventLog tools.");
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

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadStringListMap(JsonElement root, string propertyName) {
        if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind == JsonValueKind.Null) {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        if (values.ValueKind != JsonValueKind.Object) {
            throw new InvalidDataException($"Property '{propertyName}' must be an object.");
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in values.EnumerateObject()) {
            var key = (property.Name ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            var parsedValues = new List<string>();
            if (property.Value.ValueKind == JsonValueKind.String) {
                var single = (property.Value.GetString() ?? string.Empty).Trim();
                if (single.Length > 0) {
                    parsedValues.Add(single);
                }
            } else if (property.Value.ValueKind == JsonValueKind.Array) {
                foreach (var item in property.Value.EnumerateArray()) {
                    if (item.ValueKind != JsonValueKind.String) {
                        throw new InvalidDataException($"Property '{propertyName}.{key}' array must contain only strings.");
                    }

                    var candidate = (item.GetString() ?? string.Empty).Trim();
                    if (candidate.Length > 0) {
                        parsedValues.Add(candidate);
                    }
                }
            } else {
                throw new InvalidDataException($"Property '{propertyName}.{key}' must be a string or array of strings.");
            }

            result[key] = parsedValues
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return result;
    }

    private static bool IsCrossDcContinuationTurn(JsonElement turn) {
        if (!turn.TryGetProperty("min_tool_calls", out var minToolCallsElement)
            || minToolCallsElement.ValueKind != JsonValueKind.Number
            || !minToolCallsElement.TryGetInt32(out var minToolCalls)
            || minToolCalls < 2) {
            return false;
        }

        // Language-neutral gate: detect cross-DC continuation turns via structured tool-contract
        // fields instead of natural-language phrasing in `name`/`user`.
        var minimumDistinctInputValues = ReadNonNegativeIntMap(turn, "min_distinct_tool_input_values");
        if (!minimumDistinctInputValues.TryGetValue("machine_name", out var minMachineNameValues)
            || minMachineNameValues < 2) {
            return false;
        }

        var requiredPatterns = ReadStringList(turn, "require_tools")
            .Concat(ReadStringList(turn, "require_any_tools"))
            .ToArray();
        if (requiredPatterns.Length == 0) {
            return false;
        }

        return requiredPatterns.Any(static pattern =>
            pattern.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
            || pattern.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase));
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
