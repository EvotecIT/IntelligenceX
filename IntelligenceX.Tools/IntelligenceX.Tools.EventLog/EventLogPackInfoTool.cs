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
                "Use eventlog_top_events for quick triage (top N most recent events from a live channel, local or remote).",
                "Use eventlog_evtx_query or eventlog_live_query for event evidence.",
                "Use eventlog_evtx_stats/eventlog_live_stats for top-level aggregation.",
                "Use eventlog_named_events_catalog/eventlog_named_events_query for rule-based, intent-level detections (AD/Kerberos/GPO/etc).",
                "Use eventlog_timeline_explain to get reusable timeline-query guidance from current investigation shape.",
                "Use eventlog_timeline_query to build reusable timeline and correlation views from named-event evidence (no fixed report templates).",
                "For remote live logs, pass machine_name (and optional session_timeout_ms) to eventlog_live_query/eventlog_live_stats.",
                "For authentication investigations and timeline correlation, use eventlog_named_events_catalog first, then eventlog_timeline_query with named_events/categories and either correlation_profile or explicit correlation_keys.",
                "For AD identity correlation: call ad_environment_discover, then ad_search using identities extracted from eventlog_named_events_query evidence."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Quickly triage most recent events",
                    suggestedTools: new[] { "eventlog_top_events" },
                    notes: "Use for questions like: \"top 5 events from AD0 System\" before running heavier queries."),
                ToolPackGuidance.FlowStep(
                    goal: "Collect raw event evidence",
                    suggestedTools: new[] { "eventlog_evtx_query", "eventlog_live_query" }),
                ToolPackGuidance.FlowStep(
                    goal: "Build aggregate signal baselines",
                    suggestedTools: new[] { "eventlog_evtx_stats", "eventlog_live_stats" }),
                ToolPackGuidance.FlowStep(
                    goal: "Run named-event rule detections",
                    suggestedTools: new[] { "eventlog_named_events_catalog", "eventlog_named_events_query" }),
                ToolPackGuidance.FlowStep(
                    goal: "Get timeline tuning guidance before reruns",
                    suggestedTools: new[] { "eventlog_timeline_explain" },
                    notes: "Use investigation_goal plus current timeline shape to choose correlation_profile/keys, bucket_minutes, and follow-ups."),
                ToolPackGuidance.FlowStep(
                    goal: "Build generic event timelines and correlation groups",
                    suggestedTools: new[] { "eventlog_named_events_catalog", "eventlog_timeline_explain", "eventlog_timeline_query" },
                    notes: "Start with correlation_profile presets, then tune correlation_keys (for example who/object_affected/computer/action) when needed."),
                ToolPackGuidance.FlowStep(
                    goal: "Investigate authentication incidents",
                    suggestedTools: new[] { "eventlog_named_events_catalog", "eventlog_timeline_query", "eventlog_evtx_query" }),
                ToolPackGuidance.FlowStep(
                    goal: "Correlate event identities with AD objects",
                    suggestedTools: new[] { "eventlog_timeline_query", "ad_environment_discover", "ad_search" },
                    notes: "Drive AD lookups from normalized user/domain identities in named-events query evidence.")
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
                    id: "named_event_rules",
                    summary: "Run EventViewerX named-event rule detections and return normalized evidence rows.",
                    primaryTools: new[] { "eventlog_named_events_catalog", "eventlog_named_events_query" }),
                ToolPackGuidance.Capability(
                    id: "timeline_correlation",
                    summary: "Build reusable timelines and correlation groups from named-event evidence using caller-selected dimensions.",
                    primaryTools: new[] { "eventlog_named_events_catalog", "eventlog_timeline_explain", "eventlog_timeline_query" }),
                ToolPackGuidance.Capability(
                    id: "correlation_workflow",
                    summary: "Use named-event detections as reusable correlation building blocks across log, timeline, and AD enrichment workflows.",
                    primaryTools: new[] { "eventlog_named_events_catalog", "eventlog_timeline_query", "eventlog_named_events_query", "eventlog_evtx_query" },
                    notes: "Use ad_environment_discover + ad_search in the AD pack for identity enrichment.")
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "event_identity_to_ad_lookup",
                    summary: "Promote normalized identity and host fields from EventLog evidence into AD lookup workflows.",
                    entityKinds: new[] { "identity", "user", "host", "computer" },
                    sourceTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query" },
                    targetTools: new[] { "ad_environment_discover", "ad_search", "ad_object_resolve" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("events[].who", "identity", "Trim and preserve UPN/DOMAIN\\\\user forms."),
                        ToolPackGuidance.EntityFieldMapping("events[].object_affected", "identity", "Use when object_affected represents a user/computer identity."),
                        ToolPackGuidance.EntityFieldMapping("events[].computer", "identity", "Prefer hostname/FQDN values for AD computer lookups."),
                        ToolPackGuidance.EntityFieldMapping("timeline[].who", "identities", "Batch values into identities for ad_object_resolve."),
                        ToolPackGuidance.EntityFieldMapping("timeline[].object_affected", "identities", "Batch values into identities for ad_object_resolve."),
                        ToolPackGuidance.EntityFieldMapping("timeline[].computer", "identities", "Batch host identities into ad_object_resolve inputs.")
                    },
                    notes: "Use structured field correlation (not incident-specific templates) and de-duplicate identities before AD calls.")
            },
            toolCatalog: ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve raw event arrays and report objects for model reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only.",
            correlationGuidance: "Use eventlog_timeline_query with correlation_profile presets or explicit correlation_keys, then bootstrap AD lookups from user/domain payload evidence.",
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
