using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

internal static class ReviewerSetupRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["reviewer_setup_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["reviewer_setup_contract_verify"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "reviewer",
        "setup",
        "onboarding",
        "contract",
        "verification"
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "Reviewer Setup");
    }
}
