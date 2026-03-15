using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryLifecycleContractCatalog {
    private const string PackInfoToolName = "ad_lifecycle_pack_info";
    private const string UserLifecycleToolName = "ad_user_lifecycle";
    private const string ComputerLifecycleToolName = "ad_computer_lifecycle";
    private const string GroupLifecycleToolName = "ad_group_lifecycle";

    public static readonly string[] SetupHintKeys = {
        "domain_name",
        "domain_controller",
        "identity",
        "sam_account_name",
        "organizational_unit",
        "operation"
    };

    public static ToolSetupContract CreateDirectoryLifecycleSetup() {
        return ToolContractDefaults.CreateSetup(
            setupToolName: "ad_environment_discover",
            requirements: new[] {
                ToolContractDefaults.CreateRequirement(
                    requirementId: "ad_directory_context",
                    requirementKind: ToolSetupRequirementKinds.Configuration,
                    hintKeys: SetupHintKeys,
                    isRequired: true),
                ToolContractDefaults.CreateRequirement(
                    requirementId: "ad_ldap_connectivity",
                    requirementKind: ToolSetupRequirementKinds.Connectivity,
                    hintKeys: ActiveDirectoryContractCatalog.SetupHintKeys,
                    isRequired: true)
            },
            setupHintKeys: SetupHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateDirectoryLifecycleSetup();
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName, bool isWriteCapable) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return isWriteCapable
            ? ToolContractDefaults.CreateNoRetryRecovery(
                recoveryToolNames: new[] { "ad_environment_discover", PackInfoToolName })
            : ActiveDirectoryContractCatalog.CreateStandardRecovery();
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        return string.Equals(normalizedToolName, UserLifecycleToolName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedToolName, ComputerLifecycleToolName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedToolName, GroupLifecycleToolName, StringComparison.OrdinalIgnoreCase)
            ? ToolContractDefaults.CreateHandoff(
                new[] {
                    ToolContractDefaults.CreateSharedTargetRoute(
                        targetPackId: "active_directory",
                        targetToolName: "ad_object_get",
                        reason: "Verify the affected identity with the read-only Active Directory pack after the lifecycle action.",
                        targetArgument: "identity",
                        sourceFields: new[] { "distinguished_name", "identity" },
                        isRequired: false,
                        targetRole: ToolRoutingTaxonomy.RoleResolver,
                        followUpKind: ToolHandoffFollowUpKinds.Verification,
                        followUpPriority: ToolHandoffFollowUpPriorities.Critical),
                    ToolContractDefaults.CreateSharedTargetRoute(
                        targetPackId: "active_directory",
                        targetToolName: "ad_object_resolve",
                        reason: "Normalize the affected identity for follow-up AD investigations or reporting.",
                        targetArgument: "identity",
                        sourceFields: new[] { "distinguished_name", "identity" },
                        isRequired: false,
                        targetRole: ToolRoutingTaxonomy.RoleResolver,
                        followUpKind: ToolHandoffFollowUpKinds.Normalization,
                        followUpPriority: ToolHandoffFollowUpPriorities.Normal)
                })
            : null;
    }
}
