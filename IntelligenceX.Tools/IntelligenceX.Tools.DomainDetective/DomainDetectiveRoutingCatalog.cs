using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

internal static class DomainDetectiveRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["domaindetective_checks_catalog"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["domaindetective_domain_summary"] = ToolRoutingTaxonomy.RoleOperational,
            ["domaindetective_network_probe"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "dns",
        "mx",
        "spf",
        "dmarc",
        "dkim",
        "ns",
        "dnssec",
        "caa",
        "whois",
        "mta_sts",
        "bimi",
        "domaindetective",
        "domain_detective"
    };

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "DomainDetective");
    }
}
