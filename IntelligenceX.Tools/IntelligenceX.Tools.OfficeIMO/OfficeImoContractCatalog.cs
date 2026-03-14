using System;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.OfficeIMO;

internal static class OfficeImoContractCatalog {
    private const string PackInfoToolName = "officeimo_pack_info";

    public static readonly string[] SetupHintKeys = {
        "path",
        "recurse",
        "extensions",
        "max_files",
        "max_total_bytes",
        "max_input_bytes"
    };

    private static readonly string[] RetryableErrorCodes = {
        "io_error",
        "access_denied",
        "timeout",
        "parse_failed"
    };

    public static ToolSetupContract CreatePathAccessSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "officeimo_pack_info",
            requirementId: "officeimo_path_access",
            requirementKind: ToolSetupRequirementKinds.Capability,
            setupHintKeys: SetupHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreatePathAccessSetup();
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (!string.Equals(toolName, "officeimo_read", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return ToolContractDefaults.CreateHandoff(
            LocalFileInspectionFollowUpCatalog.CreateFilesystemReadRoutes(
                pathSourceField: "files[]",
                reason: "Promote normalized document extraction into raw source file inspection when the original file is needed."));
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: true,
            maxRetryAttempts: 1,
            retryableErrorCodes: RetryableErrorCodes);
    }
}
