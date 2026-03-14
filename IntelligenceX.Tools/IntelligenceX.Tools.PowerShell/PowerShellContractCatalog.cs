using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

internal static class PowerShellContractCatalog {
    private const string PackInfoToolName = "powershell_pack_info";

    public static readonly string[] SetupHintKeys = {
        "host",
        "host_name",
        "host_names",
        "timeout_ms",
        "intent"
    };

    private static readonly string[] RetryableErrorCodes = {
        "timeout",
        "query_failed",
        "probe_failed"
    };

    public static ToolSetupContract CreateHostConnectivitySetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "powershell_environment_discover",
            requirementId: "powershell_host_connectivity",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: SetupHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateHostConnectivitySetup();
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName, bool isWriteCapable) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return isWriteCapable
            ? ToolContractDefaults.CreateNoRetryRecovery(
                recoveryToolNames: new[] { "powershell_environment_discover" })
            : ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 1,
                retryableErrorCodes: RetryableErrorCodes,
                recoveryToolNames: new[] { "powershell_environment_discover", "powershell_hosts" });
    }
}
