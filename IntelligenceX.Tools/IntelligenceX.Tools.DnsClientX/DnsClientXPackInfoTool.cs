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

    private static readonly ToolDefinition DefinitionValue = new(
        "dnsclientx_pack_info",
        "Return DnsClientX pack capabilities, output contract, and recommended usage patterns.",
        ToolSchema.Object().NoAdditionalProperties(),
        category: "dns",
        tags: new[] {
            "pack:dnsclientx",
            "domain_family:public_domain",
            "domain_signals:dns,mx,spf,dmarc,dkim,ns,dnssec,caa,whois,mta_sts,bimi,dnsclientx,dns_client_x"
        },
        routing: new ToolRoutingContract {
            IsRoutingAware = true,
            PackId = "dnsclientx",
            DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
            DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic,
            DomainSignalTokens = new[] {
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
            }
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="DnsClientXPackInfoTool"/> class.
    /// </summary>
    public DnsClientXPackInfoTool(DnsClientXToolOptions options) : base(options) { }

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
            pack: "dnsclientx",
            engine: "DnsClientX",
            tools: ToolRegistryDnsClientXExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
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
            toolCatalog: ToolRegistryDnsClientXExtensions.GetRegisteredToolCatalog(Options),
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
}
