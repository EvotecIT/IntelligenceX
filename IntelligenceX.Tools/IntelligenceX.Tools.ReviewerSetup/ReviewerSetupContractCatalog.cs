using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

internal static class ReviewerSetupContractCatalog {
    private const string PackInfoToolName = "reviewer_setup_pack_info";
    private const string ContractVerifyToolName = "reviewer_setup_contract_verify";

    public static ToolSetupContract? CreateSetup(string toolName) {
        return IsPackTool(toolName) ? null : null;
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        return IsPackTool(toolName) ? null : null;
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        return IsPackTool(toolName) ? null : null;
    }

    private static bool IsPackTool(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        return string.Equals(normalizedToolName, PackInfoToolName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalizedToolName, ContractVerifyToolName, StringComparison.OrdinalIgnoreCase);
    }
}
