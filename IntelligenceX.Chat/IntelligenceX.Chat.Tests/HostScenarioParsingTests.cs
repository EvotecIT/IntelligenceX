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
      "assert_contains": ["6008", "unexpected reboot"]
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
        Assert.Equal("Check peer DCs", ReadStringProperty(turns[1], "User"));
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
}
