using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolRegistryReplacementTests {

    [Fact]
    public void Register_WithReplaceExisting_ShouldReplaceCanonicalAndAliasMetadataDeterministically() {
        var registry = new ToolRegistry();

        registry.Register(new StubTool(
            new ToolDefinition(
                name: "custom_probe",
                description: "v1",
                parameters: null,
                tags: new[] { "first" },
                aliases: new[] {
                    new ToolAliasDefinition(
                        name: "custom_probe_alias",
                        description: "alias-v1",
                        tags: new[] { "scope:domain", "operation:search" })
                })));

        Assert.True(registry.TryGetDefinition("custom_probe_alias", out var aliasBefore));
        Assert.NotNull(aliasBefore);
        Assert.Contains("scope:domain", aliasBefore!.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:search", aliasBefore.Tags, StringComparer.OrdinalIgnoreCase);

        registry.Register(new StubTool(
            new ToolDefinition(
                name: "custom_probe",
                description: "v2",
                parameters: null,
                tags: new[] { "second" },
                aliases: new[] {
                    new ToolAliasDefinition(
                        name: "custom_probe_new_alias",
                        description: "alias-v2",
                        tags: new[] { "risk:high", "routing:explicit" })
                })),
            replaceExisting: true);

        Assert.True(registry.TryGetDefinition("custom_probe", out var canonicalAfter));
        Assert.NotNull(canonicalAfter);
        Assert.Equal("v2", canonicalAfter!.Description);
        Assert.Contains("second", canonicalAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("scope:general", canonicalAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", canonicalAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", canonicalAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:low", canonicalAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:inferred", canonicalAfter.Tags, StringComparer.OrdinalIgnoreCase);

        Assert.False(registry.TryGetDefinition("custom_probe_alias", out _));

        Assert.True(registry.TryGetDefinition("custom_probe_new_alias", out var aliasAfter));
        Assert.NotNull(aliasAfter);
        Assert.Equal("custom_probe", aliasAfter!.CanonicalName);
        Assert.Equal("alias-v2", aliasAfter.Description);
        Assert.Contains("scope:general", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("operation:probe", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("entity:resource", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(aliasAfter.Tags, "scope:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "operation:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "entity:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "risk:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "routing:");
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class StubTool : ITool {
        public StubTool(ToolDefinition definition) {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("{}");
        }
    }
}
