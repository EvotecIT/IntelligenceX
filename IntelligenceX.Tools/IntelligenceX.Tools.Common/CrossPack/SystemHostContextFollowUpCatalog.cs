using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared cross-pack follow-up route builders for System host-context pivots into AD and Event Log.
/// </summary>
public static class SystemHostContextFollowUpCatalog {
    /// <summary>
    /// Builds the standard System host-context follow-up routes using caller-provided source fields.
    /// </summary>
    public static ToolHandoffRoute[] CreateHostContextRoutes(IReadOnlyList<string> sourceFields) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: "Reuse the same host as an AD scope or domain-controller hint when ComputerX evidence indicates directory follow-up.",
                bindings: CreateBindings(sourceFields, "domain_controller")),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_channels_list",
                reason: "Reuse the same host for remote Event Log channel discovery before live log triage.",
                bindings: CreateBindings(sourceFields, "machine_name"))
        };
    }

    private static ToolHandoffBinding[] CreateBindings(IReadOnlyList<string> sourceFields, string targetArgument) {
        var bindings = new ToolHandoffBinding[sourceFields?.Count ?? 0];
        for (var i = 0; i < bindings.Length; i++) {
            bindings[i] = ToolContractDefaults.CreateBinding(sourceFields![i], targetArgument, isRequired: false);
        }

        return bindings;
    }
}
