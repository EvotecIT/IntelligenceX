using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    [Theory]
    [MemberData(nameof(SchemaSnapshots))]
    public void SelectedToolSchemas_ShouldMatchSnapshot(string toolName, string[] expectedProperties, string[] expectedRequired) {
        var definition = GetDefinition(toolName);
        Assert.NotNull(definition.Parameters);
        var schema = definition.Parameters!;

        Assert.Equal("object", schema.GetString("type"));
        Assert.False(schema.GetBoolean("additionalProperties", defaultValue: true));

        var properties = schema.GetObject("properties");
        Assert.NotNull(properties);

        var actualProperties = GetObjectKeys(properties!)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var expectedSortedProperties = expectedProperties
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSortedProperties, actualProperties);

        foreach (var name in expectedProperties) {
            Assert.NotNull(properties!.GetObject(name));
        }

        if (expectedRequired.Length == 0) {
            var requiredOptional = schema.GetArray("required");
            if (requiredOptional is not null) {
                Assert.Empty(ReadArrayStrings(requiredOptional));
            }
            return;
        }

        var required = schema.GetArray("required");
        Assert.NotNull(required);

        var actualRequired = ReadArrayStrings(required!)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        var expectedSortedRequired = expectedRequired
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedSortedRequired, actualRequired);
    }

    [Fact]
    public void AllRegisteredAdToolSchemas_ShouldBeCoveredBySnapshots() {
        var snapshotNames = new HashSet<string>(
            SchemaSnapshots()
                .Select(static row => row[0] as string)
                .Where(static name => !string.IsNullOrWhiteSpace(name) && name.StartsWith("ad_", StringComparison.OrdinalIgnoreCase))
                .Select(static name => name!),
            StringComparer.OrdinalIgnoreCase);

        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var actualNames = registry.GetDefinitions()
            .Select(static d => d.Name)
            .Where(static n => n.StartsWith("ad_", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            snapshotNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase),
            actualNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase));
    }

    public static IEnumerable<object[]> SchemaSnapshots() {
        foreach (var snapshot in ActiveDirectorySchemaSnapshots()) {
            yield return snapshot;
        }

        foreach (var snapshot in SystemSchemaSnapshots()) {
            yield return snapshot;
        }

        foreach (var snapshot in EventLogAndPowerShellSchemaSnapshots()) {
            yield return snapshot;
        }
    }

    private static ToolDefinition GetDefinition(string toolName) {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });

        return registry.GetDefinitions()
            .Single(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ReadArrayStrings(JsonArray array) {
        var list = new List<string>(array.Count);
        for (var i = 0; i < array.Count; i++) {
            var value = array[i].AsString();
            if (!string.IsNullOrWhiteSpace(value)) {
                list.Add(value.Trim());
            }
        }
        return list;
    }

    private static IReadOnlyList<string> GetObjectKeys(JsonObject obj) {
        var keysProperty = obj.GetType().GetProperty("Keys");
        if (keysProperty?.GetValue(obj) is IEnumerable<string> keys) {
            return keys
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .ToArray();
        }

        if (obj is global::System.Collections.IEnumerable enumerable) {
            var list = new List<string>();
            foreach (var item in enumerable) {
                if (item is null) {
                    continue;
                }
                var keyProperty = item.GetType().GetProperty("Key");
                var key = keyProperty?.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(key)) {
                    list.Add(key.Trim());
                }
            }
            if (list.Count > 0) {
                return list;
            }

            return Array.Empty<string>();
        }

        return Array.Empty<string>();
    }
}
