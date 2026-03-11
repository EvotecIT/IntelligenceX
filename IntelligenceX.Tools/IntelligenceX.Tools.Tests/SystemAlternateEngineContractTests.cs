using System;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class SystemAlternateEngineContractTests {
    [Fact]
    public void SystemServiceListTool_Definition_ShouldExposeExplicitEngineSelector() {
        var tool = new SystemServiceListTool(new SystemToolOptions());
        var properties = tool.Definition.Parameters?.GetObject("properties");
        Assert.NotNull(properties);

        var engine = properties!.GetObject("engine");
        Assert.NotNull(engine);

        var enumValues = engine!.GetArray("enum");
        Assert.NotNull(enumValues);
        var actualValues = enumValues!
            .Select(static value => value.AsString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.True(
            new[] { "auto", "native", "wmi", "cim" }.SequenceEqual(actualValues, StringComparer.OrdinalIgnoreCase),
            $"Unexpected engine enum values: {string.Join(", ", actualValues)}");
    }

    [Fact]
    public void SystemPackRecoveryContracts_ShouldAdvertiseAlternateEnginesOnlyWhenSchemaSupportsSelection() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .Where(static definition => string.IsNullOrWhiteSpace(definition.AliasOf))
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var serviceList = Assert.IsType<ToolDefinition>(definitionsByName["system_service_list"]);
        var biosSummary = Assert.IsType<ToolDefinition>(definitionsByName["system_bios_summary"]);

        var serviceRecovery = Assert.IsType<ToolRecoveryContract>(serviceList.Recovery);
        Assert.True(serviceRecovery.SupportsAlternateEngines);
        Assert.True(
            new[] { "cim", "wmi" }.SequenceEqual(serviceRecovery.AlternateEngineIds, StringComparer.OrdinalIgnoreCase),
            $"Unexpected alternate engines: {string.Join(", ", serviceRecovery.AlternateEngineIds)}");

        var biosRecovery = Assert.IsType<ToolRecoveryContract>(biosSummary.Recovery);
        Assert.False(biosRecovery.SupportsAlternateEngines);
        Assert.Empty(biosRecovery.AlternateEngineIds);
    }
}
