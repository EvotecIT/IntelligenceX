using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Returns TestimoX pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class TestimoXPackInfoTool : TestimoXToolBase, ITool {
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = new(
        "testimox_pack_info",
        "Return TestimoX pack capabilities, output contract, and recommended usage patterns. Call this first when planning rule-based diagnostics.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="TestimoXPackInfoTool"/> class.
    /// </summary>
    public TestimoXPackInfoTool(TestimoXToolOptions options) : base(options) { }

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
            pack: "testimox",
            engine: "TestimoX",
            tools: ToolRegistryTestimoXExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Call testimox_analytics_pack_info when you need persisted analytics/report/history artifact guidance; that surface is intentionally split from live rule workflows.",
                "Call testimox_runs_list when you need to inspect stored TestimoX execution history inside an allowed result store before drilling into a specific run.",
                "Call testimox_run_summary when you need per-run score and penalty summaries plus stored rule rows from a known run_id.",
                "Call testimox_baselines_list when you need to discover available vendor baseline ids/products/versions before crosswalk or operator reporting.",
                "Call testimox_baseline_compare when you need a direct vendor delta view for one product/version slice before summarizing posture drift.",
                "Call testimox_profiles_list when you need curated execution profiles and assessment-area defaults.",
                "Call testimox_rule_inventory to inspect authoritative-vs-legacy coverage, migration state, and assessment-area classification before choosing rules.",
                "Call testimox_source_query when you need rule provenance such as enum mapping, modules, supported systems, and supersedence links.",
                "Call testimox_baseline_crosswalk to map rules to vendor baselines and PingCastle/PurpleKnight references before reporting operator deltas.",
                "Call testimox_rules_list to discover available rules and metadata (scope/source_type/origin/categories/tags/cost).",
                "When you need deterministic pagination, call testimox_rules_list with page_size + offset (or cursor) and continue from next_offset/next_cursor.",
                "Select a focused subset using explicit names and/or selectors (patterns/categories/tags/source_type/rule_origin).",
                "Apply execution scope controls when needed (include_domains/include_domain_controllers/exclude_* and include_trusted_domains).",
                "Use AD discovery receipts (ad_environment_discover/ad_scope_discovery/ad_forest_discover) to prefill TestimoX scope arguments before execution.",
                "Call testimox_rules_run and correlate run output with AD/system/eventlog evidence."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Inspect monitoring artifacts in the separate monitoring pack",
                    suggestedTools: new[] { "testimox_analytics_pack_info" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect stored execution history",
                    suggestedTools: new[] { "testimox_runs_list", "testimox_run_summary" }),
                ToolPackGuidance.FlowStep(
                    goal: "Discover available vendor baselines",
                    suggestedTools: new[] { "testimox_baselines_list" }),
                ToolPackGuidance.FlowStep(
                    goal: "Compare vendor baseline deltas",
                    suggestedTools: new[] { "testimox_baseline_compare" }),
                ToolPackGuidance.FlowStep(
                    goal: "Understand curated profiles",
                    suggestedTools: new[] { "testimox_profiles_list" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inventory authoritative vs legacy rules",
                    suggestedTools: new[] { "testimox_rule_inventory" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect rule provenance",
                    suggestedTools: new[] { "testimox_source_query" }),
                ToolPackGuidance.FlowStep(
                    goal: "Map rules to vendor baselines",
                    suggestedTools: new[] { "testimox_baseline_crosswalk" }),
                ToolPackGuidance.FlowStep(
                    goal: "Discover available checks",
                    suggestedTools: new[] { "testimox_rules_list" }),
                ToolPackGuidance.FlowStep(
                    goal: "Execute focused diagnostics",
                    suggestedTools: new[] { "testimox_rules_run" }),
                ToolPackGuidance.FlowStep(
                    goal: "Correlate findings with other packs",
                    suggestedTools: new[] { "ad_search_facets", "system_info", "eventlog_live_stats" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "run_catalog",
                    summary: "List stored TestimoX runs from an allowed result store directory before selecting a run for deeper follow-up.",
                    primaryTools: new[] { "testimox_runs_list" }),
                ToolPackGuidance.Capability(
                    id: "run_summary",
                    summary: "Summarize a stored TestimoX run with score/penalty totals and preview stored rule outcomes.",
                    primaryTools: new[] { "testimox_run_summary" }),
                ToolPackGuidance.Capability(
                    id: "baseline_catalog",
                    summary: "List available vendor baselines and stable baseline ids before crosswalk, compare, or operator delta planning.",
                    primaryTools: new[] { "testimox_baselines_list" }),
                ToolPackGuidance.Capability(
                    id: "baseline_compare",
                    summary: "Compare vendor baselines for a product/version slice and surface desired/comparator/value-kind deltas.",
                    primaryTools: new[] { "testimox_baseline_compare" }),
                ToolPackGuidance.Capability(
                    id: "profile_catalog",
                    summary: "List curated TestimoX profiles with included/excluded assessment areas, categories, tags, and cost posture.",
                    primaryTools: new[] { "testimox_profiles_list" }),
                ToolPackGuidance.Capability(
                    id: "rule_inventory",
                    summary: "Inspect authoritative-vs-legacy rule inventory, migration state, hidden/superseded posture, and profile-based assessment areas.",
                    primaryTools: new[] { "testimox_rule_inventory" }),
                ToolPackGuidance.Capability(
                    id: "source_provenance",
                    summary: "Inspect focused rule provenance including enum mapping, required modules, supported systems, resources, and supersedence links.",
                    primaryTools: new[] { "testimox_source_query" }),
                ToolPackGuidance.Capability(
                    id: "baseline_crosswalk",
                    summary: "Map TestimoX rules to vendor baselines and external documentation references for operator reporting and delta planning.",
                    primaryTools: new[] { "testimox_baseline_crosswalk" }),
                ToolPackGuidance.Capability(
                    id: "rule_catalog",
                    summary: "Enumerate TestimoX rules with metadata (scope, source_type, origin, categories, tags, cost, visibility).",
                    primaryTools: new[] { "testimox_rules_list" }),
                ToolPackGuidance.Capability(
                    id: "rule_execution",
                    summary: "Run selected TestimoX rules using explicit names and/or selectors; return typed per-rule outcomes for downstream reasoning.",
                    primaryTools: new[] { "testimox_rules_run" }),
                ToolPackGuidance.Capability(
                    id: "cross_pack_handoff",
                    summary: "Reuse AD scope discovery and route TestimoX findings into AD/System/EventLog follow-up tools without re-discovery.",
                    primaryTools: new[] { "testimox_rules_run" })
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "ad_scope_to_testimox_execution_scope",
                    summary: "Promote AD discovery receipts into explicit TestimoX execution scope for deterministic rule targeting.",
                    entityKinds: new[] { "forest", "domain", "domain_controller" },
                    sourceTools: new[] { "ad_environment_discover", "ad_scope_discovery", "ad_forest_discover" },
                    targetTools: new[] { "testimox_rule_inventory", "testimox_rules_list", "testimox_rules_run" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("scope.domains[].dns_root", "include_domains", "Deduplicate discovered domains and keep canonical DNS roots."),
                        ToolPackGuidance.EntityFieldMapping("scope.domain_controllers[].hostname", "include_domain_controllers", "Prefer reachable DC hostnames/FQDNs from discovery receipts."),
                        ToolPackGuidance.EntityFieldMapping("scope.forest.root_domain", "include_domains", "Use forest root as default include_domains seed when explicit domain list is missing.")
                    },
                    notes: "Call testimox_rules_run after scope handoff to avoid broad scans and improve deterministic runtime."),
                ToolPackGuidance.EntityHandoff(
                    id: "testimox_findings_to_ad_system_eventlog_followup",
                    summary: "Route TestimoX finding entities into AD identity resolution and host telemetry deep-dive tools.",
                    entityKinds: new[] { "identity", "computer", "domain_controller", "policy" },
                    sourceTools: new[] { "testimox_rules_run", "testimox_run_summary" },
                    targetTools: new[] { "ad_object_resolve", "ad_search_facets", "system_security_options", "eventlog_live_stats" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("rules[].findings[].identity", "identity", "Resolve accounts/groups in AD before ownership and blast-radius analysis."),
                        ToolPackGuidance.EntityFieldMapping("rules[].findings[].computer", "computer_name", "Use host indicators for ComputerX/EventLog follow-up evidence collection."),
                        ToolPackGuidance.EntityFieldMapping("rules[].findings[].domain_controller", "computer_name", "Route DC-specific findings into host-level telemetry checks."),
                        ToolPackGuidance.EntityFieldMapping("rows[].domain_controller", "computer_name", "Use stored DC scope identifiers for host-level follow-up."),
                        ToolPackGuidance.EntityFieldMapping("rows[].domain", "domain_name", "Use stored domain identifiers to pivot back into AD-focused follow-up.")
                    },
                    notes: "Prefer ad_object_resolve first for identity normalization, then query host/event evidence.")
            },
            toolCatalog: ToolRegistryTestimoXExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve raw typed run payloads (rules and test outcomes). Do not rely only on *_view fields.",
            viewProjectionPolicy: "Projection arguments are view-only and intended for display shaping (columns/sort_by/sort_direction/top).",
            correlationGuidance: "Correlate rule outcomes by rule_name/category/scope with outputs from AD/System/EventLog packs.",
            setupHints: new {
                Enabled = Options.Enabled,
                AllowedStoreRootsCount = Options.AllowedStoreRoots.Count,
                MaxRulesInCatalog = Options.MaxRulesInCatalog,
                MaxRulesPerRun = Options.MaxRulesPerRun,
                DefaultConcurrency = Options.DefaultConcurrency,
                MaxConcurrency = Options.MaxConcurrency
            });

        var summary = ToolMarkdown.SummaryText(
            title: "TestimoX Pack",
            "Discover rules first, then execute a focused rule subset.",
            "Use raw run payload fields for reasoning and cross-pack correlation.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }
}
