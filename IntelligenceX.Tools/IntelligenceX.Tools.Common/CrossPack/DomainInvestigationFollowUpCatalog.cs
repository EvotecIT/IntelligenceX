using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared cross-pack follow-up route builders for DNS and domain-investigation pivots.
/// </summary>
public static class DomainInvestigationFollowUpCatalog {
    /// <summary>
    /// Builds the standard DnsClientX query follow-up route into DomainDetective domain posture analysis.
    /// </summary>
    public static ToolHandoffRoute[] CreateDnsQueryToDomainSummaryRoutes(
        string domainSourceField,
        string dnsEndpointSourceField) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "domaindetective",
                targetToolName: "domaindetective_domain_summary",
                reason: "Promote resolver-level DNS evidence into domain posture checks.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(domainSourceField, "domain"),
                    ToolContractDefaults.CreateBinding(dnsEndpointSourceField, "dns_endpoint", isRequired: false)
                })
        };
    }

    /// <summary>
    /// Builds the standard DnsClientX ping follow-up route into richer DomainDetective network probing.
    /// </summary>
    public static ToolHandoffRoute[] CreateDnsPingToNetworkProbeRoutes(
        string hostSourceField,
        string timeoutSourceField) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "domaindetective",
                targetToolName: "domaindetective_network_probe",
                reason: "Escalate host-level reachability checks to richer network probes when needed.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(hostSourceField, "host"),
                    ToolContractDefaults.CreateBinding(timeoutSourceField, "timeout_ms", isRequired: false)
                })
        };
    }

    /// <summary>
    /// Builds the standard DomainDetective domain-summary follow-up routes into AD scope and discovery diagnostics.
    /// </summary>
    public static ToolHandoffRoute[] CreateDomainSummaryToActiveDirectoryRoutes(string domainSourceField) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: "Escalate directory-scoped investigations into explicit AD scope discovery.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(domainSourceField, "domain_name")
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_directory_discovery_diagnostics",
                reason: "Follow up on directory-focused domain issues with AD discovery diagnostics.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(domainSourceField, "forest_name")
                })
        };
    }

    /// <summary>
    /// Builds the standard DomainDetective host-probe follow-up route into AD scope discovery.
    /// </summary>
    public static ToolHandoffRoute[] CreateNetworkProbeToActiveDirectoryRoutes(string hostSourceField) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: "Use host evidence as an AD domain-controller hint when intent shifts to directory diagnostics.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(hostSourceField, "domain_controller")
                })
        };
    }
}
