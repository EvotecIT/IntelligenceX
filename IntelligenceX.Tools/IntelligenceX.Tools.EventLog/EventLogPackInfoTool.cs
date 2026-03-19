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
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "eventlog_pack_info",
        description: "Return event log pack capabilities, output contract, and recommended usage patterns. Call this first when planning event investigations.",
        packId: "eventlog",
        category: "eventlog",
        tags: new[] {
            "pack:eventlog",
            "domain_family:ad_domain",
            "domain_signals:eventlog,eventviewerx,security,kerberos,gpo,ad_domain,dc"
        },
        domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
        domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
        domainSignalTokens: new[] {
            "eventlog",
            "eventviewerx",
            "security",
            "kerberos",
            "gpo",
            "ad_domain",
            "dc"
        });

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
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            binder: BindRequest,
            execute: ExecuteAsync);
    }

    private static ToolRequestBindingResult<PackInfoRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<PackInfoRequest>.Success(new PackInfoRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<PackInfoRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var root = BuildGuidance(Options);

        var summary = ToolMarkdown.SummaryText(
            title: "EventLog Pack",
            "Use raw event payloads for reasoning/correlation; use `*_view` fields for presentation.");

        return Task.FromResult(ToolResultV2.OkModel(model: root, summaryMarkdown: summary));
    }

    internal static ToolPackInfoModel BuildGuidance(EventLogToolOptions options) {
        return ToolPackGuidance.Create(
            pack: "eventlog",
            engine: "EventViewerX",
            tools: ToolRegistryEventLogExtensions.GetRegisteredToolNames(options),
            recommendedFlow: new[] {
                "Use eventlog_connectivity_probe first when remote channel visibility, machine reachability, or live log access is uncertain before deeper EventViewerX queries.",
                "Use eventlog_top_events for quick triage (top N most recent events from a live channel, local or remote).",
                "Use eventlog_evtx_query or eventlog_live_query for event evidence.",
                "Use eventlog_evtx_stats/eventlog_live_stats for top-level aggregation.",
                "Use eventlog_evtx_security_summary for authentication-focused EVTX summaries (user_logons/failed_logons/account_lockouts).",
                "Use eventlog_named_events_catalog/eventlog_named_events_query for rule-based, intent-level detections (AD/Kerberos/GPO/etc).",
                "Use eventlog_timeline_explain to get reusable timeline-query guidance from current investigation shape.",
                "Use eventlog_timeline_query to build reusable timeline and correlation views from named-event evidence (no fixed report templates).",
                "Example local EVTX -> AD flow: eventlog_evtx_find -> eventlog_timeline_query(correlation_profile=identity) -> ad_handoff_prepare -> ad_scope_discovery -> ad_object_resolve.",
                "Example remote live -> AD flow: eventlog_live_query/eventlog_timeline_query(machine_name) -> ad_handoff_prepare -> ad_scope_discovery -> ad_search.",
                "Example remote live -> system flow: eventlog_named_events_query/eventlog_timeline_query(machine_name) -> system_info/system_metrics_summary(computer_name) for host-state follow-up.",
                "For remote live logs, pass machine_name (and optional session_timeout_ms) to eventlog_live_query/eventlog_live_stats.",
                "For authentication investigations and timeline correlation, use eventlog_named_events_catalog first, then eventlog_timeline_query with named_events/categories and either correlation_profile or explicit correlation_keys.",
                "For AD identity correlation: call ad_handoff_prepare first, then ad_scope_discovery and ad_search/ad_object_resolve."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Validate local or remote channel reachability",
                    suggestedTools: new[] { "eventlog_connectivity_probe", "eventlog_channels_list" },
                    notes: "Start here when machine reachability, channel visibility, or live Security/System log access is still uncertain."),
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
                    goal: "Summarize Security authentication signals from EVTX evidence",
                    suggestedTools: new[] { "eventlog_evtx_security_summary" },
                    notes: "Start here for authentication-centric triage before moving into timeline correlation or AD enrichment."),
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
                    suggestedTools: new[] { "eventlog_timeline_query", "ad_handoff_prepare", "ad_scope_discovery", "ad_search", "ad_object_resolve" },
                    notes: "Normalize event identities via ad_handoff_prepare before AD lookups to keep correlation flows reusable.")
                ,
                ToolPackGuidance.FlowStep(
                    goal: "Pivot correlated event hosts into remote system diagnostics",
                    suggestedTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query", "system_info", "system_metrics_summary" },
                    notes: "Use computer candidates from EventLog handoff metadata to continue with ComputerX-backed host diagnostics.")
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
                    id: "security_auth_summary",
                    summary: "Build authentication-centric EVTX summaries for user logons, failed logons, and account lockouts.",
                    primaryTools: new[] { "eventlog_evtx_security_summary" }),
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
                    primaryTools: new[] { "eventlog_named_events_catalog", "eventlog_timeline_query", "eventlog_named_events_query", "eventlog_evtx_query", "ad_handoff_prepare" },
                    notes: "Use ad_handoff_prepare + ad_scope_discovery + ad_search/ad_object_resolve in the AD pack for identity enrichment.")
                ,
                ToolPackGuidance.Capability(
                    id: "host_followup_pivots",
                    summary: "Promote correlated event hosts into remote ComputerX/system follow-up diagnostics.",
                    primaryTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query", "system_info", "system_metrics_summary" },
                    notes: "Prefer this when the investigation needs CPU, memory, disk, update, or posture follow-up for the same host.")
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "event_identity_to_ad_lookup",
                    summary: "Promote normalized identity and host fields from EventLog evidence into AD lookup workflows.",
                    entityKinds: new[] { "identity", "user", "host", "computer" },
                    sourceTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query" },
                    targetTools: new[] { "ad_handoff_prepare", "ad_scope_discovery", "ad_search", "ad_object_resolve" },
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
            recipes: new[] {
                ToolPackGuidance.Recipe(
                    id: "live_authentication_triage",
                    summary: "Run a live authentication-focused investigation on local or remote event channels.",
                    whenToUse: "Use when the question is about current or recent logons, lockouts, Kerberos signals, or security-channel activity on a live host.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Quickly inspect the latest channel activity",
                            suggestedTools: new[] { "eventlog_top_events", "eventlog_live_query" },
                            notes: "Pass machine_name for remote live investigation and use a wider session_timeout_ms only when the remote query needs it."),
                        ToolPackGuidance.FlowStep(
                            goal: "Select reusable named detections and authentication summaries",
                            suggestedTools: new[] { "eventlog_named_events_catalog", "eventlog_named_events_query", "eventlog_evtx_security_summary" },
                            notes: "Use named-event rules and authentication summaries before hand-building filters so follow-up stays reusable."),
                        ToolPackGuidance.FlowStep(
                            goal: "Build a correlation timeline from the detected evidence",
                            suggestedTools: new[] { "eventlog_timeline_explain", "eventlog_timeline_query" },
                            notes: "Use correlation_profile first, then override correlation_keys only when the incident shape demands it."),
                        ToolPackGuidance.FlowStep(
                            goal: "Promote identities into AD verification",
                            suggestedTools: new[] { "ad_handoff_prepare", "ad_scope_discovery", "ad_object_resolve" },
                            notes: "Normalize the identity and host fields before AD lookups so the same workflow works for DOMAIN\\user, UPN, and hostname evidence.")
                    },
                    verificationTools: new[] { "eventlog_timeline_query", "ad_object_resolve" }),
                ToolPackGuidance.Recipe(
                    id: "offline_evtx_timeline",
                    summary: "Triage EVTX files and turn the evidence into reusable event timelines and pivots.",
                    whenToUse: "Use when the source is an EVTX export rather than a live machine, especially for offline incident review or evidence packaging.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Inspect EVTX volume and authentication shape",
                            suggestedTools: new[] { "eventlog_evtx_stats", "eventlog_evtx_security_summary" },
                            notes: "Start with stats or security summary before running heavier EVTX evidence queries."),
                        ToolPackGuidance.FlowStep(
                            goal: "Collect raw EVTX evidence",
                            suggestedTools: new[] { "eventlog_evtx_query", "eventlog_evtx_find" }),
                        ToolPackGuidance.FlowStep(
                            goal: "Build the timeline and correlation plan",
                            suggestedTools: new[] { "eventlog_timeline_explain", "eventlog_timeline_query" },
                            notes: "Use timeline explain to choose the next correlation profile or explicit keys before rerunning the timeline."),
                        ToolPackGuidance.FlowStep(
                            goal: "Pivot the resulting identities or hosts into follow-up packs",
                            suggestedTools: new[] { "ad_handoff_prepare", "ad_object_resolve", "system_info", "system_metrics_summary" },
                            notes: "Use AD for identity ownership and System for same-host runtime follow-up when the EVTX evidence points to a concrete machine.")
                    },
                    verificationTools: new[] { "eventlog_evtx_query", "eventlog_timeline_query" }),
                ToolPackGuidance.Recipe(
                    id: "event_host_followup",
                    summary: "Promote correlated event hosts into targeted remote ComputerX diagnostics.",
                    whenToUse: "Use when EventLog already identified the interesting host and the next step is CPU, memory, disk, or posture follow-up on that same machine.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Collect host-linked event evidence",
                            suggestedTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query" },
                            notes: "Prefer queries that preserve computer or host fields so the handoff to System stays explicit."),
                        ToolPackGuidance.FlowStep(
                            goal: "Run focused remote host diagnostics",
                            suggestedTools: new[] { "system_info", "system_metrics_summary", "system_logical_disks_list", "system_windows_update_client_status" },
                            notes: "Reuse the event-derived host as computer_name rather than asking the planner to infer a new target."),
                        ToolPackGuidance.FlowStep(
                            goal: "Return to EventLog only if host state suggests a narrower rerun",
                            suggestedTools: new[] { "eventlog_live_query", "eventlog_timeline_query" },
                            notes: "Use the host diagnostics outcome to focus the next channel, time range, or named-event rerun.")
                    },
                    verificationTools: new[] { "system_info", "system_metrics_summary", "eventlog_timeline_query" })
            },
            toolCatalog: ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(options),
            runtimeCapabilities: new ToolPackRuntimeCapabilitiesModel {
                PreferredEntryTools = new[] { "eventlog_channels_list", "eventlog_top_events", "eventlog_live_query" },
                PreferredProbeTools = new[] { "eventlog_connectivity_probe" },
                ProbeHelperFreshnessWindowSeconds = 300,
                SetupHelperFreshnessWindowSeconds = 900,
                RecipeHelperFreshnessWindowSeconds = 300,
                RuntimePrerequisites = new[] {
                    "EVTX file workflows require target files to be inside the configured AllowedRoots locations.",
                    "Remote live-channel queries require machine_name plus target log access on the remote host.",
                    "Use session_timeout_ms when long-running remote live queries need a larger execution window.",
                    "Use eventlog_connectivity_probe before deeper remote queries when host reachability or channel access is uncertain."
                },
                Notes = "For authentication or AD investigations, call eventlog_named_events_catalog before eventlog_timeline_query so the timeline uses reusable named-event evidence rather than ad hoc filtering."
            },
            rawPayloadPolicy: "Preserve raw event arrays and report objects for model reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only.",
            correlationGuidance: "Use eventlog_timeline_query with correlation_profile presets or explicit correlation_keys, then normalize entity_handoff via ad_handoff_prepare and run ad_scope_discovery before AD lookups.",
            setupHints: new {
                Platform = Environment.OSVersion.Platform.ToString(),
                MaxResults = options.MaxResults,
                MaxMessageChars = options.MaxMessageChars,
                AllowedRootsCount = options.AllowedRoots.Count
            },
            note: "EVTX and live-channel workflows are available. Use raw payload fields for correlation across tools.");
    }
}
