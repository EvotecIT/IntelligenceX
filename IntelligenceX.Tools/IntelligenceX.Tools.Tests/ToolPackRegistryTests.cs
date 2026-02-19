using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolPackRegistryTests {
    [Fact]
    public void GetRegisteredToolNames_ReturnsFactoryOrder() {
        var names = ToolPackRegistry.GetRegisteredToolNames(
            new StubOptions(),
            static _ => new ITool[] {
                new StubTool("stub_a"),
                new StubTool("stub_b")
            });

        Assert.Equal(new[] { "stub_a", "stub_b" }, names);
    }

    [Fact]
    public void GetRegisteredToolCatalog_ReturnsCatalogEntries() {
        var catalog = ToolPackRegistry.GetRegisteredToolCatalog(
            new StubOptions(),
            static _ => new ITool[] {
                new StubTool("stub_catalog", "Catalog test tool")
            });

        ToolPackToolCatalogEntryModel entry = Assert.Single(catalog);
        Assert.Equal("stub_catalog", entry.Name);
        Assert.Equal("Catalog test tool", entry.Description);
    }

    [Fact]
    public void RegisterPack_RegistersEveryToolAndReturnsRegistry() {
        var registry = new ToolRegistry();

        ToolRegistry returned = ToolPackRegistry.RegisterPack(
            registry,
            new StubOptions(),
            static _ => new ITool[] {
                new StubTool("stub_register_a"),
                new StubTool("stub_register_b")
            });

        Assert.Same(registry, returned);
        Assert.True(registry.TryGet("stub_register_a", out _));
        Assert.True(registry.TryGet("stub_register_b", out _));
    }

    private sealed class StubOptions { }

    private sealed class StubTool : ITool {
        public StubTool(string name, string? description = null) {
            Definition = new ToolDefinition(
                name,
                description,
                ToolSchema.Object(("q", ToolSchema.String("query"))).NoAdditionalProperties());
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return Task.FromResult("""{"ok":true}""");
        }
    }
}
