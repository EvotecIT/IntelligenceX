using System;
using System.Collections.Generic;
using System.Reflection;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo SelectDeterministicToolSubsetMethod =
        typeof(ChatServiceSession).GetMethod("SelectDeterministicToolSubset", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("SelectDeterministicToolSubset not found.");

    [Fact]
    public void SelectDeterministicToolSubset_UsesCatalogPackFamiliesInsteadOfNamePrefixes() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "alpha_inventory",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        registry.Register(new PreflightStubTool(
            "beta_inventory",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleResolver)));
        registry.Register(new PreflightStubTool(
            "gamma_inventory",
            CreateRoutingContract("dnsclientx", ToolRoutingTaxonomy.RoleOperational)));
        SetSessionRegistry(session, registry);

        var result = SelectDeterministicToolSubsetMethod.Invoke(session, new object?[] { registry.GetDefinitions(), 2 });
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, static definition => string.Equals(definition.Name, "alpha_inventory", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, static definition => string.Equals(definition.Name, "gamma_inventory", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(selected, static definition => string.Equals(definition.Name, "beta_inventory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SelectDeterministicToolSubset_DoesNotGroupUnassignedToolsByNamePrefix() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definitions = new List<ToolDefinition> {
            new(
                name: "alpha_one",
                description: "alpha",
                routing: CreateRoutingContract(packId: string.Empty, role: ToolRoutingTaxonomy.RoleOperational)),
            new(
                name: "alpha_two",
                description: "alpha",
                routing: CreateRoutingContract(packId: string.Empty, role: ToolRoutingTaxonomy.RoleOperational)),
            new(
                name: "beta_one",
                description: "beta",
                routing: CreateRoutingContract(packId: string.Empty, role: ToolRoutingTaxonomy.RoleOperational))
        };

        var result = SelectDeterministicToolSubsetMethod.Invoke(session, new object?[] { definitions, 2 });
        var selected = Assert.IsAssignableFrom<IReadOnlyList<ToolDefinition>>(result);

        Assert.Equal(2, selected.Count);
        Assert.Contains(selected, static definition => string.Equals(definition.Name, "alpha_one", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(selected, static definition => string.Equals(definition.Name, "alpha_two", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(selected, static definition => string.Equals(definition.Name, "beta_one", StringComparison.OrdinalIgnoreCase));
    }
}
