using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsContractCatalog {
    private const string PackInfoToolName = "testimox_analytics_pack_info";

    public static ToolSetupContract CreateHintOnlySetup() {
        return ToolContractDefaults.CreateHintOnlySetup(TestimoXAnalyticsRoutingCatalog.SetupHintKeys);
    }

    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, System.StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateHintOnlySetup();
    }

    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, System.StringComparison.OrdinalIgnoreCase)
            ? null
            : ToolContractDefaults.CreateNoRetryRecovery();
    }

    public static ToolHandoffContract? CreateHandoff(string toolName) {
        if (string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, System.StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(toolName, "testimox_analytics_diagnostics_get", System.StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                TestimoXAnalyticsFollowUpCatalog.CreateDiagnosticsArtifactRoutes(
                    snapshotPathSourceField: "snapshot_path",
                    targetSourceField: "slow_probes[].target",
                    targetRoutesAreRequired: false));
        }

        if (string.Equals(toolName, "testimox_report_job_history", System.StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                TestimoXAnalyticsFollowUpCatalog.CreateReportJobHistoryArtifactRoutes(
                    historyDirectorySourceField: "history_directory",
                    reportKeySourceField: "jobs[].report_key",
                    reportPathSourceField: "jobs[].report_path"));
        }

        if (string.Equals(toolName, "testimox_history_query", System.StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(
                TestimoXAnalyticsFollowUpCatalog.CreateHistoryTargetRoutes(
                    targetSourceField: "rows[].target",
                    targetRoutesAreRequired: false));
        }

        return null;
    }
}
