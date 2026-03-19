using System;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Pack-owned System pack contract shapes used by the ComputerX pack.
/// </summary>
public static class SystemContractCatalog {
    private const string PackInfoToolName = "system_pack_info";

    /// <summary>
    /// Stable setup hint keys for remote ComputerX host access.
    /// </summary>
    public static readonly string[] SetupHintKeys = {
        "computer_name",
        "machine_name",
        "machine_names",
        "target"
    };

    private static readonly string[] RetryableErrorCodes = {
        "timeout",
        "query_failed",
        "probe_failed",
        "access_denied",
        "transport_unavailable"
    };

    /// <summary>
    /// Builds the standard System remote-host setup contract.
    /// </summary>
    public static ToolSetupContract CreateRemoteHostAccessSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "system_connectivity_probe",
            requirementId: "system_host_access",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: SetupHintKeys);
    }

    /// <summary>
    /// Resolves the default System setup contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateRemoteHostAccessSetup();
    }

    /// <summary>
    /// Builds the standard System retry and recovery contract.
    /// </summary>
    public static ToolRecoveryContract CreateRecovery(bool supportsAlternateEngines) {
        return ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: true,
            maxRetryAttempts: 1,
            retryableErrorCodes: RetryableErrorCodes,
            recoveryToolNames: new[] { "system_connectivity_probe", "system_info" },
            supportsAlternateEngines: supportsAlternateEngines,
            alternateEngineIds: supportsAlternateEngines ? new[] { "cim", "wmi" } : null);
    }

    /// <summary>
    /// Resolves the default System retry and recovery contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolRecoveryContract? CreateRecovery(string toolName, JsonObject? parameters) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateRecovery(ToolParametersExposeAlternateEngineSelector(parameters));
    }

    /// <summary>
    /// Builds the standard System cross-pack host-context handoff contract.
    /// </summary>
    public static ToolHandoffContract CreateHostContextHandoff() {
        return ToolContractDefaults.CreateHandoff(
            SystemHostContextFollowUpCatalog.CreateHostContextRoutes(
                sourceFields: new[] { "meta/computer_name", "computer_name" }));
    }

    /// <summary>
    /// Builds the standard System probe follow-up contract into deeper same-host diagnostics.
    /// </summary>
    public static ToolHandoffContract CreateConnectivityProbeHandoff() {
        return ToolContractDefaults.CreateHandoff(
            ToolContractDefaults.CreateSharedTargetRoutes(
                targetPackId: "system",
                targetArgument: "computer_name",
                sourceFields: new[] { "computer_name", "target" },
                routeDescriptors: new (string TargetToolName, string Reason)[] {
                    ("system_info", "Promote a successful ComputerX connectivity probe into fuller runtime identity and OS collection for the same host."),
                    ("system_metrics_summary", "Promote a successful ComputerX connectivity probe into CPU and memory follow-up for the same host.")
                },
                isRequired: true,
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal));
    }

    /// <summary>
    /// Resolves the default System cross-pack handoff contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolHandoffContract? CreateHandoff(string toolName, JsonObject? parameters) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (string.Equals(normalizedToolName, PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(normalizedToolName, "system_connectivity_probe", StringComparison.OrdinalIgnoreCase)) {
            return CreateConnectivityProbeHandoff();
        }

        return !ToolParametersExposeRemoteComputerName(parameters)
            ? null
            : CreateHostContextHandoff();
    }

    /// <summary>
    /// Determines whether a tool exposes an alternate engine selector parameter.
    /// </summary>
    public static bool ToolParametersExposeAlternateEngineSelector(JsonObject? parameters) {
        var properties = parameters?.GetObject("properties");
        return properties is not null
               && ToolAlternateEngineSelectorNames.TryResolveSelectorArgumentName(properties, out _);
    }

    /// <summary>
    /// Determines whether a tool exposes the standard remote computer_name parameter.
    /// </summary>
    public static bool ToolParametersExposeRemoteComputerName(JsonObject? parameters) {
        return parameters?.GetObject("properties")?.GetObject("computer_name") is not null;
    }
}
