using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ActiveDirectory;
using IntelligenceX.Tools.EventLog;
using IntelligenceX.Tools.FileSystem;
using IntelligenceX.Tools.PowerShell;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolDefinitionContractTests {
    [Fact]
    public void RegisteredPacks_ShouldExposeUniqueToolsWithClosedObjectSchemas() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterFileSystemPack(new FileSystemToolOptions());
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var definitions = registry.GetDefinitions();
        Assert.NotEmpty(definitions);

        var duplicateNames = definitions
            .GroupBy(static d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static g => g.Count() > 1)
            .Select(static g => g.Key)
            .ToArray();
        Assert.Empty(duplicateNames);

        foreach (var definition in definitions) {
            Assert.NotNull(definition.Parameters);

            var schema = definition.Parameters!;
            Assert.Equal("object", schema.GetString("type"));
            Assert.NotNull(schema.GetObject("properties"));
            Assert.False(schema.GetBoolean("additionalProperties", defaultValue: true));
        }
    }

    [Fact]
    public void CorePackToolNames_ShouldRemainStable() {
        var registry = new ToolRegistry();
        registry.RegisterEventLogPack(new EventLogToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        var expected = new[] {
            "ad_pack_info",
            "ad_environment_discover",
            "ad_object_resolve",
            "ad_stale_accounts",
            "ad_spn_stats",
            "eventlog_pack_info",
            "eventlog_evtx_report_user_logons",
            "eventlog_evtx_report_failed_logons",
            "eventlog_evtx_report_account_lockouts"
        };

        foreach (var name in expected) {
            Assert.Contains(name, names);
        }
    }

    [Fact]
    public void SystemPack_ShouldExposeFirewallToolNames() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("system_pack_info", names);
        Assert.Contains("system_firewall_rules", names);
        Assert.Contains("system_firewall_profiles", names);
        Assert.Contains("system_logical_disks_list", names);
        Assert.Contains("system_disks_list", names);
        Assert.Contains("system_devices_summary", names);
        Assert.Contains("system_hardware_identity", names);
        Assert.Contains("system_hardware_summary", names);
        Assert.Contains("system_features_list", names);
    }

    [Fact]
    public void FileSystemPack_ShouldExposePackInfoTool() {
        var registry = new ToolRegistry();
        registry.RegisterFileSystemPack(new FileSystemToolOptions());

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("fs_pack_info", names);
    }

    [Fact]
    public void PowerShellPack_ShouldExposeRuntimeToolNames() {
        var registry = new ToolRegistry();
        registry.RegisterPowerShellPack(new PowerShellToolOptions {
            Enabled = true
        });

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("powershell_pack_info", names);
        Assert.Contains("powershell_environment_discover", names);
        Assert.Contains("powershell_hosts", names);
        Assert.Contains("powershell_run", names);
    }

    [Fact]
    public void TestimoXPack_ShouldExposeRuleCatalogAndRunTools() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions {
            Enabled = true
        });

        var names = new HashSet<string>(
            registry.GetDefinitions().Select(static d => d.Name),
            StringComparer.OrdinalIgnoreCase);

        Assert.Contains("testimox_pack_info", names);
        Assert.Contains("testimox_rules_list", names);
        Assert.Contains("testimox_rules_run", names);
    }
}
