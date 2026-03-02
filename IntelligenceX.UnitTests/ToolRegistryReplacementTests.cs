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
                tags: new[] { "first", "pack:custom" },
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
                tags: new[] { "second", "pack:custom" },
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

    [Fact]
    public void Register_ShouldPreferNamePrefixCategoryOverRuntimeToolNamespace() {
        var registry = new ToolRegistry();
        registry.Register(new IntelligenceX.Tools.System.WrappedSystemTool(
            new ToolDefinition(
                name: "ad_custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "pack:active_directory" })));

        Assert.True(registry.TryGetDefinition("ad_custom_probe", out var definition));
        Assert.NotNull(definition);
        Assert.Equal("active_directory", definition!.Category);
        Assert.Contains("active_directory", definition.Tags, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterAlias_WithReplaceExisting_ShouldRebindAliasToNewCanonicalTool() {
        var registry = new ToolRegistry();

        var probeTool = new StubTool(
            new ToolDefinition(
                name: "custom_probe",
                description: "Probe",
                parameters: null,
                tags: new[] { "pack:custom" }),
            output: "probe");
        var searchTool = new StubTool(
            new ToolDefinition(
                name: "custom_search",
                description: "Search",
                parameters: null,
                tags: new[] { "pack:custom" }),
            output: "search");

        registry.Register(probeTool);
        registry.Register(searchTool);

        registry.RegisterAlias(
            aliasName: "shared_alias",
            targetToolName: "custom_probe",
            tags: new[] { "routing:explicit", "risk:high" });

        Assert.True(registry.TryGetDefinition("shared_alias", out var aliasBefore));
        Assert.NotNull(aliasBefore);
        Assert.Equal("custom_probe", aliasBefore!.CanonicalName);
        Assert.Contains("operation:probe", aliasBefore.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", aliasBefore.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", aliasBefore.Tags, StringComparer.OrdinalIgnoreCase);

        Assert.True(registry.TryGet("shared_alias", out var aliasToolBefore));
        Assert.NotNull(aliasToolBefore);
        var beforeOutput = await aliasToolBefore!.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        Assert.Equal("probe", beforeOutput);

        registry.RegisterAlias(
            aliasName: "shared_alias",
            targetToolName: "custom_search",
            tags: new[] { "routing:explicit", "risk:high" },
            replaceExisting: true);

        Assert.True(registry.TryGetDefinition("shared_alias", out var aliasAfter));
        Assert.NotNull(aliasAfter);
        Assert.Equal("custom_search", aliasAfter!.CanonicalName);
        Assert.Contains("operation:search", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("risk:high", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("routing:explicit", aliasAfter.Tags, StringComparer.OrdinalIgnoreCase);
        AssertSingleTaxonomyTag(aliasAfter.Tags, "scope:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "operation:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "entity:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "risk:");
        AssertSingleTaxonomyTag(aliasAfter.Tags, "routing:");

        Assert.True(registry.TryGet("shared_alias", out var aliasToolAfter));
        Assert.NotNull(aliasToolAfter);
        var afterOutput = await aliasToolAfter!.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);
        Assert.Equal("search", afterOutput);
    }

    private static void AssertSingleTaxonomyTag(IReadOnlyList<string> tags, string prefix) {
        Assert.Equal(
            1,
            tags.Count(tag => tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private sealed class StubTool : ITool {
        private readonly string _output;

        public StubTool(ToolDefinition definition, string output = "{}") {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _output = output ?? string.Empty;
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            _ = arguments;
            _ = cancellationToken;
            return Task.FromResult(_output);
        }
    }
}
