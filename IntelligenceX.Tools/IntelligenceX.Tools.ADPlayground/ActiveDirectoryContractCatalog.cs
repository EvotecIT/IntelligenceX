using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Pack-owned Active Directory contract shapes used by the ADPlayground pack.
/// </summary>
public static class ActiveDirectoryContractCatalog {
    private const string PackInfoToolName = "ad_pack_info";

    /// <summary>
    /// Stable setup hint keys for Active Directory scope and connectivity discovery.
    /// </summary>
    public static readonly string[] SetupHintKeys = {
        "domain_controller",
        "search_base_dn",
        "domain_name",
        "forest_name"
    };

    private static readonly string[] LdapConnectivityHintKeys = {
        "domain_controller",
        "domain_name",
        "forest_name"
    };

    private static readonly string[] RetryableErrorCodes = {
        "timeout",
        "query_failed",
        "probe_failed",
        "discovery_failed",
        "transport_unavailable"
    };

    /// <summary>
    /// Builds the standard Active Directory environment setup contract.
    /// </summary>
    public static ToolSetupContract CreateDirectoryContextSetup() {
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
                    hintKeys: LdapConnectivityHintKeys,
                    isRequired: true)
            },
            setupHintKeys: SetupHintKeys);
    }

    /// <summary>
    /// Resolves the default Active Directory setup contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateDirectoryContextSetup();
    }

    /// <summary>
    /// Builds the standard Active Directory retry and recovery contract.
    /// </summary>
    public static ToolRecoveryContract CreateStandardRecovery() {
        return ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: true,
            maxRetryAttempts: 1,
            retryableErrorCodes: RetryableErrorCodes,
            recoveryToolNames: new[] { "ad_environment_discover" });
    }

    /// <summary>
    /// Resolves the default Active Directory retry and recovery contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateStandardRecovery();
    }

    /// <summary>
    /// Resolves the default Active Directory handoff contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolHandoffContract? CreateHandoff(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();

        if (string.Equals(normalizedToolName, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "context/domain_controller",
                fallbackSourceField: "domain_controllers/0/value",
                reason: "Promote discovered AD domain-controller context into remote ComputerX host diagnostics.");
        }

        if (string.Equals(normalizedToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "domain_controllers/0/value",
                fallbackSourceField: "requested_scope/domain_controller",
                reason: "Promote discovered AD domain-controller inventory into remote ComputerX host diagnostics.");
        }

        if (string.Equals(normalizedToolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "normalized_request/domain_controller",
                fallbackSourceField: "normalized_request/targets/0",
                reason: "Promote AD monitoring probe targets into remote ComputerX follow-up diagnostics.");
        }

        if (string.Equals(normalizedToolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase)) {
            return CreatePreparedIdentityHandoff();
        }

        return null;
    }

    /// <summary>
    /// Builds the standard Active Directory handoff-preparation follow-up contract.
    /// </summary>
    public static ToolHandoffContract CreatePreparedIdentityHandoff() {
        return ToolContractDefaults.CreateHandoff(ActiveDirectoryFollowUpCatalog.CreatePreparedIdentityRoutes());
    }

    /// <summary>
    /// Builds the standard Active Directory host pivot contract into System and EventLog packs.
    /// </summary>
    public static ToolHandoffContract CreateSystemHostPivotHandoff(
        string primarySourceField,
        string fallbackSourceField,
        string reason) {
        return ToolContractDefaults.CreateHandoff(
            RemoteHostFollowUpCatalog.CreateSystemAndEventLogChannelDiscoveryRoutes(
                sourceFields: new[] { primarySourceField, fallbackSourceField },
                systemReason: reason,
                eventLogReason: "Pivot into remote Event Log channel discovery for the discovered AD host before live log triage.",
                isRequired: false));
    }
}
