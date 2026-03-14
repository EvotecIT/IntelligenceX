using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared catalog of Active Directory handoff routes for entity normalization and scope discovery.
/// </summary>
public static class ActiveDirectoryEntityHandoffCatalog {
    /// <summary>
    /// Builds the standard Active Directory entity handoff route pair.
    /// </summary>
    public static ToolHandoffRoute[] CreateEntityHandoffRoutes(
        string entityHandoffSourceField,
        string entityHandoffReason,
        string scopeDiscoverySourceField,
        string scopeDiscoveryReason,
        bool scopeDiscoveryIsRequired = false) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_handoff_prepare",
                reason: entityHandoffReason,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(entityHandoffSourceField, "entity_handoff")
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: scopeDiscoveryReason,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(
                        scopeDiscoverySourceField,
                        "domain_controller",
                        isRequired: scopeDiscoveryIsRequired)
                })
        };
    }

    /// <summary>
    /// Builds the standard Active Directory entity handoff routes plus selected System remote-host follow-up routes.
    /// </summary>
    public static ToolHandoffRoute[] CreateEntityAndSelectedSystemRoutes(
        string entityHandoffSourceField,
        string entityHandoffReason,
        string scopeDiscoverySourceField,
        string scopeDiscoveryReason,
        IReadOnlyList<string> systemSourceFields,
        IReadOnlyList<(string TargetToolName, string? ReasonOverride)> systemRouteSelections,
        bool scopeDiscoveryIsRequired = false,
        bool systemRoutesAreRequired = false) {
        var adRoutes = CreateEntityHandoffRoutes(
            entityHandoffSourceField: entityHandoffSourceField,
            entityHandoffReason: entityHandoffReason,
            scopeDiscoverySourceField: scopeDiscoverySourceField,
            scopeDiscoveryReason: scopeDiscoveryReason,
            scopeDiscoveryIsRequired: scopeDiscoveryIsRequired);
        var systemRoutes = SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
            sourceFields: systemSourceFields,
            routeSelections: systemRouteSelections,
            isRequired: systemRoutesAreRequired);
        return CrossPackRouteComposer.Combine(adRoutes, systemRoutes);
    }
}
