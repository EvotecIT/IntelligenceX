using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Returns DomainDetective pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class DomainDetectivePackInfoTool : DomainDetectiveToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "domaindetective_pack_info",
        "Return DomainDetective pack capabilities, output contract, and recommended usage patterns.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectivePackInfoTool"/> class.
    /// </summary>
    public DomainDetectivePackInfoTool(DomainDetectiveToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "domaindetective",
            engine: "DomainDetective",
            tools: ToolRegistryDomainDetectiveExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use domaindetective_domain_summary for broad domain posture checks (DNS/email/security).",
                "When you need resolver-specific evidence, pair with dnsclientx_query.",
                "When the request is AD directory-scoped (domains/DCs/LDAP), route to AD pack tools instead of DomainDetective."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Run bounded domain posture checks",
                    suggestedTools: new[] { "domaindetective_domain_summary" },
                    notes: "Prefer a focused checks[] set and explicit timeout_ms for deterministic runs."),
                ToolPackGuidance.FlowStep(
                    goal: "Validate resolver-specific discrepancies",
                    suggestedTools: new[] { "dnsclientx_query" },
                    notes: "Use explicit endpoint + record type when comparing resolver behavior."),
                ToolPackGuidance.FlowStep(
                    goal: "Escalate to AD directory diagnostics when needed",
                    suggestedTools: new[] { "ad_scope_discovery", "ad_directory_discovery_diagnostics" },
                    notes: "Use AD pack for Active Directory topology/context; DomainDetective is internet/domain posture focused.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "domain_posture_summary",
                    summary: "Run selected DomainDetective health checks and return condensed DNS/email/security posture summary.",
                    primaryTools: new[] { "domaindetective_domain_summary" }),
                ToolPackGuidance.Capability(
                    id: "domain_vs_ad_context_separation",
                    summary: "Keep public-domain diagnostics separate from AD directory operations to reduce routing ambiguity.",
                    primaryTools: new[] { "domaindetective_pack_info", "domaindetective_domain_summary" })
            },
            toolCatalog: ToolRegistryDomainDetectiveExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Keep summary + analysis availability fields as authoritative evidence for domain posture reasoning.",
            viewProjectionPolicy: "Projection arguments are optional and view-only. This pack currently returns direct evidence payloads.",
            setupHints: new {
                Dependency = "DomainDetective",
                Note = "If DomainDetective is missing in the current build, tools return dependency_unavailable."
            },
            limits: new {
                Options.DefaultTimeoutMs,
                Options.MaxTimeoutMs,
                Options.MaxChecks,
                Options.MaxHints
            });

        var summary = ToolMarkdown.SummaryText(
            title: "DomainDetective Pack",
            "Use this pack for internet/domain posture diagnostics.",
            "Use AD pack tools for directory-specific domain controller and LDAP workflows.");

        return Task.FromResult(ToolResponse.OkModel(root, summaryMarkdown: summary));
    }
}

