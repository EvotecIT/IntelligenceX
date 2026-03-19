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
                AdditionalTags: new[] { "probe", "preflight", "connectivity" })
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
            "eventlog_channels_list",
            "eventlog_providers_list",
            "eventlog_named_events_catalog",
            "eventlog_timeline_explain",
            "eventlog_live_stats",
            "eventlog_evtx_stats");
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
