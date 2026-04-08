using System.Collections.Generic;
using IntelligenceX.Chat.Host;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class HostDomainIntentRoutingTests {
    [Fact]
    public void ExtractScenarioUserRequestForDomainIntentRoutingForTesting_ReturnsTrailingUserBlock() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
User request:
[DomainIntent]
ix:domain-intent:v1
family: public_domain
""";

        var extracted = Program.ExtractScenarioUserRequestForDomainIntentRoutingForTesting(prompt);

        Assert.Equal("[DomainIntent]\nix:domain-intent:v1\nfamily: public_domain", extracted);
    }

    [Fact]
    public void TryResolveDomainIntentFamilySelectionForTesting_ResolvesStructuredPublicMarkerInsideScenarioPrompt() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
requires_tool_execution: true
User request:
[DomainIntent]
ix:domain-intent:v1
family: public_domain
""";

        var resolved = Program.TryResolveDomainIntentFamilySelectionForTesting(
            prompt,
            BuildMixedDomainToolDefinitions(),
            pendingFamilies: null,
            out var family);

        Assert.True(resolved);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyPublic, family);
    }

    [Fact]
    public void TryResolveDomainIntentFamilySelectionForTesting_MapsArabicIndicOrdinalUsingPendingFamilies() {
        var resolved = Program.TryResolveDomainIntentFamilySelectionForTesting(
            "١",
            BuildMixedDomainToolDefinitions(),
            new[] {
                ToolSelectionMetadata.DomainIntentFamilyAd,
                ToolSelectionMetadata.DomainIntentFamilyPublic
            },
            out var family);

        Assert.True(resolved);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, family);
    }

    [Fact]
    public void TryResolveDomainIntentFamilySelectionForTesting_MapsFullwidthOrdinalUsingPendingFamilies() {
        var resolved = Program.TryResolveDomainIntentFamilySelectionForTesting(
            "２",
            BuildMixedDomainToolDefinitions(),
            new[] {
                ToolSelectionMetadata.DomainIntentFamilyAd,
                ToolSelectionMetadata.DomainIntentFamilyPublic
            },
            out var family);

        Assert.True(resolved);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyPublic, family);
    }

    [Fact]
    public void TryResolveDomainIntentFamilySelectionForTesting_ResolvesActionSelectionPayloadFamily() {
        const string payload = """
{"ix_action_selection":{"id":"act_domain_scope_ad","title":"ad_domain","request":{"ix_domain_scope":{"family":"ad_domain"}},"mutating":false}}
""";

        var resolved = Program.TryResolveDomainIntentFamilySelectionForTesting(
            payload,
            BuildMixedDomainToolDefinitions(),
            pendingFamilies: null,
            out var family);

        Assert.True(resolved);
        Assert.Equal(ToolSelectionMetadata.DomainIntentFamilyAd, family);
    }

    [Fact]
    public void HasMixedDomainIntentSignalsForTesting_DetectsAdAndPublicDnsInSameAsk() {
        var mixed = Program.HasMixedDomainIntentSignalsForTesting("Please do AD LDAP + DNS MX together now.");

        Assert.True(mixed);
    }

    [Fact]
    public void ExtractDomainLikeTokensForTesting_IgnoresResolverIpLiterals() {
        var tokens = Program.ExtractDomainLikeTokensForTesting(
            "Collect A, AAAA, MX, SPF, and DMARC evidence for contoso.com using 1.1.1.1 and 8.8.8.8.");

        Assert.Single(tokens);
        Assert.Contains("contoso.com", tokens);
    }

    [Fact]
    public void TryFilterToolsByDomainIntentFamilyForTesting_RemovesOppositeFamilyTools() {
        var filtered = Program.TryFilterToolsByDomainIntentFamilyForTesting(
            BuildMixedDomainToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyPublic,
            out var filteredTools,
            out var removedCount);

        Assert.True(filtered);
        Assert.Equal(2, removedCount);
        Assert.All(filteredTools, static tool =>
            Assert.Contains("domain_family:public_domain", tool.Tags, System.StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryBuildSyntheticDomainIntentBootstrapForTesting_BuildsPublicDomainSummaryFromStructuredSelection() {
        var resolved = Program.TryBuildSyntheticDomainIntentBootstrapForTesting(
            "[DomainIntent]\nix:domain-intent:v1\nfamily: public_domain",
            BuildMixedDomainBootstrapToolDefinitions(),
            pendingFamilies: null,
            rememberedAdTarget: "corp.contoso.com",
            rememberedPublicTarget: "contoso.com",
            out var toolName,
            out var argumentsJson);

        Assert.True(resolved);
        Assert.Equal("domaindetective_domain_summary", toolName);
        Assert.Contains("\"domain\":\"contoso.com\"", argumentsJson);
    }

    [Fact]
    public void TryBuildSyntheticDomainIntentBootstrapForTesting_BuildsPublicPackInfoWithoutRememberedTarget() {
        var resolved = Program.TryBuildSyntheticDomainIntentBootstrapForTesting(
            "[DomainIntent]\nix:domain-intent:v1\nfamily: public_domain",
            BuildMixedDomainBootstrapToolDefinitions(),
            pendingFamilies: null,
            rememberedAdTarget: string.Empty,
            rememberedPublicTarget: string.Empty,
            out var toolName,
            out var argumentsJson);

        Assert.True(resolved);
        Assert.Equal("domaindetective_pack_info", toolName);
        Assert.Equal("{}", argumentsJson);
    }

    [Fact]
    public void TryBuildSyntheticDomainIntentBootstrapForTesting_BuildsAdScopeFromNumericSelection() {
        var resolved = Program.TryBuildSyntheticDomainIntentBootstrapForTesting(
            "١",
            BuildMixedDomainBootstrapToolDefinitions(),
            new[] {
                ToolSelectionMetadata.DomainIntentFamilyAd,
                ToolSelectionMetadata.DomainIntentFamilyPublic
            },
            rememberedAdTarget: "corp.contoso.com",
            rememberedPublicTarget: "contoso.com",
            out var toolName,
            out var argumentsJson);

        Assert.True(resolved);
        Assert.Equal("ad_scope_discovery", toolName);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", argumentsJson);
        Assert.DoesNotContain("\"domain_name\":", argumentsJson);
    }

    [Fact]
    public void TryBuildSyntheticPublicDomainOperationalReplayForTesting_BuildsDnsQueriesFromTechnicalTokens() {
        var resolved = Program.TryBuildSyntheticPublicDomainOperationalReplayForTesting(
            "Collect A, AAAA, MX, SPF, and DMARC evidence for contoso.com using 1.1.1.1 and 8.8.8.8.",
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyPublic,
            rememberedPublicTarget: "contoso.com",
            out var calls);

        Assert.True(resolved);
        Assert.Equal(5, calls.Count);
        Assert.All(calls, static call => Assert.Equal("dnsclientx_query", call.Name));
        Assert.Contains(calls, static call => call.Input!.Contains("\"type\":\"A\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"type\":\"AAAA\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"type\":\"MX\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"name\":\"_dmarc.contoso.com\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"endpoint\":\"Cloudflare\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"endpoint\":\"Google\""));
    }

    [Fact]
    public void TryBuildSyntheticPublicDomainOperationalReplayForTesting_AddsNsAndDomainSummaryForDkimAsks() {
        var resolved = Program.TryBuildSyntheticPublicDomainOperationalReplayForTesting(
            "Run NS, MX, SPF, DKIM, and DMARC checks and highlight concrete risk gaps.",
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyPublic,
            rememberedPublicTarget: "contoso.com",
            out var calls);

        Assert.True(resolved);
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"type\":\"NS\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"type\":\"MX\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"_dmarc.contoso.com\""));
        Assert.Contains(calls, static call => call.Name == "domaindetective_domain_summary" && call.Input!.Contains("\"domain\":\"contoso.com\""));
    }

    [Fact]
    public void TryBuildSyntheticPublicProbeReplayForTesting_BuildsPingAndProbeCalls() {
        var resolved = Program.TryBuildSyntheticPublicProbeReplayForTesting(
            userRequest: string.Empty,
            BuildMixedDomainBootstrapToolDefinitions(),
            rememberedPublicTarget: "contoso.com",
            rememberedPublicHosts: null,
            out var calls);

        Assert.True(resolved);
        Assert.Equal(2, calls.Count);
        Assert.Contains(calls, static call => call.Name == "dnsclientx_ping" && call.Input!.Contains("\"target\":\"contoso.com\""));
        Assert.Contains(calls, static call => call.Name == "domaindetective_network_probe" && call.Input!.Contains("\"host\":\"contoso.com\""));
    }

    [Fact]
    public void TryBuildSyntheticPublicProbeReplayForTesting_ExpandsAcrossAllRememberedHostsAndAddsResolverChecks() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 2
require_any_tools:
  - domaindetective_network_probe
  - dnsclientx_ping
  - dnsclientx_query
User request:
Continue network and resolver checks for all remaining discovered hosts in this turn.
""";

        var resolved = Program.TryBuildSyntheticPublicProbeReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            rememberedPublicTarget: "contoso.com",
            rememberedPublicHosts: new[] {
                "contoso.com",
                "_dmarc.contoso.com",
                "10 contoso-com.mail.protection.outlook.com.",
                "ns1-205.azure-dns.com.",
                "ns2-205.azure-dns.net.",
                "ns3-205.azure-dns.org.",
                "ns4-205.azure-dns.info."
            },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(17, calls.Count);
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"contoso.com\"") && call.Input.Contains("\"type\":\"NS\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"contoso.com\"") && call.Input.Contains("\"type\":\"SOA\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"contoso-com.mail.protection.outlook.com\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"ns1-205.azure-dns.com\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"ns2-205.azure-dns.net\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"ns3-205.azure-dns.org\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"ns4-205.azure-dns.info\""));
        Assert.Contains(calls, static call => call.Name == "dnsclientx_ping" && call.Input!.Contains("\"target\":\"contoso-com.mail.protection.outlook.com\""));
        Assert.Contains(calls, static call => call.Name == "domaindetective_network_probe" && call.Input!.Contains("\"host\":\"ns1-205.azure-dns.com\""));
        Assert.DoesNotContain(calls, static call => call.Input!.Contains("_dmarc.contoso.com"));
        Assert.DoesNotContain(calls, static call => call.Name == "dnsclientx_query" && call.Input!.Contains("\"name\":\"contoso.com\"") && call.Input.Contains("\"type\":\"A\""));
    }

    [Fact]
    public void TryBuildSyntheticAdEventReplayForTesting_BuildsCrossDcEventCalls() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 2
min_distinct_tool_input_values:
  machine_name: 2
require_any_tools:
  - eventlog_*query*
  - eventlog_*events*
  - eventlog_*stats*
User request:
Collect reboot and availability evidence across at least two discovered DCs in this turn.
""";

        var resolved = Program.TryBuildSyntheticAdEventReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] { "dc02.ad.evotec.xyz", "dc01.ad.evotec.xyz", "dc02.ad.evotec.xyz" },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, static call => Assert.Equal("eventlog_top_events", call.Name));
        Assert.Contains(calls, static call => call.Input!.Contains("\"machine_name\":\"dc02.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"machine_name\":\"dc01.ad.evotec.xyz\""));
        Assert.All(calls, static call => Assert.Contains("\"log_name\":\"System\"", call.Input!));
    }

    [Fact]
    public void TryBuildSyntheticAdEventReplayForTesting_UsesAllRememberedHostsForRemainingDcContinuation() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 2
