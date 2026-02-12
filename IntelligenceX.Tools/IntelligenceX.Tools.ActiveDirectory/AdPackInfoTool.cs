using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Returns Active Directory pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class AdPackInfoTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_pack_info",
        "Return Active Directory pack capabilities, output contract, and recommended usage patterns. Call this first when planning AD investigations.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPackInfoTool"/> class.
    /// </summary>
    public AdPackInfoTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var root = ToolPackGuidance.Create(
            pack: "active_directory",
            engine: "ADPlayground",
            tools: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolNames(Options),
            recommendedFlow: new[] {
                "Call ad_environment_discover first to learn effective domain_controller/search_base_dn and candidate DCs.",
                "Use ad_search/ad_groups_list/ad_spn_search for broad discovery.",
                "Use ad_object_resolve to avoid N+1 object lookups when correlating identities.",
                "Use ad_ldap_query_paged for large exploratory queries and continue with cursor.",
                "Use ad_search_facets/ad_replication_summary/ad_delegation_audit/ad_spn_stats for aggregated diagnostics.",
                "Use ad_monitoring_probe_catalog + ad_monitoring_probe_run for runtime DNS/LDAP/Kerberos/NTP/Replication health probes."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover candidate AD objects",
                    suggestedTools: new[] { "ad_search", "ad_groups_list", "ad_spn_search" }),
                ToolPackGuidance.FlowStep(
                    goal: "Resolve/expand identities for correlation",
                    suggestedTools: new[] { "ad_object_resolve", "ad_object_get", "ad_group_members_resolved" }),
                ToolPackGuidance.FlowStep(
                    goal: "Run diagnostics and aggregate analysis",
                    suggestedTools: new[] { "ad_search_facets", "ad_replication_summary", "ad_delegation_audit", "ad_spn_stats", "ad_ldap_diagnostics" }),
                ToolPackGuidance.FlowStep(
                    goal: "Run AD runtime monitoring probes",
                    suggestedTools: new[] { "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "directory_discovery",
                    summary: "Search and list AD users/groups/computers with optional dynamic attribute bags.",
                    primaryTools: new[] { "ad_search", "ad_groups_list", "ad_spn_search" }),
                ToolPackGuidance.Capability(
                    id: "identity_resolution",
                    summary: "Resolve identities and membership details for cross-tool correlation.",
                    primaryTools: new[] { "ad_object_resolve", "ad_object_get", "ad_group_members", "ad_group_members_resolved" }),
                ToolPackGuidance.Capability(
                    id: "ad_diagnostics",
                    summary: "Provide LDAP diagnostics and aggregated security/replication insights.",
                    primaryTools: new[] { "ad_ldap_diagnostics", "ad_search_facets", "ad_replication_summary", "ad_delegation_audit", "ad_spn_stats" }),
                ToolPackGuidance.Capability(
                    id: "ad_runtime_monitoring",
                    summary: "Run ADPlayground.Monitoring probes (ldap/dns/kerberos/ntp/replication) for server or domain scope.",
                    primaryTools: new[] { "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" })
            },
            toolCatalog: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolCatalog(Options),
            rawPayloadPolicy: "Preserve raw engine payloads (including dynamic LDAP attribute bags and nested objects).",
            viewProjectionPolicy: "Projection arguments are optional and view-only; they must not replace raw payload.",
            correlationGuidance: "Correlate users/groups/computers via raw payload fields across multiple AD tools.",
            setupHints: new {
                DomainController = Options.DomainController ?? string.Empty,
                SearchBaseDn = Options.DefaultSearchBaseDn ?? string.Empty,
                Note = "Use ad_environment_discover first to bootstrap context; provide domain_controller/search_base_dn only when discovery cannot reach your target."
            });

        var summary = ToolMarkdown.SummaryText(
            title: "Active Directory Pack",
            "Use raw payloads for reasoning/correlation; use `*_view` only for presentation.",
            "Prefer `ad_object_resolve` and paged queries to reduce repeated lookups.");

        return Task.FromResult(ToolResponse.OkModel(root, summaryMarkdown: summary));
    }
}
