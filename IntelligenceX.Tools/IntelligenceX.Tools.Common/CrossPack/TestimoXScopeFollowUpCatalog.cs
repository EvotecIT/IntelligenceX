using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared cross-pack follow-up route builders for TestimoX scope and domain-controller pivots.
/// </summary>
public static class TestimoXScopeFollowUpCatalog {
    /// <summary>
    /// Builds the standard TestimoX scope follow-up routes into AD scope discovery plus remote System and Event Log pivots.
    /// </summary>
    public static ToolHandoffRoute[] CreateScopeAndHostFollowUpRoutes(
        string domainSourceField,
        string domainControllerSourceField,
        string adReason,
        string systemReason,
        bool hostRoutesAreRequired = false,
        bool adRouteIsRequired = false) {
        var adRoutes = new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: adReason,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(domainSourceField, "domain_name", isRequired: adRouteIsRequired),
                    ToolContractDefaults.CreateBinding(domainControllerSourceField, "domain_controller", isRequired: adRouteIsRequired)
                })
        };

        var hostRoutes = RemoteHostFollowUpCatalog.CreateSystemAndEventLogTargetRoutes(
            sourceField: domainControllerSourceField,
            systemReason: systemReason,
            eventLogReason: "Promote TestimoX scope evidence into remote Event Log live statistics for the same domain controller.",
            isRequired: hostRoutesAreRequired);
        var channelRoutes = EventLogRemoteHostFollowUpCatalog.CreateChannelDiscoveryRoutes(
            sourceFields: new[] { domainControllerSourceField },
            primaryReasonOverride: "Promote TestimoX scope evidence into remote Event Log channel discovery for the same domain controller before log triage.",
            isRequired: hostRoutesAreRequired);
        return CrossPackRouteComposer.Combine(adRoutes, hostRoutes, channelRoutes);
    }
}
