using System;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXContractCatalog {
    private static readonly string[] RulesRunRetryableErrorCodes = { "execution_failed", "timeout", "transport_unavailable" };
    private static readonly string[] RecoveryToolNames = { "testimox_rules_list" };

    public static ToolSetupContract CreateHintOnlySetup(IReadOnlyList<string> hintKeys) {
        return ToolContractDefaults.CreateHintOnlySetup(hintKeys);
    }

    public static ToolSetupContract CreateRulesCatalogSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "testimox_rules_list",
            requirementId: "testimox_rules_catalog",
            requirementKind: ToolSetupRequirementKinds.Capability,
            setupHintKeys: TestimoXRoutingCatalog.SetupHintKeys);
    }

    public static ToolRecoveryContract CreateRecovery(string toolName) {
        var supportsRetry = string.Equals(toolName, "testimox_rules_run", StringComparison.OrdinalIgnoreCase);
        return supportsRetry
            ? ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 1,
                retryableErrorCodes: RulesRunRetryableErrorCodes,
                recoveryToolNames: RecoveryToolNames)
            : ToolContractDefaults.CreateNoRetryRecovery(recoveryToolNames: RecoveryToolNames);
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        if (string.Equals(toolName, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)) {
            return CreateScopeAndHostFollowUpHandoff(
                domainSourceField: "include_domains/0",
                domainControllerSourceField: "include_domain_controllers/0",
                adReason: "Promote explicit TestimoX execution scope into AD scope discovery for the same domain/DC set.",
                systemReason: "Promote explicit TestimoX execution scope into ComputerX-backed remote host diagnostics for the same domain controller.");
        }

        if (string.Equals(toolName, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)) {
            return CreateScopeAndHostFollowUpHandoff(
                domainSourceField: "rows/0/domain",
                domainControllerSourceField: "rows/0/domain_controller",
                adReason: "Promote stored TestimoX run scope into AD scope discovery before identity or ownership follow-up.",
                systemReason: "Promote stored TestimoX run domain-controller evidence into ComputerX-backed remote host diagnostics.");
        }

        return null;
    }

    private static ToolHandoffContract CreateScopeAndHostFollowUpHandoff(
        string domainSourceField,
        string domainControllerSourceField,
        string adReason,
        string systemReason) {
        return ToolContractDefaults.CreateHandoff(
            TestimoXScopeFollowUpCatalog.CreateScopeAndHostFollowUpRoutes(
                domainSourceField: domainSourceField,
                domainControllerSourceField: domainControllerSourceField,
                adReason: adReason,
                systemReason: systemReason,
                hostRoutesAreRequired: false,
                adRouteIsRequired: false));
    }
}
