using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Verifies host scenario parsing for non-interactive multi-turn replay mode.
/// </summary>
public sealed class HostScenarioParsingTests {
    [Fact]
    public void ParseChatScenarioDefinition_FromJsonObject_ParsesNameTurnsAndAssertions() {
        const string json = """
{
  "name": "ad-reboot-check",
  "turns": [
    {
      "name": "Turn One",
      "user": "Check AD0 reboot",
      "assert_contains": ["6008", "unexpected reboot"],
      "assert_contains_any": ["Active Directory", "AD DS"],
      "assert_not_contains": ["I can do that, but"],
      "assert_matches_regex": ["\\d{4}-\\d{2}-\\d{2}T\\d{2}:\\d{2}"],
      "assert_no_questions": true,
      "min_tool_calls": 1,
      "min_tool_rounds": 1,
      "require_tools": ["eventlog_live_query"],
      "require_any_tools": ["eventlog_live_query", "eventlog_live_stats"],
      "forbid_tools": ["eventlog_evtx_query"],
      "min_distinct_tool_input_values": { "machine_name": 2 },
      "forbid_tool_input_values": { "machine_name": ["AD0", "localhost"] },
      "assert_tool_output_contains": ["6008", "Kernel-Power"],
      "assert_tool_output_not_contains": ["schema_validation_failed"],
      "assert_no_tool_errors": true,
      "forbid_tool_error_codes": ["invalid_argument", "query_*"],
      "assert_clean_completion": true,
      "assert_tool_call_output_pairing": true,
      "assert_no_duplicate_tool_call_ids": true,
      "assert_no_duplicate_tool_output_call_ids": true,
      "max_no_tool_execution_retries": 0,
      "max_duplicate_tool_call_signatures": 1
    },
    "Check peer DCs"
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "fallback-name");

        Assert.Equal("ad-reboot-check", ReadStringProperty(scenario, "Name"));
        var turns = ReadTurns(scenario);
        Assert.Equal(2, turns.Count);
        Assert.Equal("Turn One", ReadStringProperty(turns[0], "Name"));
        Assert.Equal("Check AD0 reboot", ReadStringProperty(turns[0], "User"));
        Assert.Equal(2, ReadStringListProperty(turns[0], "AssertContains").Count);
        Assert.Equal(2, ReadStringListProperty(turns[0], "AssertContainsAny").Count);
        Assert.Single(ReadStringListProperty(turns[0], "AssertNotContains"));
        Assert.Single(ReadStringListProperty(turns[0], "AssertMatchesRegex"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoQuestions"));
        Assert.Equal(1, ReadNullableIntProperty(turns[0], "MinToolCalls"));
        Assert.Equal(1, ReadNullableIntProperty(turns[0], "MinToolRounds"));
        Assert.Single(ReadStringListProperty(turns[0], "RequireTools"));
        Assert.Equal(2, ReadStringListProperty(turns[0], "RequireAnyTools").Count);
        Assert.Single(ReadStringListProperty(turns[0], "ForbidTools"));
        var minDistinctInputValues = ReadIntDictionaryProperty(turns[0], "MinDistinctToolInputValues");
        Assert.True(minDistinctInputValues.TryGetValue("machine_name", out var minMachineNameValues));
        Assert.Equal(2, minMachineNameValues);
        var forbiddenInputValues = ReadStringListDictionaryProperty(turns[0], "ForbidToolInputValues");
        Assert.True(forbiddenInputValues.TryGetValue("machine_name", out var forbiddenMachineValues));
        Assert.Equal(new[] { "AD0", "localhost" }, forbiddenMachineValues);
        Assert.Equal(2, ReadStringListProperty(turns[0], "AssertToolOutputContains").Count);
        Assert.Single(ReadStringListProperty(turns[0], "AssertToolOutputNotContains"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoToolErrors"));
        Assert.Equal(2, ReadStringListProperty(turns[0], "ForbidToolErrorCodes").Count);
        Assert.True(ReadBooleanProperty(turns[0], "AssertCleanCompletion"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertToolCallOutputPairing"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoDuplicateToolCallIds"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoDuplicateToolOutputCallIds"));
        Assert.Equal(0, ReadNullableIntProperty(turns[0], "MaxNoToolExecutionRetries"));
        Assert.Equal(1, ReadNullableIntProperty(turns[0], "MaxDuplicateToolCallSignatures"));
        Assert.Equal("Check peer DCs", ReadStringProperty(turns[1], "User"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertContains"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertContainsAny"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertNotContains"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertMatchesRegex"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertNoQuestions"));
        Assert.Null(ReadNullableIntProperty(turns[1], "MinToolCalls"));
        Assert.Null(ReadNullableIntProperty(turns[1], "MinToolRounds"));
        Assert.Empty(ReadStringListProperty(turns[1], "RequireTools"));
        Assert.Empty(ReadStringListProperty(turns[1], "RequireAnyTools"));
        Assert.Empty(ReadStringListProperty(turns[1], "ForbidTools"));
        Assert.Empty(ReadIntDictionaryProperty(turns[1], "MinDistinctToolInputValues"));
        Assert.Empty(ReadStringListDictionaryProperty(turns[1], "ForbidToolInputValues"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertToolOutputContains"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertToolOutputNotContains"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertNoToolErrors"));
        Assert.Empty(ReadStringListProperty(turns[1], "ForbidToolErrorCodes"));
        Assert.True(ReadBooleanProperty(turns[1], "AssertCleanCompletion"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertToolCallOutputPairing"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertNoDuplicateToolCallIds"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertNoDuplicateToolOutputCallIds"));
        Assert.Null(ReadNullableIntProperty(turns[1], "MaxNoToolExecutionRetries"));
        Assert.Null(ReadNullableIntProperty(turns[1], "MaxDuplicateToolCallSignatures"));
    }

    [Fact]
    public void ParseChatScenarioDefinition_FromPlainText_IgnoresCommentLines() {
        const string text = """
# comment
// another comment
Check AD0 reboot

- Check AD1 reboot
""";
        var scenario = InvokeParseScenarioDefinition(text, "plain-scenario");
        var turns = ReadTurns(scenario);

        Assert.Equal("plain-scenario", ReadStringProperty(scenario, "Name"));
        Assert.Equal(2, turns.Count);
        Assert.Equal("Check AD0 reboot", ReadStringProperty(turns[0], "User"));
        Assert.Equal("Check AD1 reboot", ReadStringProperty(turns[1], "User"));
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithoutToolRequirements_ReturnsRawUserText() {
        const string json = """
{
  "name": "simple",
  "turns": [
    {
      "user": "Check AD status"
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "simple");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Equal("Check AD status", prompt);
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithToolRequirements_EmbedsExecutionContract() {
        const string json = """
{
  "name": "required-tools",
  "turns": [
    {
      "user": "Compare lastLogon across DCs.",
      "min_tool_calls": 1,
      "require_any_tools": ["ad_ldap_query*", "ad_*discover*"]
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "required-tools");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Contains("[Scenario execution contract]", prompt, StringComparison.Ordinal);
        Assert.Contains("ix:scenario-execution:v1", prompt, StringComparison.Ordinal);
        Assert.Contains("requires_tool_execution: true", prompt, StringComparison.Ordinal);
        Assert.Contains("requires_no_tool_execution: false", prompt, StringComparison.Ordinal);
        Assert.Contains("min_tool_calls: 1", prompt, StringComparison.Ordinal);
        Assert.Contains("required_tools_any: ad_ldap_query*, ad_*discover*", prompt, StringComparison.Ordinal);
        Assert.Contains("Minimum tool calls in this turn: 1.", prompt, StringComparison.Ordinal);
        Assert.Contains("ad_ldap_query*", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not ask for permission/confirmation before the first required tool call.", prompt, StringComparison.Ordinal);
        Assert.Contains("Hard requirement: execute at least one qualifying tool call before any narrative prose in this turn.", prompt, StringComparison.Ordinal);
        Assert.Contains("Make at least one best-effort qualifying tool call in this turn, then summarize results.", prompt, StringComparison.Ordinal);
        Assert.Contains("User request:", prompt, StringComparison.Ordinal);
        Assert.Contains("Compare lastLogon across DCs.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithForbiddenToolInputValues_EmbedsForbiddenInputDirective() {
        const string json = """
{
  "name": "forbidden-inputs",
  "turns": [
    {
      "user": "Run eventlog checks only on non-AD0 hosts.",
      "min_tool_calls": 2,
      "require_any_tools": ["eventlog_*query*", "eventlog_*stats*"],
      "forbid_tool_input_values": { "machine_name": ["AD0", "localhost"] }
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "forbidden-inputs");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Contains("forbidden_tool_inputs: machine_name not-in [AD0|localhost]", prompt, StringComparison.Ordinal);
        Assert.Contains("Forbidden tool input values: machine_name not-in [AD0|localhost].", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScenarioTurnPromptForExecution_WithEventLogRequirements_AddsContractDerivedHints() {
        const string json = """
{
  "name": "eventlog-contract",
  "turns": [
    {
      "user": "Correlate recent security evidence.",
      "min_tool_calls": 1,
      "require_any_tools": ["eventlog_*query*"],
      "assert_contains": ["UTC"]
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "eventlog-contract");
        var turn = ReadTurns(scenario).Single();
        var toolDefinitions = new[] {
            new ToolDefinition(
                name: "eventlog_named_events_query",
                description: "Query named event detections.",
                parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties(),
                routing: new ToolRoutingContract {
                    IsRoutingAware = true,
                    RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
                    PackId = "eventlog",
                    Role = ToolRoutingTaxonomy.RoleOperational
                },
                setup: new ToolSetupContract {
                    IsSetupAware = true,
                    SetupToolName = "eventlog_named_events_catalog"
                },
                handoff: new ToolHandoffContract {
                    IsHandoffAware = true,
                    OutboundRoutes = new[] {
                        new ToolHandoffRoute {
                            TargetPackId = "system",
                            TargetRole = ToolRoutingTaxonomy.RoleDiagnostic,
                            Bindings = new[] {
                                new ToolHandoffBinding {
                                    SourceField = "machine_name",
                                    TargetArgument = "computer_name"
                                }
                            }
                        }
                    }
                })
        };

        var prompt = InvokeBuildScenarioTurnPromptForExecution(turn, toolDefinitions);

        Assert.Contains("Representative live tool examples for this flow", prompt, StringComparison.Ordinal);
        Assert.Contains("inspect Windows event logs", prompt, StringComparison.Ordinal);
        Assert.Contains("eventlog_named_events_catalog -> eventlog_named_events_query", prompt, StringComparison.Ordinal);
        Assert.Contains("Cross-pack follow-up pivots are available into System", prompt, StringComparison.Ordinal);
        Assert.Contains("If a remote-capable tool is missing host or machine input", prompt, StringComparison.Ordinal);
        Assert.Contains("Final response must include these literals: UTC.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithAssertContainsAny_AddsAnyLiteralHints() {
        const string json = """
{
  "name": "any-literal-contract",
  "turns": [
    {
      "user": "Summarize AD findings.",
      "min_tool_calls": 1,
      "require_any_tools": ["ad_*"],
      "assert_contains_any": ["Active Directory", "AD DS", "Directorio Activo"]
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "any-literal-contract");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Contains(
            "Final response must include at least one of these literals: Active Directory, AD DS, Directorio Activo.",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithAssertNotContains_AddsDoNotRepeatLiteralHint() {
        const string json = """
{
  "name": "no-forbidden-literal-contract",
  "turns": [
    {
      "user": "Continue all remaining DCs and avoid partial output.",
      "min_tool_calls": 1,
      "require_any_tools": ["ad_*", "eventlog_*"],
      "assert_not_contains": ["partial", "stopped after AD0"]
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "no-forbidden-literal-contract");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Contains(
            "Final response must NOT include these literals (do not repeat them, even to negate them): partial, stopped after AD0.",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithNoToolContract_EmbedsStructuredNoToolDirective() {
        const string json = """
{
  "name": "no-tool-contract",
  "turns": [
    {
      "user": "Acknowledge selected scope.",
      "forbid_tools": ["*"]
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "no-tool-contract");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Contains("ix:scenario-execution:v1", prompt, StringComparison.Ordinal);
        Assert.Contains("requires_tool_execution: false", prompt, StringComparison.Ordinal);
        Assert.Contains("requires_no_tool_execution: true", prompt, StringComparison.Ordinal);
        Assert.Contains("forbidden_tools: *", prompt, StringComparison.Ordinal);
        Assert.Contains("This scenario turn requires a response without tool execution.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildScenarioTurnPrompt_WithAssertNoQuestions_AddsQuestionPunctuationBan() {
        const string json = """
{
  "name": "no-question-mark-contract",
  "turns": [
    {
      "user": "Summarize DNS and AD findings.",
      "min_tool_calls": 1,
      "require_any_tools": ["dnsclientx_query"],
      "assert_no_questions": true
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "no-question-mark-contract");
        var turn = ReadTurns(scenario).Single();

        var prompt = InvokeBuildScenarioTurnPrompt(turn);

        Assert.Contains(
            "Do not include question-mark punctuation (`?`, `？`, `¿`, `؟`) anywhere in the final response.",
            prompt,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ParseChatScenarioDefinition_ToolContractTurn_AppliesStrictDefaultsWhenFieldsAreOmitted() {
        const string json = """
{
  "name": "strict-defaults",
  "turns": [
    {
      "name": "Strict Turn",
      "user": "Check AD0 reboot evidence",
      "min_tool_calls": 1,
      "require_any_tools": ["eventlog_*query*"]
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "strict-defaults");
        var turn = ReadTurns(scenario).Single();

        Assert.True(ReadBooleanProperty(turn, "AssertCleanCompletion"));
        Assert.True(ReadBooleanProperty(turn, "AssertToolCallOutputPairing"));
        Assert.True(ReadBooleanProperty(turn, "AssertNoDuplicateToolCallIds"));
        Assert.True(ReadBooleanProperty(turn, "AssertNoDuplicateToolOutputCallIds"));
        Assert.Equal(0, ReadNullableIntProperty(turn, "MaxNoToolExecutionRetries"));
        Assert.Equal(1, ReadNullableIntProperty(turn, "MaxDuplicateToolCallSignatures"));
    }

    [Fact]
    public void ParseChatScenarioDefinition_WithScenarioDefaults_AppliesDefaultsAndTurnOverride() {
        const string json = """
{
  "name": "scenario-defaults",
  "defaults": {
    "assert_clean_completion": true,
    "assert_tool_call_output_pairing": true,
    "assert_no_duplicate_tool_call_ids": true,
    "assert_no_duplicate_tool_output_call_ids": true,
    "max_no_tool_execution_retries": 0,
    "max_duplicate_tool_call_signatures": 1
  },
  "turns": [
    "Check AD scope",
    {
      "name": "Override pairing",
      "user": "Summarize current status.",
      "assert_tool_call_output_pairing": false
    }
  ]
}
""";

        var scenario = InvokeParseScenarioDefinition(json, "scenario-defaults");
        var turns = ReadTurns(scenario);
        Assert.Equal(2, turns.Count);

        Assert.True(ReadBooleanProperty(turns[0], "AssertCleanCompletion"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertToolCallOutputPairing"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoDuplicateToolCallIds"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoDuplicateToolOutputCallIds"));
        Assert.Equal(0, ReadNullableIntProperty(turns[0], "MaxNoToolExecutionRetries"));
        Assert.Equal(1, ReadNullableIntProperty(turns[0], "MaxDuplicateToolCallSignatures"));

        Assert.True(ReadBooleanProperty(turns[1], "AssertCleanCompletion"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertToolCallOutputPairing"));
        Assert.True(ReadBooleanProperty(turns[1], "AssertNoDuplicateToolCallIds"));
        Assert.True(ReadBooleanProperty(turns[1], "AssertNoDuplicateToolOutputCallIds"));
        Assert.Equal(0, ReadNullableIntProperty(turns[1], "MaxNoToolExecutionRetries"));
        Assert.Equal(1, ReadNullableIntProperty(turns[1], "MaxDuplicateToolCallSignatures"));
    }

    [Fact]
    public void ParseChatScenarioDefinition_WithScenarioRollupP95Thresholds_ParsesAndNormalizesPhases() {
        const string json = """
{
  "name": "scenario-rollup-thresholds",
  "max_phase_p95_duration_ms": {
    "model-plan": 700,
    "tool execute": 350
  },
  "turns": [
    {
      "user": "Run scenario turn."
    }
  ]
}
""";
        var scenario = InvokeParseScenarioDefinition(json, "scenario-rollup-thresholds");

        var rollupThresholds = ReadIntDictionaryProperty(scenario, "MaxPhaseP95DurationMs");
        Assert.Equal(2, rollupThresholds.Count);
        Assert.True(rollupThresholds.TryGetValue("model_plan", out var modelPlanThreshold));
        Assert.Equal(700, modelPlanThreshold);
        Assert.True(rollupThresholds.TryGetValue("tool_execute", out var toolExecuteThreshold));
        Assert.Equal(350, toolExecuteThreshold);
    }

    private static object InvokeParseScenarioDefinition(string raw, string fallbackName) {
        var programType = ResolveHostProgramType();
        var parseMethod = programType.GetMethod("ParseChatScenarioDefinition", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(null, new object?[] { raw, fallbackName });
        Assert.NotNull(result);
        return result!;
    }

    private static string InvokeBuildScenarioTurnPrompt(object turn) {
        var programType = ResolveHostProgramType();
        var turnType = programType.Assembly.GetType("IntelligenceX.Chat.Host.Program+ChatScenarioTurn", throwOnError: true);
        Assert.NotNull(turnType);
        Assert.True(turnType!.IsInstanceOfType(turn));

        var promptMethod = programType.GetMethod("BuildScenarioTurnPrompt", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(promptMethod);
        var result = promptMethod!.Invoke(null, new[] { turn });
        return Assert.IsType<string>(result);
    }

    private static string InvokeBuildScenarioTurnPromptForExecution(object turn, IReadOnlyList<ToolDefinition> toolDefinitions) {
        var programType = ResolveHostProgramType();
        var turnType = programType.Assembly.GetType("IntelligenceX.Chat.Host.Program+ChatScenarioTurn", throwOnError: true);
        Assert.NotNull(turnType);
        Assert.True(turnType!.IsInstanceOfType(turn));

        var promptMethod = programType.GetMethod("BuildScenarioTurnPromptForExecution", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(promptMethod);
        var result = promptMethod!.Invoke(null, new object?[] { turn, toolDefinitions });
        return Assert.IsType<string>(result);
    }

    private static Type ResolveHostProgramType() {
        var hostAssembly = Assembly.Load("IntelligenceX.Chat.Host");
        var hostProgramType = hostAssembly.GetType("IntelligenceX.Chat.Host.Program", throwOnError: true);
        Assert.NotNull(hostProgramType);
        return hostProgramType!;
    }

    private static string? ReadStringProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return property!.GetValue(instance) as string;
    }

    private static IReadOnlyList<object> ReadTurns(object scenarioDefinition) {
        var turnsProperty = scenarioDefinition.GetType().GetProperty("Turns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(turnsProperty);
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(turnsProperty!.GetValue(scenarioDefinition));
        return enumerable.Cast<object>().ToList();
    }

    private static IReadOnlyList<string> ReadStringListProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var enumerable = Assert.IsAssignableFrom<IEnumerable>(property!.GetValue(instance));
        return enumerable.Cast<object>()
            .Select(value => value?.ToString() ?? string.Empty)
            .ToList();
    }

    private static int? ReadNullableIntProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        if (value is null) {
            return null;
        }

        return Convert.ToInt32(value);
    }

    private static bool ReadBooleanProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var value = property!.GetValue(instance);
        return value is bool b && b;
    }

    private static IReadOnlyDictionary<string, int> ReadIntDictionaryProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var raw = property!.GetValue(instance);
        var dictionary = Assert.IsAssignableFrom<IEnumerable>(raw);
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dictionary) {
            var type = entry.GetType();
            var keyProperty = type.GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            var valueProperty = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(keyProperty);
            Assert.NotNull(valueProperty);
            var key = (keyProperty!.GetValue(entry)?.ToString() ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            var parsed = Convert.ToInt32(valueProperty!.GetValue(entry));
            result[key] = parsed;
        }

        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadStringListDictionaryProperty(object instance, string propertyName) {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        var raw = property!.GetValue(instance);
        var dictionary = Assert.IsAssignableFrom<IEnumerable>(raw);
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in dictionary) {
            var type = entry.GetType();
            var keyProperty = type.GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
            var valueProperty = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(keyProperty);
            Assert.NotNull(valueProperty);
            var key = (keyProperty!.GetValue(entry)?.ToString() ?? string.Empty).Trim();
            if (key.Length == 0) {
                continue;
            }

            var values = Assert.IsAssignableFrom<IEnumerable>(valueProperty!.GetValue(entry))
                .Cast<object>()
                .Select(value => value?.ToString() ?? string.Empty)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            result[key] = values;
        }

        return result;
    }
}
