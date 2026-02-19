using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ActiveDirectoryRequiredDomainContractTests {
    [Fact]
    public async Task AdTools_WithOnlyDomainAsRequiredArgument_ShouldUseStandardMissingDomainError() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var definitions = registry.GetDefinitions()
            .Where(static definition => definition.Name.StartsWith("ad_", StringComparison.OrdinalIgnoreCase))
            .Where(static definition => HasOnlyRequiredDomainName(definition.Parameters))
            .OrderBy(static definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.NotEmpty(definitions);

        foreach (var definition in definitions) {
            Assert.True(registry.TryGet(definition.Name, out var tool), $"Tool not found in registry: {definition.Name}");
            var json = await tool!.InvokeAsync(new JsonObject(), CancellationToken.None);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            Assert.False(root.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_argument", root.GetProperty("error_code").GetString());
            Assert.Equal("domain_name is required.", root.GetProperty("error").GetString());
        }
    }

    private static bool HasOnlyRequiredDomainName(JsonObject? parameters) {
        if (parameters is null) {
            return false;
        }

        var required = ReadRequired(parameters);
        return required.Count == 1 && string.Equals(required[0], "domain_name", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ReadRequired(JsonObject parameters) {
        var required = parameters.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        var values = new List<string>(required.Count);
        for (var i = 0; i < required.Count; i++) {
            var value = required[i].AsString();
            if (!string.IsNullOrWhiteSpace(value)) {
                values.Add(value.Trim());
            }
        }

        return values;
    }
}
