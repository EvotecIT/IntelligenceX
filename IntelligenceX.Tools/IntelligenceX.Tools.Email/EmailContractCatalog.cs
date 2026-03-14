using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.Email;

internal static class EmailContractCatalog {
    private const string PackInfoToolName = "email_pack_info";

    public static readonly string[] SetupHintKeys = {
        "folder",
        "query",
        "from",
        "to",
        "subject",
        "auth_probe_id"
    };

    private static readonly string[] RetryableErrorCodes = {
        "timeout",
        "query_failed",
        "connection_failed"
    };

    public static ToolSetupContract CreateAuthenticationSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "email_pack_info",
            requirementId: "email_account_authentication",
            requirementKind: ToolSetupRequirementKinds.Authentication,
            setupHintKeys: SetupHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateAuthenticationSetup();
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName, bool isWriteCapable) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (isWriteCapable) {
            return ToolContractDefaults.CreateNoRetryRecovery();
        }

        var supportsRetry = string.Equals(toolName, "email_imap_search", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(toolName, "email_imap_get", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(toolName, "email_smtp_probe", StringComparison.OrdinalIgnoreCase);

        return ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: supportsRetry,
            maxRetryAttempts: supportsRetry ? 1 : 0,
            retryableErrorCodes: supportsRetry ? RetryableErrorCodes : Array.Empty<string>());
    }
}
