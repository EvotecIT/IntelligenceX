using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

internal static class OfficeImoRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["officeimo_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["officeimo_read"] = ToolRoutingTaxonomy.RoleOperational
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "officeimo",
        "document",
        "word",
        "excel",
        "powerpoint",
        "pdf",
        "markdown",
        "file"
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "OfficeIMO");
    }
}
