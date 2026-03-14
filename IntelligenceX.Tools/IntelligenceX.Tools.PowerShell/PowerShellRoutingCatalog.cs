using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

internal static class PowerShellRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["powershell_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["powershell_environment_discover"] = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            ["powershell_hosts"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["powershell_run"] = ToolRoutingTaxonomy.RoleOperational
        };

    private static readonly IReadOnlyDictionary<string, SelectionDescriptor> ExplicitSelectionDescriptors =
        new Dictionary<string, SelectionDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["powershell_run"] = new(
                Scope: "host",
                Operation: "execute_write",
                Entity: "command",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "execution", "mutating" })
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "powershell",
        "script",
        "command",
        "host",
        "session"
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
            packDisplayName: "PowerShell");
    }

    private sealed record SelectionDescriptor(
        string Scope,
        string Operation,
        string Entity,
        string Risk,
        string[] AdditionalTags);
}
