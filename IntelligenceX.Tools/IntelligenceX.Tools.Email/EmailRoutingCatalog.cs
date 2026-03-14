using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

internal static class EmailRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["email_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["email_imap_search"] = ToolRoutingTaxonomy.RoleResolver,
            ["email_imap_get"] = ToolRoutingTaxonomy.RoleOperational,
            ["email_smtp_probe"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["email_smtp_send"] = ToolRoutingTaxonomy.RoleOperational
        };

    private static readonly IReadOnlyDictionary<string, SelectionDescriptor> ExplicitSelectionDescriptors =
        new Dictionary<string, SelectionDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["email_smtp_send"] = new(
                Scope: "message",
                Operation: "write",
                Entity: "message",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "smtp", "send" })
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "email",
        "imap",
        "smtp",
        "mailbox",
        "message"
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
            packDisplayName: "Email");
    }

    private sealed record SelectionDescriptor(
        string Scope,
        string Operation,
        string Entity,
        string Risk,
        string[] AdditionalTags);
}
