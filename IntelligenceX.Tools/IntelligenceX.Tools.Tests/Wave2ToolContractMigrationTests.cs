using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Email;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.OfficeIMO;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class Wave2ToolContractMigrationTests {
    private static readonly HashSet<string> Wave2Packs = new(StringComparer.OrdinalIgnoreCase) {
        "system",
        "eventlog",
        "testimox",
        "filesystem",
        "email",
        "powershell",
        "officeimo"
    };

    [Fact]
    public void Wave2Tools_ShouldExposeExplicitRoutingSetupAndRecoveryContracts() {
        var definitions = BuildWave2CanonicalDefinitions();

        foreach (var definition in definitions) {
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.True(routing.IsRoutingAware);
            Assert.Equal(ToolRoutingTaxonomy.SourceExplicit, routing.RoutingSource, ignoreCase: true);
            Assert.Contains(routing.PackId, Wave2Packs, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(routing.Role, ToolRoutingTaxonomy.AllowedRoles, StringComparer.Ordinal);

            if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var setup = Assert.IsType<ToolSetupContract>(definition.Setup);
            Assert.True(setup.IsSetupAware);
            Assert.True(setup.Requirements.Count > 0 || setup.SetupHintKeys.Count > 0 || !string.IsNullOrWhiteSpace(setup.SetupToolName));

            var recovery = Assert.IsType<ToolRecoveryContract>(definition.Recovery);
            Assert.True(recovery.IsRecoveryAware);
        }
    }

    [Fact]
    public void Wave2Tools_ShouldKeepPackIdsConsistentWithToolNamespaces() {
        var definitions = BuildWave2CanonicalDefinitions();

        foreach (var definition in definitions) {
            var expectedPackId = ResolveExpectedPackId(definition.Name);
            var routing = Assert.IsType<ToolRoutingContract>(definition.Routing);
            Assert.Equal(expectedPackId, routing.PackId, ignoreCase: true);
        }
    }

    [Fact]
    public void EventLogWave2Tools_ShouldDeclareExplicitAdHandoffRoutes() {
        var definitionsByName = BuildWave2CanonicalDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var namedEvents = Assert.IsType<ToolDefinition>(definitionsByName["eventlog_named_events_query"]);
        var timeline = Assert.IsType<ToolDefinition>(definitionsByName["eventlog_timeline_query"]);

        AssertRouteToAdHandoffPrepare(namedEvents);
        AssertRouteToAdHandoffPrepare(timeline);
        AssertRouteToTarget(namedEvents, expectedPackId: "system", expectedToolName: "system_info");
        AssertRouteToTarget(namedEvents, expectedPackId: "system", expectedToolName: "system_metrics_summary");
        AssertRouteToTarget(timeline, expectedPackId: "system", expectedToolName: "system_info");
        AssertRouteToTarget(timeline, expectedPackId: "system", expectedToolName: "system_metrics_summary");
    }

    [Fact]
    public void EventLogWave2Tools_ShouldReferenceRegisteredChannelListForSetupAndRecovery() {
        var definitionsByName = BuildWave2CanonicalDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var timeline = Assert.IsType<ToolDefinition>(definitionsByName["eventlog_timeline_query"]);
        var setup = Assert.IsType<ToolSetupContract>(timeline.Setup);
        var recovery = Assert.IsType<ToolRecoveryContract>(timeline.Recovery);

        Assert.Equal("eventlog_channels_list", setup.SetupToolName, ignoreCase: true);
        Assert.Contains(recovery.RecoveryToolNames, tool => string.Equals(tool, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SystemWave2HostTools_ShouldDeclareReverseAdAndEventLogHandoffRoutes() {
        var definitionsByName = BuildWave2CanonicalDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRouteToTarget(definitionsByName["system_info"], expectedPackId: "active_directory", expectedToolName: "ad_scope_discovery");
        AssertRouteToTarget(definitionsByName["system_info"], expectedPackId: "eventlog", expectedToolName: "eventlog_channels_list");
        AssertRouteToTarget(definitionsByName["system_metrics_summary"], expectedPackId: "active_directory", expectedToolName: "ad_scope_discovery");
        AssertRouteToTarget(definitionsByName["system_metrics_summary"], expectedPackId: "eventlog", expectedToolName: "eventlog_channels_list");
        AssertRouteToTarget(definitionsByName["system_time_sync"], expectedPackId: "active_directory", expectedToolName: "ad_scope_discovery");
        AssertRouteToTarget(definitionsByName["system_time_sync"], expectedPackId: "eventlog", expectedToolName: "eventlog_channels_list");
    }

    [Fact]
    public void TestimoXWave2Tools_ShouldDeclareAdSystemAndEventLogFollowUpRoutes() {
        var definitionsByName = BuildWave2CanonicalDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRouteToTarget(definitionsByName["testimox_rules_run"], expectedPackId: "active_directory", expectedToolName: "ad_scope_discovery");
        AssertRouteToTarget(definitionsByName["testimox_rules_run"], expectedPackId: "system", expectedToolName: "system_info");
        AssertRouteToTarget(definitionsByName["testimox_rules_run"], expectedPackId: "system", expectedToolName: "system_metrics_summary");
        AssertRouteToTarget(definitionsByName["testimox_rules_run"], expectedPackId: "eventlog", expectedToolName: "eventlog_channels_list");

        AssertRouteToTarget(definitionsByName["testimox_run_summary"], expectedPackId: "active_directory", expectedToolName: "ad_scope_discovery");
        AssertRouteToTarget(definitionsByName["testimox_run_summary"], expectedPackId: "system", expectedToolName: "system_info");
        AssertRouteToTarget(definitionsByName["testimox_run_summary"], expectedPackId: "system", expectedToolName: "system_metrics_summary");
        AssertRouteToTarget(definitionsByName["testimox_run_summary"], expectedPackId: "eventlog", expectedToolName: "eventlog_channels_list");
    }

    [Fact]
    public void Wave2WriteCapableTools_ShouldDisableAutomaticRetries() {
        var definitionsByName = BuildWave2CanonicalDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var powershellRun = Assert.IsType<ToolDefinition>(definitionsByName["powershell_run"]);
        var smtpSend = Assert.IsType<ToolDefinition>(definitionsByName["email_smtp_send"]);

        var powershellRecovery = Assert.IsType<ToolRecoveryContract>(powershellRun.Recovery);
        Assert.True(powershellRecovery.IsRecoveryAware);
        Assert.False(powershellRecovery.SupportsTransientRetry);
        Assert.Equal(0, powershellRecovery.MaxRetryAttempts);

        var smtpRecovery = Assert.IsType<ToolRecoveryContract>(smtpSend.Recovery);
        Assert.True(smtpRecovery.IsRecoveryAware);
        Assert.False(smtpRecovery.SupportsTransientRetry);
        Assert.Equal(0, smtpRecovery.MaxRetryAttempts);
    }

    private static IReadOnlyList<ToolDefinition> BuildWave2CanonicalDefinitions() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions {
            Enabled = true
        });
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterEmailPack(new EmailToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions {
            Enabled = true
        });
        registry.RegisterOfficeImoPack(new OfficeImoToolOptions());

        return registry.GetDefinitions()
            .Where(static definition => string.IsNullOrWhiteSpace(definition.AliasOf))
            .Where(definition => Wave2Packs.Contains(definition.Routing?.PackId ?? string.Empty))
            .ToArray();
    }

    private static string ResolveExpectedPackId(string toolName) {
        if (toolName.StartsWith("system_", StringComparison.OrdinalIgnoreCase)
            || toolName.StartsWith("wsl_", StringComparison.OrdinalIgnoreCase)) {
            return "system";
        }

        if (toolName.StartsWith("eventlog_", StringComparison.OrdinalIgnoreCase)) {
            return "eventlog";
        }

        if (toolName.StartsWith("testimox_", StringComparison.OrdinalIgnoreCase)) {
            return "testimox";
        }

        if (toolName.StartsWith("fs_", StringComparison.OrdinalIgnoreCase)) {
            return "filesystem";
        }

        if (toolName.StartsWith("email_", StringComparison.OrdinalIgnoreCase)) {
            return "email";
        }

        if (toolName.StartsWith("powershell_", StringComparison.OrdinalIgnoreCase)) {
            return "powershell";
        }

        if (toolName.StartsWith("officeimo_", StringComparison.OrdinalIgnoreCase)) {
            return "officeimo";
        }

        throw new InvalidOperationException($"Cannot infer expected pack id for '{toolName}'.");
    }

    private static void AssertRouteToAdHandoffPrepare(ToolDefinition definition) {
        var handoff = Assert.IsType<ToolHandoffContract>(definition.Handoff);
        Assert.True(handoff.IsHandoffAware);
        Assert.Contains(
            handoff.OutboundRoutes,
            static route =>
                string.Equals(route.TargetPackId, "active_directory", StringComparison.OrdinalIgnoreCase)
                && string.Equals(route.TargetToolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertRouteToTarget(ToolDefinition definition, string expectedPackId, string expectedToolName) {
        var handoff = Assert.IsType<ToolHandoffContract>(definition.Handoff);
        Assert.True(handoff.IsHandoffAware);
        Assert.Contains(
            handoff.OutboundRoutes,
            route =>
                string.Equals(route.TargetPackId, expectedPackId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(route.TargetToolName, expectedToolName, StringComparison.OrdinalIgnoreCase));
    }
}