require_any_tools:
  - eventlog_*query*
  - eventlog_*events*
  - eventlog_*stats*
User request:
Continue EventLog evidence across all remaining DCs for this AD scope.
""";

        var resolved = Program.TryBuildSyntheticAdEventReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] { "dc01.ad.evotec.xyz", "dc02.ad.evotec.xyz", "dc03.ad.evotec.xyz", "dc04.ad.evotec.xyz" },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(4, calls.Count);
        Assert.All(calls, static call => Assert.Equal("eventlog_top_events", call.Name));
        Assert.Contains(calls, static call => call.Input!.Contains("\"machine_name\":\"dc01.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"machine_name\":\"dc02.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"machine_name\":\"dc03.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"machine_name\":\"dc04.ad.evotec.xyz\""));
    }

    [Fact]
    public void TryBuildSyntheticAdEventPlatformFallbackReplayForTesting_PivotsToSystemEvidenceForPlatformBlockedHosts() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 2
require_any_tools:
  - eventlog_*query*
  - eventlog_*events*
  - eventlog_*stats*
User request:
Collect reboot and availability evidence across at least two discovered DCs in this turn.
""";

        var resolved = Program.TryBuildSyntheticAdEventPlatformFallbackReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] {
                BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"dc02.ad.evotec.xyz","log_name":"System","max_events":10}"""),
                BuildToolCall("call_2", "eventlog_top_events", """{"machine_name":"dc01.ad.evotec.xyz","log_name":"System","max_events":10}""")
            },
            new[] {
                new ToolOutput("call_1", """{"ok":false,"error_code":"platform_not_supported","error":"Remote event log query failed for log 'System' on machine 'dc02.ad.evotec.xyz'. Reason: EventLog access is not supported on this platform."}"""),
                new ToolOutput("call_2", """{"ok":false,"failure":{"code":"platform_not_supported","message":"Remote event log query failed for log 'System' on machine 'dc01.ad.evotec.xyz'. Reason: EventLog access is not supported on this platform."}}""")
            },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(4, calls.Count);
        Assert.Contains(calls, static call => call.Name == "system_connectivity_probe" && call.Input!.Contains("\"computer_name\":\"dc02.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Name == "system_connectivity_probe" && call.Input!.Contains("\"include_time_sync\":true"));
        Assert.Contains(calls, static call => call.Name == "system_windows_update_telemetry" && call.Input!.Contains("\"computer_name\":\"dc01.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Name == "system_windows_update_telemetry" && call.Input!.Contains("\"include_event_telemetry\":false"));
    }

    [Fact]
    public void TryBuildSyntheticAdEventPlatformFallbackReplayForTesting_UsesAllRemainingBlockedHostsWhenRequested() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 2
require_any_tools:
  - eventlog_*query*
  - eventlog_*events*
  - eventlog_*stats*
User request:
Continue EventLog evidence across all remaining DCs for this AD scope.
""";

        var resolved = Program.TryBuildSyntheticAdEventPlatformFallbackReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] {
                BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"dc01.ad.evotec.xyz","log_name":"System","max_events":10}"""),
                BuildToolCall("call_2", "eventlog_top_events", """{"machine_name":"dc02.ad.evotec.xyz","log_name":"System","max_events":10}"""),
                BuildToolCall("call_3", "eventlog_top_events", """{"machine_name":"dc03.ad.evotec.xyz","log_name":"System","max_events":10}""")
            },
            new[] {
                new ToolOutput("call_1", """{"ok":false,"error_code":"platform_not_supported","error":"EventLog access is not supported on this platform."}"""),
                new ToolOutput("call_2", """{"ok":false,"error_code":"platform_not_supported","error":"EventLog access is not supported on this platform."}"""),
                new ToolOutput("call_3", """{"ok":false,"error_code":"platform_not_supported","error":"EventLog access is not supported on this platform."}""")
            },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(6, calls.Count);
        Assert.Contains(calls, static call => call.Input!.Contains("\"computer_name\":\"dc01.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"computer_name\":\"dc02.ad.evotec.xyz\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"computer_name\":\"dc03.ad.evotec.xyz\""));
    }

    [Fact]
    public void TryBuildSyntheticAdEventPlatformFallbackReplayForTesting_DoesNotTriggerWithoutPlatformBlockedOutputs() {
        var resolved = Program.TryBuildSyntheticAdEventPlatformFallbackReplayForTesting(
            "Collect reboot and availability evidence across at least two discovered DCs in this turn.",
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] {
                BuildToolCall("call_1", "eventlog_top_events", """{"machine_name":"dc01.ad.evotec.xyz","log_name":"System","max_events":10}""")
            },
            new[] {
                new ToolOutput("call_1", """{"ok":false,"error_code":"timeout","error":"The operation timed out."}""")
            },
            out var calls);

        Assert.False(resolved);
        Assert.Empty(calls);
    }

    [Fact]
    public void TryBuildSyntheticAdMonitoringReplayForTesting_BuildsLdapAndAdwsProbeCalls() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 1
require_any_tools:
  - ad_*ldap*
  - ad_*adws*
  - ad_monitoring_probe_run
User request:
Validate LDAP and ADWS health for the same AD scope.
""";

        var resolved = Program.TryBuildSyntheticAdMonitoringReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] { "dc02.ad.evotec.xyz", "dc01.ad.evotec.xyz", "dc02.ad.evotec.xyz" },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, static call => Assert.Equal("ad_monitoring_probe_run", call.Name));
        Assert.Contains(calls, static call => call.Input!.Contains("\"probe_kind\":\"ldap\""));
        Assert.Contains(calls, static call => call.Input!.Contains("\"probe_kind\":\"adws\""));
        Assert.All(calls, static call => Assert.Contains("\"targets\":[\"dc02.ad.evotec.xyz\",\"dc01.ad.evotec.xyz\"]", call.Input!));
    }

    [Fact]
    public void TryBuildSyntheticAdReplicationReplayForTesting_BuildsSplitReplicationProbeCalls() {
        const string prompt = """
[Scenario execution contract]
ix:scenario-execution:v1
min_tool_calls: 2
require_any_tools:
  - ad_*replication*
  - ad_monitoring_probe_run
User request:
Continue replication checks on all remaining discovered DCs in this turn.
""";

        var resolved = Program.TryBuildSyntheticAdReplicationReplayForTesting(
            prompt,
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] { "dc01.ad.evotec.xyz", "dc02.ad.evotec.xyz", "dc03.ad.evotec.xyz", "dc04.ad.evotec.xyz" },
            out var calls);

        Assert.True(resolved);
        Assert.Equal(2, calls.Count);
        Assert.All(calls, static call => Assert.Equal("ad_monitoring_probe_run", call.Name));
        Assert.All(calls, static call => Assert.Contains("\"probe_kind\":\"replication\"", call.Input!));
        Assert.Contains(calls, static call => call.Input!.Contains("\"targets\":[\"dc01.ad.evotec.xyz\",\"dc03.ad.evotec.xyz\"]"));
        Assert.Contains(calls, static call => call.Input!.Contains("\"targets\":[\"dc02.ad.evotec.xyz\",\"dc04.ad.evotec.xyz\"]"));
    }

    [Fact]
    public void TryBuildSyntheticAdMonitoringReplayForTesting_DoesNotRunForMixedAsk() {
        var resolved = Program.TryBuildSyntheticAdMonitoringReplayForTesting(
            "Please do AD LDAP + DNS MX together now.",
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] { "dc01.ad.evotec.xyz", "dc02.ad.evotec.xyz" },
            out var calls);

        Assert.False(resolved);
        Assert.Empty(calls);
    }

    [Fact]
    public void TryBuildSyntheticAdReplicationReplayForTesting_DoesNotRunForMixedAsk() {
        var resolved = Program.TryBuildSyntheticAdReplicationReplayForTesting(
            "Please do AD replication + DNS MX together now.",
            BuildMixedDomainBootstrapToolDefinitions(),
            ToolSelectionMetadata.DomainIntentFamilyAd,
            new[] { "dc01.ad.evotec.xyz", "dc02.ad.evotec.xyz" },
            out var calls);

        Assert.False(resolved);
        Assert.Empty(calls);
    }

    private static IReadOnlyList<ToolDefinition> BuildMixedDomainToolDefinitions() {
        return new List<ToolDefinition> {
            new("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("eventlog_live_query", "EventLog", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_domain_summary", "Domain summary", tags: new[] { "domain_family:public_domain" })
        };
    }

    private static IReadOnlyList<ToolDefinition> BuildMixedDomainBootstrapToolDefinitions() {
        return new List<ToolDefinition> {
            new("ad_scope_discovery", "AD scope", tags: new[] { "domain_family:ad_domain" }),
            new("ad_environment_discover", "AD environment", tags: new[] { "domain_family:ad_domain" }),
            new("ad_monitoring_probe_run", "AD monitoring probe", tags: new[] { "domain_family:ad_domain" }),
            new("eventlog_top_events", "EventLog top events", tags: new[] { "domain_family:ad_domain" }),
            new("system_connectivity_probe", "System connectivity probe", tags: new[] { "domain_family:ad_domain" }),
            new("system_windows_update_telemetry", "System Windows Update telemetry", tags: new[] { "domain_family:ad_domain" }),
            new("dnsclientx_query", "DNS query", tags: new[] { "domain_family:public_domain" }),
            new("dnsclientx_ping", "DNS ping", tags: new[] { "domain_family:public_domain" }),
            new("dnsclientx_pack_info", "DNS pack info", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_domain_summary", "Domain summary", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_network_probe", "Network probe", tags: new[] { "domain_family:public_domain" }),
            new("domaindetective_pack_info", "Domain pack info", tags: new[] { "domain_family:public_domain" })
        };
    }

    private static ToolCall BuildToolCall(string callId, string name, string jsonArgs) {
        var args = JsonLite.Parse(jsonArgs)?.AsObject();
        var raw = new JsonObject()
            .Add("type", "custom_tool_call")
            .Add("call_id", callId)
            .Add("name", name)
            .Add("input", jsonArgs);
        return new ToolCall(callId, name, jsonArgs, args, raw);
    }
}
