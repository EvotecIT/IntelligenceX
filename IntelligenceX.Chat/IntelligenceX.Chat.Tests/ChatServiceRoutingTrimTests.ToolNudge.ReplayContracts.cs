using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildWhenNoPartialScopeSignal() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_environment_discover"] = "active_directory";
        packMap["ad_scope_discovery"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", schema),
            new("ad_scope_discovery", "scope", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-2", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-2",
                Output = """{"ok":true,"domain_controllers":["AD0","AD1","AD2"]}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "run discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_RecognizesAdPackIdAliasWhenRegisteredAsAd() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_environment_discover"] = "ad";
        packMap["ad_scope_discovery"] = "ad";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", schema),
            new("ad_scope_discovery", "scope", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ad", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ad",
                Output = """{"ok":true,"discovery_status":{"limited_discovery":true,"domain_name":"contoso.local"}}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue AD discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCustomAdFallbackUsingSchemaAwareDomainHint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_environment_discover"] = "active_directory";
        packMap["ad_domain_inventory_custom"] = "active_directory";

        var sourceSchema = ToolSchema.Object().NoAdditionalProperties();
        var targetSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")))
            .Required("domain_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", sourceSchema),
            new("ad_domain_inventory_custom", "custom ad inventory", targetSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ad", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ad",
                Output = """{"ok":true,"discovery_status":{"limited_discovery":true,"domain_name":"contoso.local"}}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_domain_inventory_custom"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue AD discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("ad_domain_inventory_custom", toolCall.Name);
        Assert.Contains("\"domain_name\":\"contoso.local\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCustomAdFallbackUsingSchemaAwareDefaults() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_environment_discover"] = "active_directory";
        packMap["ad_domain_inventory_custom"] = "active_directory";

        var sourceSchema = ToolSchema.Object().NoAdditionalProperties();
        var targetSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")),
                ("max_results", ToolSchema.Integer("max results")))
            .Required("domain_name", "max_results")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", sourceSchema),
            new("ad_domain_inventory_custom", "custom ad inventory", targetSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ad-defaults", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ad-defaults",
                Output = """{"ok":true,"discovery_status":{"limited_discovery":true,"domain_name":"contoso.local"}}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_domain_inventory_custom"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue AD discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("ad_domain_inventory_custom", toolCall.Name);
        Assert.Contains("\"domain_name\":\"contoso.local\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"max_results\":500", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsDynamicPackInfoFallbackForNonHardcodedPackId() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["customx_scan"] = "customx";
        packMap["customx_pack_info"] = "customx";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("customx_scan", "custom pack scan", schema),
            new("customx_pack_info", "custom pack info", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-custom", Name = "customx_scan" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-custom",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["customx_pack_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue custom diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("customx_pack_info", toolCall.Name);
        Assert.Contains("pack_contract_failure_autofallback", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppendPackFallbackTelemetryMarker_EncodesFieldValuesForMachineParsing() {
        var method = typeof(ChatServiceSession).GetMethod("AppendPackFallbackTelemetryMarker", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(
            null,
            new object?[] {
                "pack_contract_failure_autofallback:customx:query_failed->custom tool",
                "active directory",
                "custom tool/diag"
            });

        var reason = Assert.IsType<string>(result);
        Assert.Contains("ix:pack-fallback:v1", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_pack=activedirectory", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_tool=custom%20tool%2Fdiag", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("family=pack_contract", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("pack_contract_cross_public_posture_testimox:domaindetective:query_failed->testimox_rules_list", "public_domain")]
    [InlineData("pack_contract_cross_host_eventlog_evidence:computerx:query_failed->eventlog_live_query", "host")]
    [InlineData("pack_contract_cross_system_ad_discovery:system:query_failed->ad_scope_discovery", "ad_domain")]
    [InlineData("pack_contract_failure_autofallback:customx:query_failed->customx_pack_info", "pack_contract")]
    [InlineData("pack_contract_unclassified_path:customx:query_failed->customx_pack_info", "general")]
    public void ResolvePackFallbackTelemetryFamily_UsesStableReasonTokens(string reason, string expectedFamily) {
        var method = typeof(ChatServiceSession).GetMethod("ResolvePackFallbackTelemetryFamily", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var result = method!.Invoke(null, new object?[] { reason });
        Assert.Equal(expectedFamily, Assert.IsType<string>(result), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsDomainDetectiveFallbackFromSourceArguments() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_domain_summary"] = "domaindetective";
        packMap["domaindetective_network_probe"] = "domaindetective";

        var domainSummarySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var networkProbeSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_domain_summary", "domain summary", domainSummarySchema),
            new("domaindetective_network_probe", "network probe", networkProbeSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd",
                Name = "domaindetective_domain_summary",
                ArgumentsJson = """{"domain":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_network_probe"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("domaindetective_network_probe", toolCall.Name);
        Assert.Contains("\"host\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDnsClientXFallbackFromDomainDetectiveFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_domain_summary"] = "domaindetective";
        packMap["domaindetective_network_probe"] = "domaindetective";
        packMap["dnsclientx_query"] = "dnsclientx";

        var domainSummarySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var networkProbeSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var dnsQuerySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_domain_summary", "domain summary", domainSummarySchema),
            new("domaindetective_network_probe", "network probe", networkProbeSchema),
            new("dnsclientx_query", "dns query", dnsQuerySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd",
                Name = "domaindetective_domain_summary",
                ArgumentsJson = """{"domain":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_network_probe"] = false,
            ["dnsclientx_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("dnsclientx_query", toolCall.Name);
        Assert.Contains("\"name\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_public_dns", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDnsClientXFallbackFromSecondCandidateWhenTopCandidateMissingRequiredArgs() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_domain_summary"] = "domaindetective";
        packMap["dnsclientx_a_query_requires_ticket"] = "dnsclientx";
        packMap["dnsclientx_b_query"] = "dnsclientx";

        var sourceSchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var dnsPrimarySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")),
                ("ticket_id", ToolSchema.String("ticket id")))
            .Required("ticket_id")
            .NoAdditionalProperties();
        var dnsSecondarySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_domain_summary", "domain summary", sourceSchema),
            new("dnsclientx_a_query_requires_ticket", "dns query primary", dnsPrimarySchema),
            new("dnsclientx_b_query", "dns query secondary", dnsSecondarySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd-fallback-loop",
                Name = "domaindetective_domain_summary",
                ArgumentsJson = """{"domain":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd-fallback-loop",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_a_query_requires_ticket"] = false,
            ["dnsclientx_b_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("dnsclientx_b_query", toolCall.Name);
        Assert.Contains("\"name\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDnsClientXQueryFallbackFromMetadataEligibleDomainDetectiveSource() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_dns_query"] = "domaindetective";
        packMap["dnsclientx_query"] = "dnsclientx";

        var domainQuerySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var dnsQuerySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_dns_query", "domain query", domainQuerySchema),
            new("dnsclientx_query", "dns query", dnsQuerySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd-meta-domain",
                Name = "domaindetective_dns_query",
                ArgumentsJson = """{"domain":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd-meta-domain",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("dnsclientx_query", toolCall.Name);
        Assert.Contains("\"name\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDnsClientXPingFallbackFromMetadataEligibleDomainDetectiveSource() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_reachability_probe"] = "domaindetective";
        packMap["dnsclientx_ping"] = "dnsclientx";

        var sourceSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var dnsPingSchema = ToolSchema.Object(
                ("target", ToolSchema.String("target")))
            .Required("target")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_reachability_probe", "reachability probe", sourceSchema),
            new("dnsclientx_ping", "dns ping", dnsPingSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd-meta-host",
                Name = "domaindetective_reachability_probe",
                ArgumentsJson = """{"host":"mail.contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd-meta-host",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_ping"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("dnsclientx_ping", toolCall.Name);
        Assert.Contains("\"target\":\"mail.contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDnsClientXFallbackWithCustomCandidateName() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_reachability_probe"] = "domaindetective";
        packMap["dnsclientx_host_probe_custom"] = "dnsclientx";

        var sourceSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var targetSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_reachability_probe", "reachability probe", sourceSchema),
            new("dnsclientx_host_probe_custom", "custom dns host probe", targetSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd-custom-target",
                Name = "domaindetective_reachability_probe",
                ArgumentsJson = """{"host":"mail.contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd-custom-target",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_host_probe_custom"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("dnsclientx_host_probe_custom", toolCall.Name);
        Assert.Contains("\"host\":\"mail.contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildCrossPackDnsClientXFallbackFromDomainDetectivePackInfoFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_custom_pack_info"] = "domaindetective";
        packMap["dnsclientx_query"] = "dnsclientx";
        packMap["dnsclientx_ping"] = "dnsclientx";

        var sourceSchema = ToolSchema.Object().NoAdditionalProperties();
        var dnsQuerySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var dnsPingSchema = ToolSchema.Object(
                ("target", ToolSchema.String("target")))
            .Required("target")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_custom_pack_info", "custom pack info", sourceSchema),
            new("dnsclientx_query", "dns query", dnsQuerySchema),
            new("dnsclientx_ping", "dns ping", dnsPingSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd-guide",
                Name = "domaindetective_custom_pack_info",
                ArgumentsJson = """{"domain":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd-guide",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_query"] = false,
            ["dnsclientx_ping"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsDnsClientXQueryFallbackFromPingSourceArguments() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_ping"] = "dnsclientx";
        packMap["dnsclientx_query"] = "dnsclientx";

        var pingSchema = ToolSchema.Object(
                ("target", ToolSchema.String("target")))
            .NoAdditionalProperties();
        var querySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_ping", "dns ping", pingSchema),
            new("dnsclientx_query", "dns query", querySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dns",
                Name = "dnsclientx_ping",
                ArgumentsJson = """{"target":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dns",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue dns checks", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("dnsclientx_query", toolCall.Name);
        Assert.Contains("\"name\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDomainDetectiveFallbackFromDnsClientXFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_query"] = "dnsclientx";
        packMap["domaindetective_domain_summary"] = "domaindetective";

        var dnsQuerySchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var domainSummarySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_query", "dns query", dnsQuerySchema),
            new("domaindetective_domain_summary", "domain summary", domainSummarySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dnsq",
                Name = "dnsclientx_query",
                ArgumentsJson = """{"name":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dnsq",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_domain_summary"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue dns investigation", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("domaindetective_domain_summary", toolCall.Name);
        Assert.Contains("\"domain\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_public_domain", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDomainDetectiveFallbackFromSecondCandidateWhenTopCandidateMissingRequiredArgs() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_query"] = "dnsclientx";
        packMap["domaindetective_a_domain_summary_requires_ticket"] = "domaindetective";
        packMap["domaindetective_b_domain_summary"] = "domaindetective";

        var sourceSchema = ToolSchema.Object(
                ("name", ToolSchema.String("name")))
            .Required("name")
            .NoAdditionalProperties();
        var primarySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")),
                ("ticket_id", ToolSchema.String("ticket id")))
            .Required("ticket_id")
            .NoAdditionalProperties();
        var secondarySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_query", "dns query", sourceSchema),
            new("domaindetective_a_domain_summary_requires_ticket", "domain summary primary", primarySchema),
            new("domaindetective_b_domain_summary", "domain summary secondary", secondarySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dns-fallback-loop",
                Name = "dnsclientx_query",
                ArgumentsJson = """{"name":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dns-fallback-loop",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_a_domain_summary_requires_ticket"] = false,
            ["domaindetective_b_domain_summary"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("domaindetective_b_domain_summary", toolCall.Name);
        Assert.Contains("\"domain\":\"contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDnsClientXPingFallbackFromDomainDetectiveProbeFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["domaindetective_network_probe"] = "domaindetective";
        packMap["dnsclientx_ping"] = "dnsclientx";

        var networkProbeSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var dnsPingSchema = ToolSchema.Object(
                ("target", ToolSchema.String("target")))
            .Required("target")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("domaindetective_network_probe", "network probe", networkProbeSchema),
            new("dnsclientx_ping", "dns ping", dnsPingSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dd-probe",
                Name = "domaindetective_network_probe",
                ArgumentsJson = """{"host":"mail.contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dd-probe",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_ping"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue probe investigation", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("dnsclientx_ping", toolCall.Name);
        Assert.Contains("\"target\":\"mail.contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_public_dns", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDomainDetectiveProbeFallbackFromDnsClientXPingFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_ping"] = "dnsclientx";
        packMap["domaindetective_network_probe"] = "domaindetective";

        var dnsPingSchema = ToolSchema.Object(
                ("target", ToolSchema.String("target")))
            .Required("target")
            .NoAdditionalProperties();
        var networkProbeSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_ping", "dns ping", dnsPingSchema),
            new("domaindetective_network_probe", "network probe", networkProbeSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dns-ping",
                Name = "dnsclientx_ping",
                ArgumentsJson = """{"target":"mail.contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dns-ping",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_network_probe"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue dns investigation", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("domaindetective_network_probe", toolCall.Name);
        Assert.Contains("\"host\":\"mail.contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_public_domain", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDomainDetectiveProbeFallbackFromMetadataEligibleDnsClientXSource() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_reachability_probe"] = "dnsclientx";
        packMap["domaindetective_network_probe"] = "domaindetective";

        var sourceSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var networkProbeSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_reachability_probe", "reachability probe", sourceSchema),
            new("domaindetective_network_probe", "network probe", networkProbeSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dns-meta-host",
                Name = "dnsclientx_reachability_probe",
                ArgumentsJson = """{"host":"mail.contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dns-meta-host",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_network_probe"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue dns investigation", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("domaindetective_network_probe", toolCall.Name);
        Assert.Contains("\"host\":\"mail.contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackDomainDetectiveFallbackWithCustomCandidateName() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_reachability_probe"] = "dnsclientx";
        packMap["domaindetective_host_probe_custom"] = "domaindetective";

        var sourceSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var targetSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_reachability_probe", "dns reachability probe", sourceSchema),
            new("domaindetective_host_probe_custom", "custom domain detective host probe", targetSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dns-custom-target",
                Name = "dnsclientx_reachability_probe",
                ArgumentsJson = """{"host":"mail.contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dns-custom-target",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_host_probe_custom"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue dns investigation", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("domaindetective_host_probe_custom", toolCall.Name);
        Assert.Contains("\"host\":\"mail.contoso.com\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildCrossPackDomainDetectiveFallbackFromDnsClientXPackInfoFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["dnsclientx_custom_pack_info"] = "dnsclientx";
        packMap["domaindetective_domain_summary"] = "domaindetective";
        packMap["domaindetective_network_probe"] = "domaindetective";

        var sourceSchema = ToolSchema.Object().NoAdditionalProperties();
        var domainSummarySchema = ToolSchema.Object(
                ("domain", ToolSchema.String("domain")))
            .Required("domain")
            .NoAdditionalProperties();
        var networkProbeSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("dnsclientx_custom_pack_info", "custom pack info", sourceSchema),
            new("domaindetective_domain_summary", "domain summary", domainSummarySchema),
            new("domaindetective_network_probe", "network probe", networkProbeSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-dns-guide",
                Name = "dnsclientx_custom_pack_info",
                ArgumentsJson = """{"name":"contoso.com"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-dns-guide",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_domain_summary"] = false,
            ["domaindetective_network_probe"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue dns investigation", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsTestimoXRunFallbackUsingSelectorHints() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["testimox_rules_list"] = "testimox";
        packMap["testimox_rules_run"] = "testimox";

        var listSchema = ToolSchema.Object(
                ("search_text", ToolSchema.String("selector")))
            .NoAdditionalProperties();
        var runSchema = ToolSchema.Object(
                ("search_text", ToolSchema.String("selector")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("testimox_rules_list", "rules list", listSchema),
            new("testimox_rules_run", "rules run", runSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-tx",
                Name = "testimox_rules_list",
                ArgumentsJson = """{"search_text":"kerberos"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-tx",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_rules_run"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue with testimo checks", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("testimox_rules_run", toolCall.Name);
        Assert.Contains("\"search_text\":\"kerberos\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsComputerXPackInfoFallbackWhenSourceFailsWithoutHints() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_info"] = "computerx";
        packMap["system_pack_info"] = "computerx";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_info", "system info", schema),
            new("system_pack_info", "system pack info", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-sys", Name = "system_info" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_pack_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("system_pack_info", toolCall.Name);
        Assert.Contains("pack_contract_failure_autofallback", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildHintlessPackInfoFallbackWhenSourceDidNotFail() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_info"] = "computerx";
        packMap["system_pack_info"] = "computerx";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_info", "system info", schema),
            new("system_pack_info", "system pack info", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-sys", Name = "system_info" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys",
                Output = """{"ok":true,"host":"srv01"}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_pack_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidenceFromSystemFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_info"] = "computerx";
        packMap["eventlog_live_query"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("log_name", ToolSchema.String("log name")),
                ("max_events", ToolSchema.Integer("max events")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_info", "system info", systemSchema),
            new("eventlog_live_query", "eventlog live query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-host",
                Name = "system_info",
                ArgumentsJson = """{"computer_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-host",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_host_eventlog_evidence", reason, StringComparison.OrdinalIgnoreCase);
        AssertPackFallbackTelemetry(reason, "computerx", "eventlog_live_query", "host");
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidenceFromSecondCandidateWhenTopCandidateMissingRequiredArgs() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_info"] = "computerx";
        packMap["eventlog_a_live_query_requires_ticket"] = "eventlog";
        packMap["eventlog_b_live_query"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .NoAdditionalProperties();
        var eventlogPrimarySchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("ticket_id", ToolSchema.String("ticket id")))
            .Required("ticket_id")
            .NoAdditionalProperties();
        var eventlogSecondarySchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .Required("machine_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_info", "system info", systemSchema),
            new("eventlog_a_live_query_requires_ticket", "eventlog query primary", eventlogPrimarySchema),
            new("eventlog_b_live_query", "eventlog query secondary", eventlogSecondarySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-eventlog-loop",
                Name = "system_info",
                ArgumentsJson = """{"computer_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-eventlog-loop",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_a_live_query_requires_ticket"] = false,
            ["eventlog_b_live_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_b_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidenceFromMetadataEligibleSystemSource() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_inventory_query"] = "computerx";
        packMap["eventlog_live_query"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("log_name", ToolSchema.String("log name")),
                ("max_events", ToolSchema.Integer("max events")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_inventory_query", "system inventory query", systemSchema),
            new("eventlog_live_query", "eventlog live query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-meta",
                Name = "system_inventory_query",
                ArgumentsJson = """{"computer_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-meta",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_host_eventlog_evidence", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidenceWithCustomCandidateName() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_inventory_query"] = "computerx";
        packMap["eventlog_host_query_custom"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("host", ToolSchema.String("host")),
                ("log_name", ToolSchema.String("log name")))
            .Required("host")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_inventory_query", "system inventory query", systemSchema),
            new("eventlog_host_query_custom", "eventlog host query custom", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-custom",
                Name = "system_inventory_query",
                ArgumentsJson = """{"computer_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-custom",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_host_query_custom"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_host_query_custom", toolCall.Name);
        Assert.Contains("\"host\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidenceWithMachineNamesArrayCandidate() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_inventory_query"] = "computerx";
        packMap["eventlog_custom_query"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_names", ToolSchema.Array(ToolSchema.String(), "machine names")),
                ("log_name", ToolSchema.String("log name")))
            .Required("machine_names")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_inventory_query", "system inventory query", systemSchema),
            new("eventlog_custom_query", "eventlog custom query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-machine-names",
                Name = "system_inventory_query",
                ArgumentsJson = """{"computer_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-machine-names",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_custom_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_custom_query", toolCall.Name);
        Assert.Contains("\"machine_names\":[\"AD0\"]", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidenceFromMachineNamesHintArray() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_inventory_query"] = "computerx";
        packMap["eventlog_live_query"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("machine_names", ToolSchema.Array(ToolSchema.String(), "machine names")))
            .NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("log_name", ToolSchema.String("log name")))
            .Required("machine_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_inventory_query", "system inventory query", systemSchema),
            new("eventlog_live_query", "eventlog live query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-machine-array",
                Name = "system_inventory_query",
                ArgumentsJson = """{"machine_names":["AD0","AD1"]}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-machine-array",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostEventlogEvidencePreservingTargetsHintArray() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_inventory_query"] = "computerx";
        packMap["eventlog_targets_query"] = "eventlog";

        var systemSchema = ToolSchema.Object(
                ("targets", ToolSchema.Array(ToolSchema.String(), "targets")))
            .NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("targets", ToolSchema.Array(ToolSchema.String(), "targets")),
                ("log_name", ToolSchema.String("log name")))
            .Required("targets")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_inventory_query", "system inventory query", systemSchema),
            new("eventlog_targets_query", "eventlog targets query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-target-array",
                Name = "system_inventory_query",
                ArgumentsJson = """{"targets":["AD0","AD1"]}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-target-array",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_targets_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_targets_query", toolCall.Name);
        Assert.Contains("\"targets\":[\"AD0\",\"AD1\"]", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildCrossHostEventlogEvidenceFromSystemPackInfoFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_custom_pack_info"] = "computerx";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_custom_pack_info", "system custom pack info", schema),
            new("eventlog_live_query", "eventlog live query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-guide",
                Name = "system_custom_pack_info",
                ArgumentsJson = """{"computer_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-guide",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildCrossHostEventlogEvidenceWithoutHostHint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_info"] = "computerx";
        packMap["eventlog_live_query"] = "eventlog";

        var systemSchema = ToolSchema.Object().NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_info", "system info", systemSchema),
            new("eventlog_live_query", "eventlog live query", eventlogSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-nohost",
                Name = "system_info",
                ArgumentsJson = "{}"
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-nohost",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossPackSystemToAdDiscoveryFromStringBooleanHint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["system_bios_summary"] = "system";
        packMap["ad_scope_discovery"] = "active_directory";

        var systemSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")),
                ("include_trusts", ToolSchema.String("include trusts")))
            .Required("domain_name")
            .NoAdditionalProperties();
        var adSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")),
                ("discovery_fallback", ToolSchema.String("fallback mode")),
                ("include_trusts", ToolSchema.Boolean()))
            .Required("domain_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_bios_summary", "system bios summary", systemSchema),
            new("ad_scope_discovery", "ad scope discovery", adSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-sys-ad-bool",
                Name = "system_bios_summary",
                ArgumentsJson = """{"domain_name":"contoso.local","include_trusts":"false"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-sys-ad-bool",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
        Assert.Contains("\"domain_name\":\"contoso.local\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"include_trusts\":false", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_PrefersAdDiscoveryBeforeCrossDcEventlogFanOut() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_stats"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";
        packMap["ad_scope_discovery"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("log_name", ToolSchema.String("log name")))
            .NoAdditionalProperties();
        var adSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_stats", "live stats", schema),
            new("eventlog_live_query", "live query", eventlogSchema),
            new("ad_scope_discovery", "scope", adSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ev", Name = "eventlog_live_stats" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev",
                Output = """
                         {"ok":true,"discovery_status":{"limited_discovery":true,"machine_name":"AD0","domain_name":"ad.evotec.xyz"}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Could you check other domain controllers for the same issue?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
        Assert.Contains("cross_dc_discovery_first", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_PrefersCustomAdDiscoveryCandidateBeforeCrossDcEventlogFanOut() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_stats"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";
        packMap["ad_dc_fabric_discover"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("log_name", ToolSchema.String("log name")))
            .NoAdditionalProperties();
        var adSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_stats", "live stats", schema),
            new("eventlog_live_query", "live query", eventlogSchema),
            new("ad_dc_fabric_discover", "custom scope", adSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ev", Name = "eventlog_live_stats" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev",
                Output = """
                         {"ok":true,"discovery_status":{"limited_discovery":true,"machine_name":"AD0","domain_name":"ad.evotec.xyz"}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["ad_dc_fabric_discover"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Could you check other domain controllers for the same issue?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("ad_dc_fabric_discover", toolCall.Name);
        Assert.Contains("cross_dc_discovery_first", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_PrefersCustomAdDiscoveryRequiringDomainHintBeforeCrossDcEventlogFanOut() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_stats"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";
        packMap["ad_dc_discovery_custom"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var adSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")))
            .Required("domain_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_stats", "live stats", schema),
            new("eventlog_live_query", "live query", schema),
            new("ad_dc_discovery_custom", "custom scope", adSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ev", Name = "eventlog_live_stats" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev",
                Output = """
                         {"ok":true,"discovery_status":{"limited_discovery":true,"machine_name":"AD0","domain_name":"ad.evotec.xyz"}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["ad_dc_discovery_custom"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Could you check other domain controllers for the same issue?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("ad_dc_discovery_custom", toolCall.Name);
        Assert.Contains("\"domain_name\":\"ad.evotec.xyz\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_dc_discovery_first", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_PrefersCustomAdDiscoveryRequiringSchemaDefaultBeforeCrossDcEventlogFanOut() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_stats"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";
        packMap["ad_dc_discovery_with_default_custom"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var adSchema = ToolSchema.Object(
                ("domain_name", ToolSchema.String("domain name")),
                ("discovery_fallback", ToolSchema.String("fallback mode")))
            .Required("domain_name", "discovery_fallback")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_stats", "live stats", schema),
            new("eventlog_live_query", "live query", schema),
            new("ad_dc_discovery_with_default_custom", "custom scope", adSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ev-default", Name = "eventlog_live_stats" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-default",
                Output = """
                         {"ok":true,"discovery_status":{"limited_discovery":true,"machine_name":"AD0","domain_name":"ad.evotec.xyz"}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["ad_dc_discovery_with_default_custom"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Could you check other domain controllers for the same issue?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("ad_dc_discovery_with_default_custom", toolCall.Name);
        Assert.Contains("\"domain_name\":\"ad.evotec.xyz\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_dc_discovery_first", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_SkipsAdPackInfoWhenCrossDcDiscoveryCandidatesAreNotViable() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_stats"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";
        packMap["ad_discovery_requires_token"] = "active_directory";
        packMap["ad_custom_pack_info"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var adSchema = ToolSchema.Object(
                ("tenant_token", ToolSchema.String("tenant token")))
            .Required("tenant_token")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_stats", "live stats", schema),
            new("eventlog_live_query", "live query", schema),
            new("ad_discovery_requires_token", "custom discovery", adSchema),
            new("ad_custom_pack_info", "custom pack info", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ev", Name = "eventlog_live_stats" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev",
                Output = """
                         {"ok":true,"discovery_status":{"limited_discovery":true,"machine_name":"AD0","domain_name":"ad.evotec.xyz"}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["ad_discovery_requires_token"] = false,
            ["ad_custom_pack_info"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Could you check other domain controllers for the same issue?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.DoesNotContain("cross_dc_discovery_first", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_KeepsHostScopedEventlogFallbackWhenRequestIsHostTargeted() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_stats"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";
        packMap["ad_scope_discovery"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_stats", "live stats", schema),
            new("eventlog_live_query", "live query", schema),
            new("ad_scope_discovery", "scope", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ev", Name = "eventlog_live_stats" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev",
                Output = """
                         {"ok":true,"discovery_status":{"limited_discovery":true,"machine_name":"AD0","domain_name":"ad.evotec.xyz"}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Could you check AD0 for the same issue?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostSystemBaselineFromEventlogFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_query"] = "eventlog";
        packMap["system_info"] = "computerx";

        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .Required("computer_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_query", "live query", eventlogSchema),
            new("system_info", "system info", systemSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-ev-host",
                Name = "eventlog_live_query",
                ArgumentsJson = """{"machine_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-host",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("system_info", toolCall.Name);
        Assert.Contains("\"computer_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_host_system_baseline", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostSystemBaselineFromSecondCandidateWhenTopCandidateMissingRequiredArgs() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_query"] = "eventlog";
        packMap["system_a_inventory_requires_ticket"] = "computerx";
        packMap["system_b_info"] = "computerx";

        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var systemPrimarySchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")),
                ("ticket_id", ToolSchema.String("ticket id")))
            .Required("ticket_id")
            .NoAdditionalProperties();
        var systemSecondarySchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .Required("computer_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_query", "live query", eventlogSchema),
            new("system_a_inventory_requires_ticket", "system inventory primary", systemPrimarySchema),
            new("system_b_info", "system info secondary", systemSecondarySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-ev-system-loop",
                Name = "eventlog_live_query",
                ArgumentsJson = """{"machine_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-system-loop",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_a_inventory_requires_ticket"] = false,
            ["system_b_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("system_b_info", toolCall.Name);
        Assert.Contains("\"computer_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostSystemBaselineFromMetadataEligibleEventlogSource() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_named_events_query"] = "eventlog";
        packMap["system_info"] = "computerx";

        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .Required("computer_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_named_events_query", "named events query", eventlogSchema),
            new("system_info", "system info", systemSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-ev-meta",
                Name = "eventlog_named_events_query",
                ArgumentsJson = """{"machine_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-meta",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("system_info", toolCall.Name);
        Assert.Contains("\"computer_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cross_host_system_baseline", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsCrossHostSystemBaselineWithCustomCandidateName() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_named_events_query"] = "eventlog";
        packMap["system_host_inventory_custom"] = "computerx";

        var eventlogSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var systemSchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .Required("machine_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_named_events_query", "named events query", eventlogSchema),
            new("system_host_inventory_custom", "system host inventory custom", systemSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-ev-custom",
                Name = "eventlog_named_events_query",
                ArgumentsJson = """{"machine_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-custom",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_host_inventory_custom"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("system_host_inventory_custom", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildCrossHostSystemBaselineFromEventlogPackInfoFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_custom_pack_info"] = "eventlog";
        packMap["system_info"] = "computerx";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .Required("computer_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_custom_pack_info", "eventlog custom pack info", schema),
            new("system_info", "system info", systemSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-ev-guide",
                Name = "eventlog_custom_pack_info",
                ArgumentsJson = """{"machine_name":"AD0"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-guide",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue host diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildCrossHostSystemBaselineWithoutHostHint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_live_query"] = "eventlog";
        packMap["system_info"] = "computerx";

        var eventlogSchema = ToolSchema.Object().NoAdditionalProperties();
        var systemSchema = ToolSchema.Object(
                ("computer_name", ToolSchema.String("computer name")))
            .Required("computer_name")
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_query", "live query", eventlogSchema),
            new("system_info", "system info", systemSchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-ev-nohost",
                Name = "eventlog_live_query",
                ArgumentsJson = "{}"
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ev-nohost",
                Output = """{"ok":false,"error_code":"query_failed"}""",
                Ok = false,
                ErrorCode = "query_failed"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["system_info"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "continue diagnostics", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsEventlogLiveQueryFallbackWhenEvtxAccessDeniedWithHostHint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_evtx_find"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_evtx_find", "find evtx", schema),
            new("eventlog_live_query", "live query", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-3", Name = "eventlog_evtx_find" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-3",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Can you find out why and when AD0 was rebooted?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("pack_contract_partial_scope_autofallback", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evtx_access_denied_live_query_fallback", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsEventlogLiveQueryFallbackFromMetadataEligibleEvtxSource() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_custom_evtx_query"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var sourceSchema = ToolSchema.Object(
                ("path", ToolSchema.String("Path to the .evtx file.")))
            .Required("path")
            .NoAdditionalProperties();
        var liveQuerySchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")),
                ("log_name", ToolSchema.String("log name")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_custom_evtx_query", "custom evtx query", sourceSchema),
            new("eventlog_live_query", "live query", liveQuerySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-evtx-meta",
                Name = "eventlog_custom_evtx_query",
                ArgumentsJson = """{"path":"C:\\Logs\\System.evtx"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evtx-meta",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Can you check AD0 for reboot evidence?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildEventlogLiveQueryFallbackFromEventlogPackInfoFailure() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_custom_pack_info"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var liveQuerySchema = ToolSchema.Object(
                ("machine_name", ToolSchema.String("machine name")))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_custom_pack_info", "custom pack info", schema),
            new("eventlog_live_query", "live query", liveQuerySchema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-evtx-guide",
                Name = "eventlog_custom_pack_info"
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evtx-guide",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Can you check AD0 for reboot evidence?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_ResolvesHostHintAgainstPriorDiscoveryOutputs() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_scope_discovery"] = "active_directory";
        packMap["eventlog_evtx_find"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema),
            new("eventlog_evtx_find", "find evtx", schema),
            new("eventlog_live_query", "live query", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ad", Name = "ad_scope_discovery" },
            new() { CallId = "call-evtx", Name = "eventlog_evtx_find" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ad",
                Output = """
                         {"ok":true,"domain_controllers":[{"machine_name":"AD0.contoso.local"},{"machine_name":"AD1.contoso.local"}]}
                         """,
                Ok = true
            },
            new() {
                CallId = "call-evtx",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Can you find out why and when AD0 was rebooted?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0.contoso.local\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildEventlogFallbackWithoutHostHint() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_evtx_find"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_evtx_find", "find evtx", schema),
            new("eventlog_live_query", "live query", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-4", Name = "eventlog_evtx_find" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-4",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Please continue with reboot checks.",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void SupportsSyntheticHostReplayItems_EnablesOnlyCompatibleHttpTransport() {
        var nativeResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.Native });
        var appServerResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.AppServer });
        var compatibleResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.CompatibleHttp });
        var copilotCliResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.CopilotCli });

        Assert.False(Assert.IsType<bool>(nativeResult));
        Assert.False(Assert.IsType<bool>(appServerResult));
        Assert.True(Assert.IsType<bool>(compatibleResult));
        Assert.False(Assert.IsType<bool>(copilotCliResult));
    }

    [Fact]
    public void BuildHostReplayReviewInput_UsesTextReplayForNativeSafeMode() {
        var call = new ToolCall(
            callId: "host_next_action_123",
            name: "ad_scope_discovery",
            input: "{\"domain\":\"ad.evotec.xyz\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "ad_scope_discovery"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_next_action_123",
                Output = "{\"ok\":true,\"domain\":\"ad.evotec.xyz\"}",
                Ok = true
            }
        };

        var inputObj = BuildHostReplayReviewInputMethod.Invoke(
            null,
            new object?[] { call, outputs, false });

        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);
        Assert.Equal(1, items.Count);

        var first = Assert.IsType<JsonObject>(items[0].AsObject());
        Assert.Equal("text", first.GetString("type"));
        Assert.Contains("ix:host-replay-review:v1", first.GetString("text"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildHostReplayReviewInput_UsesSyntheticReplayItemsWhenSupported() {
        var call = new ToolCall(
            callId: "host_pack_fallback_123",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_pack_fallback_123",
                Output = "{\"ok\":true}",
                Ok = true
            }
        };

        var inputObj = BuildHostReplayReviewInputMethod.Invoke(
            null,
            new object?[] { call, outputs, true });

        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);
        Assert.Equal(2, items.Count);

        var first = Assert.IsType<JsonObject>(items[0].AsObject());
        var second = Assert.IsType<JsonObject>(items[1].AsObject());
        Assert.Equal("custom_tool_call", first.GetString("type"));
        Assert.Equal("custom_tool_call_output", second.GetString("type"));
    }

    [Fact]
    public void BuildHostReplayReviewInput_FallsBackToTextReplayWhenOutputCallIdsDoNotMatch() {
        var call = new ToolCall(
            callId: "host_pack_fallback_123",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_next_action_mismatch",
                Output = "{\"ok\":true}",
                Ok = true
            }
        };

        var inputObj = BuildHostReplayReviewInputMethod.Invoke(
            null,
            new object?[] { call, outputs, true });

        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);
        Assert.Single(items);

        var first = Assert.IsType<JsonObject>(items[0].AsObject());
        Assert.Equal("text", first.GetString("type"));
        Assert.Contains("ix:host-replay-review:v1", first.GetString("text"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveToolOutputCallId_UsesRawCallIdWhenMatched() {
        var extracted = new List<ToolCall> {
            new(
                callId: "call_123",
                name: "eventlog_live_query",
                input: "{}",
                arguments: null,
                raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"))
        };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_123"] = extracted[0]
        };

        var result = ResolveToolOutputCallIdMethod.Invoke(
            null,
            new object?[] { extracted, byId, "call_123", 0 });

        Assert.Equal("call_123", Assert.IsType<string>(result));
    }

    [Fact]
    public void ResolveToolOutputCallId_FallsBackToIndexedCallWhenRawCallIdIsMismatched() {
        var first = new ToolCall(
            callId: "call_123",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var second = new ToolCall(
            callId: "call_456",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { first, second };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_123"] = first,
            ["call_456"] = second
        };

        var result = ResolveToolOutputCallIdMethod.Invoke(
            null,
            new object?[] { extracted, byId, "mismatch_call_id", 1 });

        Assert.Equal("call_456", Assert.IsType<string>(result));
    }

    [Fact]
    public void ResolveToolOutputCallId_ReturnsEmptyWhenNoSafeFallbackExists() {
        var first = new ToolCall(
            callId: "call_123",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var second = new ToolCall(
            callId: "call_456",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { first, second };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_123"] = first,
            ["call_456"] = second
        };

        var result = ResolveToolOutputCallIdMethod.Invoke(
            null,
            new object?[] { extracted, byId, string.Empty, 99 });

        Assert.Equal(string.Empty, Assert.IsType<string>(result));
    }

    [Fact]
    public void BuildToolRoundReplayInput_DeduplicatesByCallId_AndSkipsOrphanOutputs() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var callB = new ToolCall(
            callId: "call_b",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD1\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA, callB };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = "out-a-first", Ok = true },
            new() { CallId = "call_b", Output = "out-b-first", Ok = true },
            new() { CallId = "call_a", Output = "out-a-duplicate", Ok = true },
            new() { CallId = "orphan_call", Output = "out-orphan", Ok = true },
            new() { CallId = string.Empty, Output = "out-empty-id", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(4, items.Count);

        var toolCalls = 0;
        var toolOutputs = 0;
        string? callAOutput = null;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            var type = item.GetString("type");
            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                toolCalls++;
                continue;
            }

            if (!string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            toolOutputs++;
            var callId = item.GetString("call_id");
            if (string.Equals(callId, "call_a", StringComparison.OrdinalIgnoreCase)) {
                callAOutput = item.GetString("output");
            }
        }

        Assert.Equal(2, toolCalls);
        Assert.Equal(2, toolOutputs);
        Assert.Equal("out-a-duplicate", callAOutput);
    }

    [Fact]
    public void BuildToolRoundReplayInput_DropAfterToolCall_ReplaysOnlyCompletedPair() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var callB = new ToolCall(
            callId: "call_b",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD1\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA, callB };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = "out-a-complete", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);

        var replayedCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            var type = item.GetString("type");
            var callId = item.GetString("call_id");
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                replayedCallIds.Add(callId!);
                continue;
            }

            if (string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                outputCallIds.Add(callId!);
            }
        }

        Assert.Contains("call_a", replayedCallIds);
        Assert.DoesNotContain("call_b", replayedCallIds);
        Assert.Contains("call_a", outputCallIds);
        Assert.DoesNotContain("call_b", outputCallIds);
    }

    private static void AssertPackFallbackTelemetry(string reason, string expectedSourcePack, string expectedTargetTool, string expectedFamily) {
        Assert.Contains("ix:pack-fallback:v1", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source_pack=" + expectedSourcePack, reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target_tool=" + expectedTargetTool, reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("family=" + expectedFamily, reason, StringComparison.OrdinalIgnoreCase);
    }

}
