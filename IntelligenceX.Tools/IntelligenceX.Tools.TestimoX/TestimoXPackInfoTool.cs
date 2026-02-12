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
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "testimox",
            engine: "TestimoX",
            tools: ToolRegistryTestimoXExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Call testimox_rules_list to discover available rules and metadata (scope/categories/tags/cost).",
                "Select a focused subset of rules (prefer explicit names over broad all-rules execution).",
                "Call testimox_rules_run and correlate run output with AD/system/eventlog evidence."
            },
            flowSteps: new[] {
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
                    id: "rule_catalog",
                    summary: "Enumerate TestimoX rules with metadata (scope, categories, tags, cost, visibility).",
                    primaryTools: new[] { "testimox_rules_list" }),
                ToolPackGuidance.Capability(
                    id: "rule_execution",
                    summary: "Run selected TestimoX rules and return typed per-rule outcomes for downstream reasoning.",
                    primaryTools: new[] { "testimox_rules_run" })
            },
            toolCatalog: ToolRegistryTestimoXExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve raw typed run payloads (rules and test outcomes). Do not rely only on *_view fields.",
            viewProjectionPolicy: "Projection arguments are view-only and intended for display shaping (columns/sort_by/sort_direction/top).",
            correlationGuidance: "Correlate rule outcomes by rule_name/category/scope with outputs from AD/System/EventLog packs.",
            setupHints: new {
                Enabled = Options.Enabled,
                MaxRulesInCatalog = Options.MaxRulesInCatalog,
                MaxRulesPerRun = Options.MaxRulesPerRun,
                DefaultConcurrency = Options.DefaultConcurrency,
                MaxConcurrency = Options.MaxConcurrency
            });

        var summary = ToolMarkdown.SummaryText(
            title: "TestimoX Pack",
            "Discover rules first, then execute a focused rule subset.",
            "Use raw run payload fields for reasoning and cross-pack correlation.");

        return Task.FromResult(ToolResponse.OkModel(root, summaryMarkdown: summary));
    }
}
