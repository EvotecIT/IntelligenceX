using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryLifecycleContractCatalog {
    private const string PackInfoToolName = "ad_lifecycle_pack_info";
    private const string UserLifecycleToolName = "ad_user_lifecycle";
    private const string ComputerLifecycleToolName = "ad_computer_lifecycle";
    private const string GroupLifecycleToolName = "ad_group_lifecycle";
    private const string OuLifecycleToolName = "ad_ou_lifecycle";

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
        var routes = normalizedToolName switch {
            _ when string.Equals(normalizedToolName, UserLifecycleToolName, StringComparison.OrdinalIgnoreCase) => CreateUserLifecycleRoutes(),
            _ when string.Equals(normalizedToolName, ComputerLifecycleToolName, StringComparison.OrdinalIgnoreCase) => CreateComputerLifecycleRoutes(),
            _ when string.Equals(normalizedToolName, GroupLifecycleToolName, StringComparison.OrdinalIgnoreCase) => CreateGroupLifecycleRoutes(),
            _ when string.Equals(normalizedToolName, OuLifecycleToolName, StringComparison.OrdinalIgnoreCase) => CreateOuLifecycleRoutes(),
            _ => null
        };

        return routes is { Length: > 0 }
            ? ToolContractDefaults.CreateHandoff(routes)
            : null;
    }

    private static ToolHandoffRoute[] CreateUserLifecycleRoutes() {
        var readOnlyRoutes = CreateReadOnlyVerificationRoutes(
            verificationReason: "Verify the affected user identity with the read-only Active Directory pack after the lifecycle action.",
            normalizationReason: "Normalize the affected user identity for follow-up AD investigations or reporting.");
        var membershipRoute = new[] {
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_user_groups_resolved",
                reason: "Verify the post-change user group footprint with resolved group details after the governed lifecycle change.",
                targetArgument: "identity",
                sourceFields: new[] { "distinguished_name", "identity", "sam_account_name", "user_principal_name" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High)
        };
        return CombineRoutes(readOnlyRoutes, membershipRoute);
    }

    private static ToolHandoffRoute[] CreateComputerLifecycleRoutes() {
        var readOnlyRoutes = CreateReadOnlyVerificationRoutes(
            verificationReason: "Verify the affected computer identity with the read-only Active Directory pack after the lifecycle action.",
            normalizationReason: "Normalize the affected computer identity for follow-up AD investigations or reporting.");
        var systemRoutes = new[] {
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "system",
                targetToolName: "system_info",
                reason: "Verify same-host runtime identity and OS context after the governed computer lifecycle change.",
                targetArgument: "computer_name",
                sourceFields: new[] { "computer_name" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High),
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "system",
                targetToolName: "system_metrics_summary",
                reason: "Collect same-host runtime telemetry after the governed computer lifecycle change.",
                targetArgument: "computer_name",
                sourceFields: new[] { "computer_name" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Investigation,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal)
        };
        var eventLogRoutes = new[] {
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_channels_list",
                reason: "Verify same-host event log channel reachability after the governed computer lifecycle change before deeper live log triage.",
                targetArgument: "machine_name",
                sourceFields: new[] { "computer_name" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal)
        };
        return CombineRoutes(readOnlyRoutes, systemRoutes, eventLogRoutes);
    }

    private static ToolHandoffRoute[] CreateGroupLifecycleRoutes() {
        var readOnlyRoutes = CreateReadOnlyVerificationRoutes(
            verificationReason: "Verify the affected group identity with the read-only Active Directory pack after the lifecycle action.",
            normalizationReason: "Normalize the affected group identity for follow-up AD investigations or reporting.");
        var membershipRoute = new[] {
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_group_members_resolved",
                reason: "Verify the post-change group membership set with resolved member details after the governed lifecycle change.",
                targetArgument: "identity",
                sourceFields: new[] { "distinguished_name", "identity", "sam_account_name" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High)
        };
        return CombineRoutes(readOnlyRoutes, membershipRoute);
    }

    private static ToolHandoffRoute[] CreateOuLifecycleRoutes() {
        return CreateReadOnlyVerificationRoutes(
            verificationReason: "Verify the affected organizational unit with the read-only Active Directory pack after the lifecycle action.",
            normalizationReason: "Normalize the affected organizational unit identity for follow-up AD investigations or reporting.");
    }

    private static ToolHandoffRoute[] CreateReadOnlyVerificationRoutes(string verificationReason, string normalizationReason) {
        return new[] {
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_object_get",
                reason: verificationReason,
                targetArgument: "identity",
                sourceFields: new[] { "distinguished_name", "identity" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleResolver,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Critical),
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_object_resolve",
                reason: normalizationReason,
                targetArgument: "identity",
                sourceFields: new[] { "distinguished_name", "identity" },
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleResolver,
                followUpKind: ToolHandoffFollowUpKinds.Normalization,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal)
        };
    }

    private static ToolHandoffRoute[] CombineRoutes(params ToolHandoffRoute[][] routeGroups) {
        var routes = new System.Collections.Generic.List<ToolHandoffRoute>();
        for (var i = 0; i < routeGroups.Length; i++) {
            var group = routeGroups[i];
            if (group is null || group.Length == 0) {
                continue;
            }

            routes.AddRange(group);
        }

        return routes.ToArray();
    }
}
