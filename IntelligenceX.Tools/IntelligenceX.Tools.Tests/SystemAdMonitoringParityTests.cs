using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class SystemAdMonitoringParityTests {
    [Theory]
    [InlineData(typeof(SystemHardwareSummaryTool))]
    [InlineData(typeof(SystemMetricsSummaryTool))]
    [InlineData(typeof(SystemInfoTool))]
    [InlineData(typeof(SystemProcessListTool))]
    [InlineData(typeof(SystemNetworkAdaptersTool))]
    [InlineData(typeof(SystemPortsListTool))]
    [InlineData(typeof(SystemServiceListTool))]
    [InlineData(typeof(SystemScheduledTasksListTool))]
    [InlineData(typeof(SystemDevicesSummaryTool))]
    [InlineData(typeof(SystemFeaturesListTool))]
    public void RemoteComputerXParityTools_Definition_ShouldExposeOptionalComputerName(Type toolType) {
        var tool = (ITool)Activator.CreateInstance(toolType, new SystemToolOptions())!;
        var properties = tool.Definition.Parameters?.GetObject("properties");

        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject("computer_name"));
    }

    [Fact]
    public void SystemPackRegistry_ShouldExposeSystemMetricsSummary() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var names = registry.GetDefinitions()
            .Select(static definition => definition.Name)
            .ToArray();

        Assert.Contains("system_metrics_summary", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldListWindowsUpdateProbe() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds")
            .EnumerateArray()
            .Select(static node => node.GetProperty("probe_kind").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Contains("windows_update", probeKinds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdMonitoringProbeRun_Definition_ShouldAcceptWindowsUpdateProbeKindAndRequireWsusOption() {
        var tool = new AdMonitoringProbeRunTool(new ActiveDirectoryToolOptions());
        var schema = tool.Definition.Parameters;
        Assert.NotNull(schema);

        var properties = schema!.GetObject("properties");
        Assert.NotNull(properties);

        var probeKind = properties!.GetObject("probe_kind");
        Assert.NotNull(probeKind);

        var enumValues = probeKind!.GetArray("enum");
        Assert.NotNull(enumValues);
        Assert.Contains(
            "windows_update",
            enumValues!.Select(static value => value.AsString()).Where(static value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(properties.GetObject("require_wsus"));
    }
}
