using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared cross-pack follow-up route builders for TestimoX analytics artifacts and target history.
/// </summary>
public static class TestimoXAnalyticsFollowUpCatalog {
    /// <summary>
    /// Builds the standard diagnostics follow-up routes into local snapshot inspection plus remote host and log pivots.
    /// </summary>
    public static ToolHandoffRoute[] CreateDiagnosticsArtifactRoutes(
        string snapshotPathSourceField,
        string targetSourceField,
        bool targetRoutesAreRequired = false) {
        var fileRoute = new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "filesystem",
                targetToolName: "fs_read",
                reason: "Promote analytics diagnostics into local snapshot file inspection when the raw diagnostics JSON is needed.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(snapshotPathSourceField, "path")
                })
        };
        var targetRoutes = RemoteHostFollowUpCatalog.CreateSystemAndEventLogTargetRoutes(
            sourceField: targetSourceField,
            systemReason: "Promote slow probe diagnostics into remote host inspection for affected targets.",
            eventLogReason: "Promote slow probe diagnostics into remote EventViewerX follow-up for affected targets.",
            isRequired: targetRoutesAreRequired);
        return CrossPackRouteComposer.Combine(fileRoute, targetRoutes);
    }

    /// <summary>
    /// Builds the standard report-job-history follow-up routes into cached artifacts and optional local file inspection.
    /// </summary>
    public static ToolHandoffRoute[] CreateReportJobHistoryArtifactRoutes(
        string historyDirectorySourceField,
        string reportKeySourceField,
        string reportPathSourceField) {
        return new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "testimox_analytics",
                targetToolName: "testimox_report_data_snapshot_get",
                reason: "Promote report job history into cached monitoring report data snapshot retrieval.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(historyDirectorySourceField, "history_directory"),
                    ToolContractDefaults.CreateBinding(reportKeySourceField, "report_key")
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "testimox_analytics",
                targetToolName: "testimox_report_snapshot_get",
                reason: "Promote report job history into cached monitoring HTML report snapshot retrieval.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(historyDirectorySourceField, "history_directory"),
                    ToolContractDefaults.CreateBinding(reportKeySourceField, "report_key")
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "filesystem",
                targetToolName: "fs_read",
                reason: "Promote report job history into local report file inspection when a stored report path is available.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding(reportPathSourceField, "path", isRequired: false)
                })
        };
    }

    /// <summary>
    /// Builds the standard history-target follow-up routes into remote host and Event Log pivots.
    /// </summary>
    public static ToolHandoffRoute[] CreateHistoryTargetRoutes(
        string targetSourceField,
        bool targetRoutesAreRequired = false) {
        return RemoteHostFollowUpCatalog.CreateSystemAndEventLogTargetRoutes(
            sourceField: targetSourceField,
            systemReason: "Promote monitoring target history into remote host inspection.",
            eventLogReason: "Promote monitoring target history into remote EventViewerX follow-up.",
            isRequired: targetRoutesAreRequired);
    }
}
