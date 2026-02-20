using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Email;
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
            "ad_handoff_prepare",
            "ad_object_resolve",
            "ad_stale_accounts",
            "ad_spn_stats",
            "eventlog_pack_info",
            "eventlog_named_events_catalog",
            "eventlog_named_events_query",
            "eventlog_timeline_explain",
            "eventlog_timeline_query",
            "eventlog_top_events",
            "eventlog_evtx_query",
            "eventlog_evtx_stats"
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
        Assert.Contains("system_security_options", names);
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
    public void WriteCapableTools_ShouldDeclareGovernanceContracts() {
        var registry = new ToolRegistry();
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(definitionsByName.TryGetValue("powershell_run", out var powershellRun));
        Assert.NotNull(powershellRun.WriteGovernance);
        Assert.True(powershellRun.WriteGovernance!.IsWriteCapable);
        Assert.True(powershellRun.WriteGovernance.RequiresGovernanceAuthorization);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, powershellRun.WriteGovernance.GovernanceContractId);

        Assert.True(definitionsByName.TryGetValue("email_smtp_send", out var smtpSend));
        Assert.NotNull(smtpSend.WriteGovernance);
        Assert.True(smtpSend.WriteGovernance!.IsWriteCapable);
        Assert.True(smtpSend.WriteGovernance.RequiresGovernanceAuthorization);
        Assert.Equal(ToolWriteGovernanceContract.DefaultContractId, smtpSend.WriteGovernance.GovernanceContractId);
        Assert.NotNull(smtpSend.Authentication);
        Assert.True(smtpSend.Authentication!.IsAuthenticationAware);
        Assert.True(smtpSend.Authentication.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, smtpSend.Authentication.AuthenticationContractId);
        Assert.Equal(ToolAuthenticationMode.HostManaged, smtpSend.Authentication.Mode);
        Assert.True(smtpSend.Authentication.SupportsConnectivityProbe);
        Assert.Equal("email_smtp_probe", smtpSend.Authentication.ProbeToolName);
        Assert.Contains(ToolAuthenticationArgumentNames.ProbeId, smtpSend.Authentication.GetSchemaArgumentNames());
        var smtpSendProperties = smtpSend.Parameters?.GetObject("properties");
        Assert.NotNull(smtpSendProperties);
        Assert.NotNull(smtpSendProperties!.GetObject(ToolAuthenticationArgumentNames.ProbeId));

        Assert.True(definitionsByName.TryGetValue("email_smtp_probe", out var smtpProbe));
        Assert.Null(smtpProbe.WriteGovernance);
        Assert.NotNull(smtpProbe.Authentication);
        Assert.True(smtpProbe.Authentication!.IsAuthenticationAware);
        Assert.True(smtpProbe.Authentication.RequiresAuthentication);
        Assert.Equal(ToolAuthenticationContract.DefaultContractId, smtpProbe.Authentication.AuthenticationContractId);
        Assert.Equal(ToolAuthenticationMode.HostManaged, smtpProbe.Authentication.Mode);
    }

    [Fact]
    public void WriteCapableTools_ShouldExposeCanonicalGovernanceMetadataArguments() {
        var registry = new ToolRegistry();
        registry.RegisterPowerShellPack(new PowerShellToolOptions { Enabled = true });
        registry.RegisterEmailPack(new EmailToolOptions());

        var writeCapableDefinitions = registry.GetDefinitions()
            .Where(static definition => definition.WriteGovernance?.IsWriteCapable == true)
            .ToArray();
        Assert.NotEmpty(writeCapableDefinitions);

        foreach (var definition in writeCapableDefinitions) {
            var properties = definition.Parameters?.GetObject("properties");
            Assert.NotNull(properties);
            Assert.NotNull(properties!.GetObject(ToolWriteGovernanceArgumentNames.ExecutionId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ActorId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.ChangeReason));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackPlanId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.RollbackProviderId));
            Assert.NotNull(properties.GetObject(ToolWriteGovernanceArgumentNames.AuditCorrelationId));
        }
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
