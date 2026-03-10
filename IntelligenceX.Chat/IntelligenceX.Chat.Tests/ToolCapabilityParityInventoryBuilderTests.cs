using System;
using System.Linq;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Tests for runtime phase-1 parity inventory generation.
/// </summary>
public sealed class ToolCapabilityParityInventoryBuilderTests {
    /// <summary>
    /// Ensures the runtime parity inventory reflects current remote-read-only coverage truth instead of crediting local-only wrappers.
    /// </summary>
    [Fact]
    public void Build_WithLivePacks_ReportsComputerXHealthyAndGovernedBacklog() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            registry.GetDefinitions(),
            new[] {
                CreateEnabledPack("system", "System"),
                CreateEnabledPack("active_directory", "Active Directory"),
                CreateEnabledPack("testimox", "TestimoX")
            });

        var ad = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "adplayground_monitoring", StringComparison.OrdinalIgnoreCase));
        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));
        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));
        var governed = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_powershell", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, ad.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, system.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, testimox.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.GovernedBacklogStatus, governed.Status);
        Assert.Empty(system.MissingCapabilities);
        Assert.True(entries.Sum(static entry => entry.MissingCapabilityCount) >= 0);
    }

    /// <summary>
    /// Ensures the parity inventory flags a gap when a required remote ComputerX wrapper contract is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemMetricsWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_metrics_summary", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_metrics_summary", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the profile catalog wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXProfilesWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_profiles_list", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("profile_catalog", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the report job history wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXReportJobHistoryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_report_job_history", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("report_job_history", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the maintenance window history wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXMaintenanceWindowHistoryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_maintenance_window_history", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("maintenance_window_history", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the probe index status wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXProbeIndexStatusWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_probe_index_status", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("probe_index_status", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the monitoring diagnostics wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXMonitoringDiagnosticsWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_monitoring_diagnostics_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("monitoring_diagnostics", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the monitoring history wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXHistoryQueryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_history_query", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("monitoring_history", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the report data snapshot wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXReportDataSnapshotWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_report_data_snapshot_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("report_data_snapshot", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the HTML report snapshot wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXReportSnapshotWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_report_snapshot_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("report_snapshot", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the baseline catalog wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXBaselinesListWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_baselines_list", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("baseline_catalog", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the baseline compare wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXBaselineCompareWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_baseline_compare", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("baseline_compare", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the source provenance wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXSourceQueryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_source_query", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("source_provenance", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the baseline crosswalk wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXBaselineCrosswalkWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_baseline_crosswalk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("baseline_crosswalk", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures local-only wrappers do not count toward remote parity simply because the tool name is registered.
    /// </summary>
    [Fact]
    public void Build_WhenLocalOnlySystemWrapperExists_DoesNotCreditRemoteParity() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var definitions = registry.GetDefinitions()
            .Select(static definition => {
                if (!string.Equals(definition.Name, "system_process_list", StringComparison.OrdinalIgnoreCase)) {
                    return definition;
                }

                return new ToolDefinition(
                    name: definition.Name,
                    description: definition.Description,
                    parameters: ToolSchema.Object(
                            ("name_contains", ToolSchema.String("Optional case-insensitive name filter.")),
                            ("max_processes", ToolSchema.Integer("Optional maximum processes to return (capped).")))
                        .WithTableViewOptions()
                        .NoAdditionalProperties(),
                    displayName: definition.DisplayName,
                    category: definition.Category,
                    tags: definition.Tags,
                    writeGovernance: definition.WriteGovernance,
                    aliases: definition.Aliases,
                    aliasOf: definition.AliasOf,
                    authentication: definition.Authentication,
                    routing: definition.Routing,
                    setup: definition.Setup,
                    handoff: definition.Handoff,
                    recovery: definition.Recovery);
            })
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            definitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_process_inventory", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures parity detail summaries expose surfaced-vs-expected counts and governed backlog notes.
    /// </summary>
    [Fact]
    public void BuildDetailSummaries_WithLivePacks_EmitsDeltaAndGovernedDetail() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            registry.GetDefinitions(),
            new[] {
                CreateEnabledPack("system", "System"),
                CreateEnabledPack("active_directory", "Active Directory"),
                CreateEnabledPack("testimox", "TestimoX")
            });

        var details = ToolCapabilityParityInventoryBuilder.BuildDetailSummaries(entries, maxItems: 8);

        Assert.Contains(details, static line => line.Contains("computerx [healthy]", StringComparison.OrdinalIgnoreCase)
            && line.Contains("surfaced=", StringComparison.OrdinalIgnoreCase)
            && line.Contains("registered_tools=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(details, static line => line.Contains("testimox_powershell [governed_backlog]", StringComparison.OrdinalIgnoreCase));
    }

    private static ToolPackAvailabilityInfo CreateEnabledPack(string id, string name) {
        return new ToolPackAvailabilityInfo {
            Id = id,
            Name = name,
            SourceKind = "builtin",
            Enabled = true
        };
    }
}
