using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName = BuildDeclaredRolesByToolName();
    private static readonly IReadOnlyDictionary<string, SelectionDescriptor> ExplicitSelectionDescriptors =
        new Dictionary<string, SelectionDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_connectivity_probe"] = new(
                Scope: "host",
                Operation: "probe",
                Entity: "eventlog",
                Risk: ToolRoutingTaxonomy.RiskLow,
                AdditionalTags: new[] { "probe", "preflight", "connectivity" }),
            ["eventlog_channel_policy_set"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "eventlog_channel",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "channel_policy", "governed_write", "retention", "lifecycle" }),
            ["eventlog_classic_log_ensure"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "eventlog_classic_log",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "classic_log", "governed_write", "provisioning", "source" }),
            ["eventlog_classic_log_remove"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "eventlog_classic_log",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "classic_log", "governed_write", "cleanup", "source" }),
            ["eventlog_collector_subscriptions_list"] = new(
                Scope: "host",
                Operation: "list",
                Entity: "eventlog_subscription",
                Risk: ToolRoutingTaxonomy.RiskLow,
                AdditionalTags: new[] { "collector_subscription", "inventory", "subscription", "wec" }),
            ["eventlog_collector_subscription_set"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "eventlog_subscription",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "collector_subscription", "governed_write", "subscription", "wec" })
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "eventlog",
        "security",
        "kerberos",
        "gpo",
        "ad_domain",
        "dc"
    };

    public static ToolDefinition ApplySelectionMetadata(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        return ExplicitSelectionDescriptors.TryGetValue((definition.Name ?? string.Empty).Trim(), out var descriptor)
            ? ToolExplicitSelectionMetadata.Apply(
                definition,
                scope: descriptor.Scope,
                operation: descriptor.Operation,
                entity: descriptor.Entity,
                risk: descriptor.Risk,
                additionalTags: descriptor.AdditionalTags)
            : definition;
    }

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "EventLog");
    }

    private static IReadOnlyDictionary<string, string> BuildDeclaredRolesByToolName() {
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddRoleGroup(declared, ToolRoutingTaxonomy.RolePackInfo,
            "eventlog_pack_info");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleDiagnostic,
            "eventlog_connectivity_probe",
            "eventlog_collector_subscriptions_list",
            "eventlog_channels_list",
            "eventlog_providers_list",
            "eventlog_named_events_catalog",
            "eventlog_timeline_explain",
            "eventlog_live_stats",
            "eventlog_evtx_stats");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleOperational,
            "eventlog_channel_policy_set",
            "eventlog_classic_log_ensure",
            "eventlog_classic_log_remove",
            "eventlog_collector_subscription_set");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleResolver,
            "eventlog_named_events_query",
            "eventlog_timeline_query",
            "eventlog_top_events",
            "eventlog_live_query",
            "eventlog_evtx_find",
            "eventlog_evtx_security_summary",
            "eventlog_evtx_query");
        return declared;
    }

    private static void AddRoleGroup(
        IDictionary<string, string> declared,
        string role,
        params string[] toolNames) {
        foreach (var toolName in toolNames) {
            declared[toolName] = role;
        }
    }

    private sealed record SelectionDescriptor(
        string Scope,
        string Operation,
        string Entity,
        string Risk,
        string[] AdditionalTags);
}
