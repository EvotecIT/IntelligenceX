using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

internal static class FileSystemRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["fs_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["fs_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["fs_read"] = ToolRoutingTaxonomy.RoleOperational,
            ["fs_search"] = ToolRoutingTaxonomy.RoleResolver
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "filesystem",
        "file",
        "folder",
        "path",
        "content"
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "FileSystem");
    }
}
