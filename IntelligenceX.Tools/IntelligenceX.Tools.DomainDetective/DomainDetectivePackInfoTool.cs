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
    private sealed record PackInfoRequest;
    private readonly PackInfoAdapter _adapter;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "domaindetective_pack_info",
        description: "Return DomainDetective pack capabilities, output contract, and recommended usage patterns.",
        packId: "domaindetective",
        category: "dns",
        tags: new[] {
            "pack:domaindetective",
            "domain_family:public_domain",
            "domain_signals:dns,mx,spf,dmarc,dkim,ns,dnssec,caa,whois,mta_sts,bimi,domaindetective,domain_detective"
        },
        domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyPublic,
        domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdPublic,
        domainSignalTokens: new[] {
            "dns",
            "mx",
            "spf",
            "dmarc",
            "dkim",
            "ns",
            "dnssec",
            "caa",
            "whois",
            "mta_sts",
            "bimi",
            "domaindetective",
            "domain_detective"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="DomainDetectivePackInfoTool"/> class.
    /// </summary>
    public DomainDetectivePackInfoTool(DomainDetectiveToolOptions options) : base(options) {
        _adapter = new PackInfoAdapter(this);
    }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        return RunPipelineAsync(
            arguments: arguments,
            cancellationToken: cancellationToken,
            adapter: _adapter);
    }

    private Task<string> BuildPackInfoResponseAsync(ToolPipelineContext<PackInfoRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "domaindetective",
            engine: "DomainDetective",
            tools: ToolRegistryDomainDetectiveExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use domaindetective_checks_catalog first when you need canonical check names or alias normalization guidance.",
                "Use domaindetective_domain_summary for broad domain posture checks (DNS/email/security).",
                "Use domaindetective_network_probe for host-level ping/traceroute diagnostics.",
                "When you need resolver-specific evidence, pair with dnsclientx_query.",
                "When the request is AD directory-scoped (domains/DCs/LDAP), route to AD pack tools instead of DomainDetective."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover supported check names and aliases",
                    suggestedTools: new[] { "domaindetective_checks_catalog" },
                    notes: "Use catalog output to build valid checks[] sets before running summaries."),
                ToolPackGuidance.FlowStep(
                    goal: "Run bounded domain posture checks",
                    suggestedTools: new[] { "domaindetective_domain_summary" },
                    notes: "Prefer a focused checks[] set and explicit timeout_ms for deterministic runs."),
                ToolPackGuidance.FlowStep(
                    goal: "Run bounded host reachability diagnostics",
                    suggestedTools: new[] { "domaindetective_network_probe" },
                    notes: "Use ping first, then enable traceroute when path diagnostics are needed."),
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
                    id: "check_catalog_discovery",
                    summary: "List supported check names, baseline defaults, and alias normalization guidance for deterministic checks[] selection.",
                    primaryTools: new[] { "domaindetective_checks_catalog" }),
                ToolPackGuidance.Capability(
                    id: "domain_posture_summary",
                    summary: "Run selected DomainDetective health checks and return condensed DNS/email/security posture summary.",
                    primaryTools: new[] { "domaindetective_domain_summary" }),
                ToolPackGuidance.Capability(
                    id: "network_reachability_probe",
                    summary: "Run host-level ping and optional traceroute diagnostics for fast path validation.",
                    primaryTools: new[] { "domaindetective_network_probe" }),
                ToolPackGuidance.Capability(
                    id: "domain_vs_ad_context_separation",
                    summary: "Keep public-domain diagnostics separate from AD directory operations to reduce routing ambiguity.",
                    primaryTools: new[] { "domaindetective_pack_info", "domaindetective_domain_summary" })
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "domain_context_to_ad_scope",
                    summary: "Route AD-directory scoped domain investigations to AD tools after domain posture triage.",
                    entityKinds: new[] { "domain", "dns", "host" },
                    sourceTools: new[] { "domaindetective_domain_summary", "domaindetective_network_probe", "dnsclientx_query" },
                    targetTools: new[] { "ad_scope_discovery", "ad_directory_discovery_diagnostics", "ad_object_resolve", "ad_search", "ad_object_get" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("domain", "domain_name", "Use normalized domain name when AD scope is directory-focused."),
                        ToolPackGuidance.EntityFieldMapping("summary.domain", "domain_name", "Prefer summary domain output when present."),
                        ToolPackGuidance.EntityFieldMapping("host", "domain_controller", "Use host/FQDN as domain controller hint when AD follow-up is requested.")
                    },
                    notes: "When user intent targets LDAP/DC/replication/GPO workflows, switch from DomainDetective to AD pack execution.")
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

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }

    private sealed class PackInfoAdapter : ToolRequestAdapter<PackInfoRequest> {
        private readonly DomainDetectivePackInfoTool _tool;

        public PackInfoAdapter(DomainDetectivePackInfoTool tool) {
            _tool = tool;
        }

        public override ToolRequestBindingResult<PackInfoRequest> Bind(JsonObject? arguments) {
            _ = arguments;
            return ToolRequestBindingResult<PackInfoRequest>.Success(new PackInfoRequest());
        }

        public override Task<string> ExecuteAsync(
            ToolPipelineContext<PackInfoRequest> context,
            CancellationToken cancellationToken) {
            return _tool.BuildPackInfoResponseAsync(context, cancellationToken);
        }
    }
}
