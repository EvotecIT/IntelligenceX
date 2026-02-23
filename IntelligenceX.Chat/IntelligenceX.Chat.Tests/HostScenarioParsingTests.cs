using System;
using System.Collections;
using System.Linq;
using System.Reflection;
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
      "assert_not_contains": ["I can do that, but"],
      "min_tool_calls": 1,
      "min_tool_rounds": 1,
      "require_tools": ["eventlog_live_query"],
      "require_any_tools": ["eventlog_live_query", "eventlog_live_stats"],
      "forbid_tools": ["eventlog_evtx_query"],
      "assert_tool_output_contains": ["6008", "Kernel-Power"],
      "assert_tool_output_not_contains": ["schema_validation_failed"],
      "assert_no_tool_errors": true,
      "forbid_tool_error_codes": ["invalid_argument", "query_*"]
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
        Assert.Single(ReadStringListProperty(turns[0], "AssertNotContains"));
        Assert.Equal(1, ReadNullableIntProperty(turns[0], "MinToolCalls"));
        Assert.Equal(1, ReadNullableIntProperty(turns[0], "MinToolRounds"));
        Assert.Single(ReadStringListProperty(turns[0], "RequireTools"));
        Assert.Equal(2, ReadStringListProperty(turns[0], "RequireAnyTools").Count);
        Assert.Single(ReadStringListProperty(turns[0], "ForbidTools"));
        Assert.Equal(2, ReadStringListProperty(turns[0], "AssertToolOutputContains").Count);
        Assert.Single(ReadStringListProperty(turns[0], "AssertToolOutputNotContains"));
        Assert.True(ReadBooleanProperty(turns[0], "AssertNoToolErrors"));
        Assert.Equal(2, ReadStringListProperty(turns[0], "ForbidToolErrorCodes").Count);
        Assert.Equal("Check peer DCs", ReadStringProperty(turns[1], "User"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertContains"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertNotContains"));
        Assert.Null(ReadNullableIntProperty(turns[1], "MinToolCalls"));
        Assert.Null(ReadNullableIntProperty(turns[1], "MinToolRounds"));
        Assert.Empty(ReadStringListProperty(turns[1], "RequireTools"));
        Assert.Empty(ReadStringListProperty(turns[1], "RequireAnyTools"));
        Assert.Empty(ReadStringListProperty(turns[1], "ForbidTools"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertToolOutputContains"));
        Assert.Empty(ReadStringListProperty(turns[1], "AssertToolOutputNotContains"));
        Assert.False(ReadBooleanProperty(turns[1], "AssertNoToolErrors"));
        Assert.Empty(ReadStringListProperty(turns[1], "ForbidToolErrorCodes"));
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

    private static object InvokeParseScenarioDefinition(string raw, string fallbackName) {
        var programType = ResolveHostProgramType();
        var parseMethod = programType.GetMethod("ParseChatScenarioDefinition", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(parseMethod);
        var result = parseMethod!.Invoke(null, new object?[] { raw, fallbackName });
        Assert.NotNull(result);
        return result!;
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
}
