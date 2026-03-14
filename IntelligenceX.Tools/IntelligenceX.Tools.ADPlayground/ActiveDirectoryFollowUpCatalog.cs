using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryFollowUpCatalog {
    public static ToolHandoffRoute[] CreatePreparedIdentityRoutes() {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_object_resolve",
                reason: "Use normalized identities from handoff payload for batched AD object resolution.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("target_arguments/ad_object_resolve/identities", "identities")
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: "Use discovered domain hints to bootstrap AD scope before resolution calls.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(
                        "target_arguments/ad_scope_discovery/domain_name",
                        "domain_name",
                        isRequired: false),
                    ToolContractDefaults.CreateBinding(
                        "target_arguments/ad_scope_discovery/include_domain_controllers",
                        "include_domain_controllers",
                        isRequired: false)
                })
        };
    }
}
