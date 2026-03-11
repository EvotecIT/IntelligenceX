using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["eventlog_channels_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["eventlog_providers_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["eventlog_named_events_catalog"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["eventlog_named_events_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_timeline_explain"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["eventlog_timeline_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_top_events"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_live_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_live_stats"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["eventlog_evtx_find"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_evtx_security_summary"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_evtx_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["eventlog_evtx_stats"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

    private static readonly string[] SetupHintKeys = {
        "machine_name",
        "machine_names",
        "channel",
        "channels",
        "evtx_path",
        "path"
    };

    private static readonly string[] EventLogSignalTokens = {
        "eventlog",
        "eventviewerx",
        "security",
        "kerberos",
        "gpo",
        "ad_domain",
        "dc"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
        return ToolDefinitionOverlay.WithDefinition(tool, updatedDefinition);
    }

    private static ToolRoutingContract BuildRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = string.IsNullOrWhiteSpace(existing?.RoutingContractId)
                ? ToolRoutingContract.DefaultContractId
                : existing!.RoutingContractId,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = "eventlog",
            Role = ResolveDeclaredRole(definition.Name, existing?.Role),
            DomainIntentFamily = string.IsNullOrWhiteSpace(existing?.DomainIntentFamily)
                ? ToolSelectionMetadata.DomainIntentFamilyAd
                : existing!.DomainIntentFamily,
            DomainIntentActionId = string.IsNullOrWhiteSpace(existing?.DomainIntentActionId)
                ? ToolSelectionMetadata.DomainIntentActionIdAd
                : existing!.DomainIntentActionId,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : EventLogSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateRequiredSetup(
                setupToolName: "eventlog_channels_list",
                requirementId: "eventlog_channel_access",
                requirementKind: ToolSetupRequirementKinds.Connectivity,
                setupHintKeys: SetupHintKeys));
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "eventlog_evtx_find", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "eventlog",
                    targetToolName: "eventlog_evtx_query",
                    reason: "Promote discovered EVTX files into local event inspection.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("files[].path", "path")
                    }),
                ToolContractDefaults.CreateRoute(
                    targetPackId: "eventlog",
                    targetToolName: "eventlog_evtx_security_summary",
                    reason: "Promote discovered EVTX files into local authentication-focused security summaries.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("files[].path", "path")
                    }),
                ToolContractDefaults.CreateRoute(
                    targetPackId: "eventlog",
                    targetToolName: "eventlog_evtx_stats",
                    reason: "Promote discovered EVTX files into local EVTX statistics and prevalence analysis.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("files[].path", "path")
                    })
            });
        }

        if (string.Equals(definition.Name, "eventlog_evtx_security_summary", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(ToolContractDefaults.CreateActiveDirectoryEntityHandoffRoutes(
                entityHandoffSourceField: "meta/entity_handoff",
                entityHandoffReason: "Promote EVTX security summary entity handoff payload into AD identity normalization before lookups.",
                scopeDiscoverySourceField: "meta/entity_handoff/computer_candidates/0/value",
                scopeDiscoveryReason: "Seed AD scope discovery with host evidence from local EVTX security summary analysis.",
                scopeDiscoveryIsRequired: false));
        }

        if (string.Equals(definition.Name, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Name, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(ToolContractDefaults.CreateActiveDirectoryEntityHandoffRoutes(
                entityHandoffSourceField: "meta/entity_handoff",
                entityHandoffReason: "Promote EventLog entity handoff payload into AD identity normalization before lookups.",
                scopeDiscoverySourceField: "meta/entity_handoff/computer_candidates/0/value",
                scopeDiscoveryReason: "Seed AD scope discovery with host evidence from EventLog query context.",
                scopeDiscoveryIsRequired: false));
        }

        return definition.Handoff;
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => {
                var supportsRetry = definition.Name.IndexOf("_query", StringComparison.OrdinalIgnoreCase) >= 0
                                    || definition.Name.IndexOf("_find", StringComparison.OrdinalIgnoreCase) >= 0
                                    || definition.Name.IndexOf("_top_events", StringComparison.OrdinalIgnoreCase) >= 0;

                return ToolContractDefaults.CreateRecovery(
                    supportsTransientRetry: supportsRetry,
                    maxRetryAttempts: supportsRetry ? 1 : 0,
                    retryableErrorCodes: supportsRetry
                        ? new[] { "timeout", "query_failed", "probe_failed", "transport_unavailable" }
                        : Array.Empty<string>(),
                    recoveryToolNames: new[] { "eventlog_channels_list" });
            });
    }

    private static string ResolveDeclaredRole(string toolName, string? existingRole) {
        var normalizedExistingRole = NormalizeRole(existingRole);
        if (normalizedExistingRole.Length > 0) {
            return normalizedExistingRole;
        }

        if (DeclaredRolesByToolName.TryGetValue(toolName, out var declaredRole)) {
            return declaredRole;
        }

        throw new InvalidOperationException(
            $"EventLog tool '{toolName}' must declare an explicit routing role in EventLogToolContracts or ToolDefinition.Routing.Role.");
    }

    private static string NormalizeRole(string? role) {
        var normalized = (role ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.ToLowerInvariant();
        if (!ToolRoutingTaxonomy.IsAllowedRole(normalized)) {
            throw new InvalidOperationException(
                $"EventLog tool routing role '{role}' is invalid. Allowed values: {string.Join(", ", ToolRoutingTaxonomy.AllowedRoles)}.");
        }

        return normalized;
    }
}
