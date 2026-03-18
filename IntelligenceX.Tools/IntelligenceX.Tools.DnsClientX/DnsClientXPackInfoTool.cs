using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// Returns DnsClientX pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class DnsClientXPackInfoTool : DnsClientXToolBase, ITool {
    private sealed record PackInfoRequest;
    private readonly PackInfoAdapter _adapter;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "dnsclientx_pack_info",
        description: "Return DnsClientX pack capabilities, output contract, and recommended usage patterns.",
        packId: "dnsclientx",
        category: "dns",
        tags: new[] {
            "pack:dnsclientx",
            "domain_family:public_domain",
            "domain_signals:dns,mx,spf,dmarc,dkim,ns,dnssec,caa,whois,mta_sts,bimi,dnsclientx,dns_client_x"
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
            "dnsclientx",
            "dns_client_x"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClientXPackInfoTool"/> class.
    /// </summary>
    public DnsClientXPackInfoTool(DnsClientXToolOptions options) : base(options) {
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
            pack: "dnsclientx",
            engine: "DnsClientX",
            tools: ToolRegistryDnsClientXExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Use dnsclientx_ping first when endpoint reachability is uncertain so DNS timeouts are not misread as resolver-only failures.",
                "Use dnsclientx_query to verify DNS records directly with explicit endpoint and record type.",
                "Use dnsclientx_ping for a quick ICMP reachability baseline before deeper diagnostics.",
                "When a domain-level security diagnosis is needed, hand off to domaindetective_domain_summary.",
                "Reason from raw payload fields; use summary_markdown only as a short operator preview."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Collect resolver-level DNS evidence",
                    suggestedTools: new[] { "dnsclientx_query" },
                    notes: "Prefer explicit record type + endpoint when troubleshooting resolver disagreements."),
                ToolPackGuidance.FlowStep(
                    goal: "Check host-level reachability",
                    suggestedTools: new[] { "dnsclientx_ping" },
                    notes: "Use for quick connectivity signals before deeper protocol-specific checks."),
                ToolPackGuidance.FlowStep(
                    goal: "Escalate to domain posture checks",
                    suggestedTools: new[] { "domaindetective_domain_summary" },
                    notes: "Use DomainDetective pack for broader domain health/security posture.")
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "dns_record_query",
                    summary: "Query DNS records using open-source DnsClientX with endpoint and timeout controls.",
                    primaryTools: new[] { "dnsclientx_query" }),
                ToolPackGuidance.Capability(
                    id: "network_reachability_probe",
                    summary: "Run bounded ICMP ping probes to validate host reachability and latency.",
                    primaryTools: new[] { "dnsclientx_ping" })
            },
            recipes: new[] {
                ToolPackGuidance.Recipe(
                    id: "resolver_disagreement_triage",
                    summary: "Validate reachability first, then collect resolver-specific DNS evidence with explicit endpoint and record type controls.",
                    whenToUse: "Use when the request mentions DNS disagreement, timeout ambiguity, or a specific resolver/server that may disagree with public results.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Confirm resolver host reachability",
                            suggestedTools: new[] { "dnsclientx_ping" },
                            notes: "Ping the resolver endpoint or target host first so transport problems do not get mistaken for record-level DNS failures."),
                        ToolPackGuidance.FlowStep(
                            goal: "Collect explicit resolver DNS evidence",
                            suggestedTools: new[] { "dnsclientx_query" },
                            notes: "Specify name, type, and endpoint when comparing different resolvers or validating an override."),
                        ToolPackGuidance.FlowStep(
                            goal: "Escalate into broader domain posture if needed",
                            suggestedTools: new[] { "domaindetective_domain_summary" },
                            notes: "Use DomainDetective once the DNS evidence suggests a broader posture or policy problem.")
                    },
                    verificationTools: new[] { "dnsclientx_query", "domaindetective_domain_summary" }),
                ToolPackGuidance.Recipe(
                    id: "host_reachability_then_dns_followup",
                    summary: "Establish host reachability before pivoting into resolver-specific DNS queries for the same target.",
                    whenToUse: "Use when a hostname or server is mentioned first and the next step is deciding whether the issue is network reachability or DNS behavior.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Collect bounded reachability evidence",
                            suggestedTools: new[] { "dnsclientx_ping" }),
                        ToolPackGuidance.FlowStep(
                            goal: "Run the DNS query once the target is confirmed reachable",
                            suggestedTools: new[] { "dnsclientx_query" },
                            notes: "Use explicit timeout and endpoint values when the same target may be tested through multiple resolvers.")
                    },
                    verificationTools: new[] { "dnsclientx_ping", "dnsclientx_query" })
            },
            toolCatalog: ToolRegistryDnsClientXExtensions.GetRegisteredToolCatalog(Options),
            runtimeCapabilities: new ToolPackRuntimeCapabilitiesModel {
                PreferredEntryTools = new[] { "dnsclientx_query" },
                PreferredProbeTools = new[] { "dnsclientx_ping" },
                ProbeHelperFreshnessWindowSeconds = 300,
                SetupHelperFreshnessWindowSeconds = 900,
                RecipeHelperFreshnessWindowSeconds = 300,
                RuntimePrerequisites = new[] {
                    "Use dnsclientx_ping when host reachability or resolver endpoint access is uncertain before treating a DNS timeout as a resolver-only failure.",
                    "Pass endpoint when you need deterministic resolver comparison instead of relying on ambient DNS resolution.",
                    "Use domaindetective_domain_summary when DNS evidence needs broader public-domain posture context rather than more raw resolver retries."
                },
                Notes = "Prefer dnsclientx_ping before deeper query retries when network path uncertainty is still unresolved, then use dnsclientx_query with explicit name/type/endpoint for deterministic DNS evidence."
            },
            rawPayloadPolicy: "Preserve raw DNS sections (answers/authority/additional) for reasoning and evidence correlation.",
            viewProjectionPolicy: "Projection arguments are optional and view-only. This pack currently returns direct evidence payloads.",
            setupHints: new {
                Dependency = "DnsClientX",
                Note = "If DnsClientX is missing in the current build, tools return dependency_unavailable."
            },
            limits: new {
                Options.MaxAnswersPerSection,
                Options.DefaultTimeoutMs,
                Options.MaxTimeoutMs,
                Options.MaxRetries,
                Options.MaxPingTargets,
                Options.DefaultPingTimeoutMs,
                Options.MaxPingTimeoutMs
            });

        var summary = ToolMarkdown.SummaryText(
            title: "DnsClientX Pack",
            "Use `dnsclientx_query` for DNS evidence and `dnsclientx_ping` for quick reachability baselines.",
            "Use `domaindetective_domain_summary` when you need broader domain posture checks.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }

    private sealed class PackInfoAdapter : ToolRequestAdapter<PackInfoRequest> {
        private readonly DnsClientXPackInfoTool _tool;

        public PackInfoAdapter(DnsClientXPackInfoTool tool) {
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
