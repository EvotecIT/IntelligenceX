using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogArtifactFollowUpCatalog {
    public static ToolHandoffRoute[] CreateEvtxPathRoutes() {
        return new[] {
            CreatePathRoute(
                targetToolName: "eventlog_evtx_query",
                reason: "Promote discovered EVTX file paths directly into bounded EVTX querying."),
            CreatePathRoute(
                targetToolName: "eventlog_evtx_security_summary",
                reason: "Promote discovered EVTX security logs directly into security-summary analysis."),
            CreatePathRoute(
                targetToolName: "eventlog_evtx_stats",
                reason: "Promote discovered EVTX file paths into fast count/statistics follow-up.")
        };
    }

    private static ToolHandoffRoute CreatePathRoute(string targetToolName, string reason) {
        return ToolContractDefaults.CreateRoute(
            targetPackId: "eventlog",
            targetToolName: targetToolName,
            reason: reason,
            bindings: new[] {
                ToolContractDefaults.CreateBinding("files[].path", "path")
            },
            followUpKind: ToolHandoffFollowUpKinds.Investigation,
            followUpPriority: ToolHandoffFollowUpPriorities.Normal);
    }
}
