using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Returns TestimoX analytics artifact pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class TestimoXAnalyticsPackInfoTool : TestimoXToolBase, ITool {
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "testimox_analytics_pack_info",
        description: "Return TestimoX analytics artifact pack capabilities, output contract, and recommended usage patterns. Call this first when planning history/report/snapshot inspection.",
        packId: "testimox_analytics");

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXAnalyticsPackInfoTool"/> class.
    /// </summary>
    public TestimoXAnalyticsPackInfoTool(TestimoXToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
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

        var root = ToolPackGuidance.Create(
            pack: "testimox_analytics",
            engine: "ADPlayground.Monitoring",
            tools: ToolRegistryTestimoXAnalyticsExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Call testimox_analytics_diagnostics_get when you need a compact operator-facing diagnostics snapshot from an allowed analytics history directory before deeper history/report drill-down.",
                "Call testimox_dashboard_autogenerate_status_get when you need dashboard scheduler state, last outcome, next run, or generated report path from an allowed analytics history directory.",
                "Call testimox_availability_rollup_status_get when you need the latest rollup refresh timestamp, bucket freshness, or row counts from an allowed analytics history directory.",
                "Call testimox_probe_index_status when you need the latest indexed status per probe from an allowed monitoring history directory without loading raw probe history.",
                "Call testimox_maintenance_window_history when you need resolved reporting maintenance windows and suppression scope from an allowed monitoring history directory.",
                "Call testimox_report_data_snapshot_get when you need the cached report data payload for a known report key and want a safe preview by default.",
                "Call testimox_report_snapshot_get when you need the cached HTML report snapshot for a known report key and want a safe preview by default.",
                "Call testimox_history_query when you need monitoring availability history rollups from an allowed monitoring history directory before asking for trend or outage summaries.",
                "Call testimox_report_job_history when you need monitoring report execution history from an allowed monitoring history directory before opening stored report artifacts.",
                "Keep this pack separate from live rule execution: for rules, baselines, profiles, and stored runs, pivot back to testimox_pack_info."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Inspect compact analytics diagnostics",
                    suggestedTools: new[] { "testimox_analytics_diagnostics_get" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect dashboard auto-generate scheduler state",
                    suggestedTools: new[] { "testimox_dashboard_autogenerate_status_get" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect availability rollup refresh status",
                    suggestedTools: new[] { "testimox_availability_rollup_status_get" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect latest indexed probe status",
                    suggestedTools: new[] { "testimox_probe_index_status" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect resolved maintenance windows",
                    suggestedTools: new[] { "testimox_maintenance_window_history" }),
                ToolPackGuidance.FlowStep(
                    goal: "Retrieve cached report data payload",
                    suggestedTools: new[] { "testimox_report_data_snapshot_get" }),
                ToolPackGuidance.FlowStep(
                    goal: "Retrieve cached HTML report snapshot",
                    suggestedTools: new[] { "testimox_report_snapshot_get" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect monitoring availability history",
                    suggestedTools: new[] { "testimox_history_query" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect monitoring report execution history",
                    suggestedTools: new[] { "testimox_report_job_history" }),
                ToolPackGuidance.FlowStep(
                    goal: "Pivot back to core TestimoX workflows",
                    suggestedTools: new[] { "testimox_pack_info" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "analytics_diagnostics",
                    summary: "Load a compact monitoring diagnostics snapshot with queue, notification, maintenance, SQLite health, reachability, and optional slow-probe summaries.",
                    primaryTools: new[] { "testimox_analytics_diagnostics_get" }),
                ToolPackGuidance.Capability(
                    id: "dashboard_autogenerate_status",
                    summary: "Load dashboard auto-generate scheduler state, run outcome, cooldowns, and generated report file context from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_dashboard_autogenerate_status_get" }),
                ToolPackGuidance.Capability(
                    id: "availability_rollup_status",
                    summary: "Load availability rollup refresh timestamps, latest bucket freshness, row counts, and last refresh error from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_availability_rollup_status_get" }),
                ToolPackGuidance.Capability(
                    id: "probe_index_status",
                    summary: "Read the latest indexed status and completion time per probe from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_probe_index_status" }),
                ToolPackGuidance.Capability(
                    id: "maintenance_window_history",
                    summary: "Query resolved reporting maintenance windows, overlap ranges, suppression flags, and targeting filters from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_maintenance_window_history" }),
                ToolPackGuidance.Capability(
                    id: "report_data_snapshot",
                    summary: "Load a cached report data snapshot by report_key with preview-first payload access from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_report_data_snapshot_get" }),
                ToolPackGuidance.Capability(
                    id: "report_snapshot",
                    summary: "Load a cached HTML report snapshot by report_key with preview-first HTML access from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_report_snapshot_get" }),
                ToolPackGuidance.Capability(
                    id: "monitoring_history",
                    summary: "Query monitoring availability rollup buckets, probe scope, and uptime/problem counts from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_history_query" }),
                ToolPackGuidance.Capability(
                    id: "report_job_history",
                    summary: "List monitoring report generation jobs, status, timing, and captured history/report metrics from an allowed monitoring history directory.",
                    primaryTools: new[] { "testimox_report_job_history" })
            },
            recipes: new[] {
                ToolPackGuidance.Recipe(
                    id: "monitoring_health_triage",
                    summary: "Start with compact monitoring diagnostics, then expand into freshness, probe, and maintenance evidence only where needed.",
                    whenToUse: "Use when the user wants to know whether monitoring, dashboard generation, or rollups are healthy before opening larger history artifacts.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Load the compact monitoring diagnostics snapshot",
                            suggestedTools: new[] { "testimox_analytics_diagnostics_get", "testimox_dashboard_autogenerate_status_get" },
                            notes: "Start here for queue, scheduler, maintenance, and report generation state."),
                        ToolPackGuidance.FlowStep(
                            goal: "Validate probe freshness and rollup recency",
                            suggestedTools: new[] { "testimox_probe_index_status", "testimox_availability_rollup_status_get" },
                            notes: "Use these as lightweight preflight before loading broader monitoring history."),
                        ToolPackGuidance.FlowStep(
                            goal: "Inspect maintenance and history detail only when the summary points there",
                            suggestedTools: new[] { "testimox_maintenance_window_history", "testimox_history_query", "testimox_report_job_history" })
                    },
                    verificationTools: new[] { "testimox_analytics_diagnostics_get", "testimox_probe_index_status", "testimox_availability_rollup_status_get" }),
                ToolPackGuidance.Recipe(
                    id: "report_snapshot_followup",
                    summary: "Validate report generation state first, then open only the specific cached data or HTML snapshot that matches the user’s report key.",
                    whenToUse: "Use when the request is about a known report, dashboard artifact, or cached monitoring summary and you want to avoid opening the wrong snapshot.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Confirm report job and dashboard state",
                            suggestedTools: new[] { "testimox_dashboard_autogenerate_status_get", "testimox_report_job_history" }),
                        ToolPackGuidance.FlowStep(
                            goal: "Load the relevant report data or HTML snapshot",
                            suggestedTools: new[] { "testimox_report_data_snapshot_get", "testimox_report_snapshot_get" },
                            notes: "Use preview-first access until you know the exact report_key and artifact shape.")
                    },
                    verificationTools: new[] { "testimox_report_data_snapshot_get", "testimox_report_snapshot_get" })
            },
            toolCatalog: ToolRegistryTestimoXAnalyticsExtensions.GetRegisteredToolCatalog(Options),
            runtimeCapabilities: new ToolPackRuntimeCapabilitiesModel {
                PreferredEntryTools = new[] { "testimox_analytics_diagnostics_get", "testimox_dashboard_autogenerate_status_get", "testimox_history_query" },
                PreferredProbeTools = new[] { "testimox_probe_index_status", "testimox_availability_rollup_status_get" },
                ProbeHelperFreshnessWindowSeconds = 300,
                SetupHelperFreshnessWindowSeconds = 900,
                RecipeHelperFreshnessWindowSeconds = 600,
                RuntimePrerequisites = new[] {
                    "Analytics and history tools require a history directory under the configured AllowedHistoryRoots.",
                    "Use report_key when loading cached report data or HTML snapshots so artifact retrieval stays deterministic.",
                    "Prefer the compact diagnostics and status tools before loading broader history or snapshot content."
                },
                Notes = "Treat probe index and rollup status tools as lightweight preflight for analytics freshness, then open only the specific history or snapshot artifacts the investigation needs."
            },
            rawPayloadPolicy: "Preserve raw monitoring/history/report payloads. Do not rely only on preview fields.",
            viewProjectionPolicy: "Snapshot preview arguments are view-only and meant to keep artifact inspection safe by default.",
            correlationGuidance: "Use report keys, probe names, time windows, and maintenance identifiers to correlate monitoring artifacts before pivoting into core TestimoX, AD, or System follow-up.",
            setupHints: new {
                Enabled = Options.Enabled,
                AllowedHistoryRootsCount = Options.AllowedHistoryRoots.Count,
                MaxHistoryRowsInCatalog = Options.MaxHistoryRowsInCatalog,
                MaxSnapshotContentChars = Options.MaxSnapshotContentChars
            });

        var summary = ToolMarkdown.SummaryText(
            title: "TestimoX Analytics Pack",
            "Use this pack for persisted analytics, report, and history artifacts.",
            "Use testimox_pack_info for live rule, profile, baseline, and stored-run workflows.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }
}
