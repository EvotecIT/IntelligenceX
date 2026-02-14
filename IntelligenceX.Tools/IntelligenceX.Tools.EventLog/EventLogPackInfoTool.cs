using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Returns event log pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class EventLogPackInfoTool : EventLogToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "eventlog_pack_info",
        "Return event log pack capabilities, output contract, and recommended usage patterns. Call this first when planning event investigations.",
        ToolSchema.Object()
            .NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="EventLogPackInfoTool"/> class.
    /// </summary>
    /// <param name="options">Tool options.</param>
    public EventLogPackInfoTool(EventLogToolOptions options) : base(options) { }

    /// <summary>
    /// Tool schema/definition used for registration and tool calling.
    /// </summary>
    public override ToolDefinition Definition => DefinitionValue;

    /// <summary>
    /// Invokes the tool.
    /// </summary>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON string result.</returns>
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "eventlog",
            engine: "EventViewerX",
            tools: ToolRegistryEventLogExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use eventlog_evtx_query or eventlog_live_query for event evidence.",
                "Use eventlog_evtx_stats/eventlog_live_stats for top-level aggregation.",
                "For remote live logs, pass machine_name (and optional session_timeout_ms) to eventlog_live_query/eventlog_live_stats.",
                "Use security report tools for lockouts/logons/failures when investigating authentication incidents.",
                "For AD identity correlation: call ad_environment_discover, then ad_search using eventlog report ad_correlation candidates."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Collect raw event evidence",
                    suggestedTools: new[] { "eventlog_evtx_query", "eventlog_live_query" }),
                ToolPackGuidance.FlowStep(
                    goal: "Build aggregate signal baselines",
                    suggestedTools: new[] { "eventlog_evtx_stats", "eventlog_live_stats" }),
                ToolPackGuidance.FlowStep(
                    goal: "Investigate authentication incidents",
                    suggestedTools: new[] { "eventlog_evtx_report_failed_logons", "eventlog_evtx_report_account_lockouts", "eventlog_evtx_report_user_logons" }),
                ToolPackGuidance.FlowStep(
                    goal: "Correlate event identities with AD objects",
                    suggestedTools: new[] { "ad_environment_discover", "ad_search" },
                    notes: "Use ad_correlation payload from security report tools to drive AD lookups.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "event_evidence",
                    summary: "Query EVTX files and live channels while preserving raw event payloads.",
                    primaryTools: new[] { "eventlog_evtx_query", "eventlog_live_query" }),
                ToolPackGuidance.Capability(
                    id: "event_aggregation",
                    summary: "Compute top-level statistics from EVTX files and live event channels.",
                    primaryTools: new[] { "eventlog_evtx_stats", "eventlog_live_stats" }),
                ToolPackGuidance.Capability(
                    id: "security_reports",
                    summary: "Produce focused lockout/logon/failure views for security investigations.",
                    primaryTools: new[] { "eventlog_evtx_report_failed_logons", "eventlog_evtx_report_account_lockouts", "eventlog_evtx_report_user_logons" }),
                ToolPackGuidance.Capability(
                    id: "ad_correlation_hints",
                    summary: "Emit AD follow-up hints/candidate identities from security report aggregates.",
                    primaryTools: new[] { "eventlog_evtx_report_failed_logons", "eventlog_evtx_report_account_lockouts", "eventlog_evtx_report_user_logons" },
                    notes: "Use ad_environment_discover + ad_search in the AD pack for identity enrichment.")
            },
            toolCatalog: ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve raw event arrays and report objects for model reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only.",
            correlationGuidance: "Security report tools include ad_correlation candidates to bootstrap AD lookups and cross-pack reasoning.",
            setupHints: new {
                Platform = Environment.OSVersion.Platform.ToString(),
                MaxResults = Options.MaxResults,
                MaxMessageChars = Options.MaxMessageChars,
                AllowedRootsCount = Options.AllowedRoots.Count
            },
            note: "EVTX and live-channel workflows are available. Use raw payload fields for correlation across tools.");

        var summary = ToolMarkdown.SummaryText(
            title: "EventLog Pack",
            "Use raw event payloads for reasoning/correlation; use `*_view` fields for presentation.");

        return Task.FromResult(ToolResponse.OkModel(model: root, summaryMarkdown: summary));
    }
}
