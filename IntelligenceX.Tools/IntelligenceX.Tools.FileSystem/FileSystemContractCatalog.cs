using System;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.FileSystem;

internal static class FileSystemContractCatalog {
    private const string PackInfoToolName = "fs_pack_info";

    public static readonly string[] SetupHintKeys = {
        "path",
        "folder",
        "recurse",
        "pattern"
    };

    private static readonly string[] RetryableErrorCodes = {
        "io_error",
        "access_denied",
        "timeout",
        "query_failed"
    };

    public static ToolSetupContract CreatePathAccessSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "fs_list",
            requirementId: "filesystem_path_access",
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

        if (string.Equals(toolName, "fs_list", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                LocalFileInspectionFollowUpCatalog.CreateFilesystemReadRoutes(
                    pathSourceField: "entries[].path",
                    reason: "Promote listed file entries into direct local content inspection.",
                    isRequired: false));
        }

        if (string.Equals(toolName, "fs_search", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                LocalFileInspectionFollowUpCatalog.CreateFilesystemReadRoutes(
                    pathSourceField: "matches[].path",
                    reason: "Promote content search matches into direct local file inspection."));
        }

        return null;
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
