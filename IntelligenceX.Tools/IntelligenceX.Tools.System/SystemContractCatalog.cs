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
    /// Resolves the default System cross-pack handoff contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolHandoffContract? CreateHandoff(string toolName, JsonObject? parameters) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
               || !ToolParametersExposeRemoteComputerName(parameters)
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
