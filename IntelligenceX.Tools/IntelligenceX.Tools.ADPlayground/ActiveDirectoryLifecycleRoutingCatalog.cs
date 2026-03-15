using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryLifecycleRoutingCatalog {
    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "dc",
        "ldap",
        "active_directory",
        "adplayground",
        "identity_lifecycle",
        "joiner",
        "leaver",
        "offboarding",
        "password_reset",
        "computer_account",
        "host_provisioning",
        "group_account",
        "membership_management"
    };

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ad_lifecycle_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["ad_user_lifecycle"] = ToolRoutingTaxonomy.RoleOperational,
            ["ad_computer_lifecycle"] = ToolRoutingTaxonomy.RoleOperational,
            ["ad_group_lifecycle"] = ToolRoutingTaxonomy.RoleOperational
        };

    private static readonly IReadOnlyDictionary<string, SelectionDescriptor> ExplicitSelectionDescriptors =
        new Dictionary<string, SelectionDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["ad_user_lifecycle"] = new(
                Scope: "identity",
                Operation: "write",
                Entity: "user",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "joiner", "leaver", "offboarding", "provisioning" }),
            ["ad_computer_lifecycle"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "computer",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "provisioning", "decommissioning", "password_reset", "host_account" }),
            ["ad_group_lifecycle"] = new(
                Scope: "identity",
                Operation: "write",
                Entity: "group",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "provisioning", "membership", "decommissioning", "group_account" })
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
        if (!string.IsNullOrWhiteSpace(explicitRole)) {
            return ToolRoutingRoleResolver.ResolveExplicitOrFallback(
                explicitRole: explicitRole,
                fallbackRole: ToolRoutingTaxonomy.RoleOperational,
                packDisplayName: "ActiveDirectoryLifecycle");
        }

        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (DeclaredRolesByToolName.TryGetValue(normalizedToolName, out var declaredRole)) {
            return declaredRole;
        }

        throw new InvalidOperationException(
            $"ActiveDirectoryLifecycle tool '{normalizedToolName}' must declare an explicit routing role or be added to the known-tool role catalog.");
    }

    private sealed record SelectionDescriptor(
        string Scope,
        string Operation,
        string Entity,
        string Risk,
        IReadOnlyList<string> AdditionalTags);
}
