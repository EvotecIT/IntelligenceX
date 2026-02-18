using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns available AD monitoring probe kinds and usage hints.
/// </summary>
public sealed class AdMonitoringProbeCatalogTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_probe_catalog",
        "List available AD monitoring probe kinds (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping) with scope and argument hints.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringProbeCatalogTool"/> class.
    /// </summary>
    public AdMonitoringProbeCatalogTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;

    /// <inheritdoc />
    protected override Task<string> InvokeCoreAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var model = new {
            ProbeKinds = new[] {
                new {
                    ProbeKind = "ldap",
                    Summary = "LDAP/LDAPS/GC bind + certificate + optional identity checks.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "domain_name", "targets", "domain_controller", "identity", "verify_certificate", "include_global_catalog", "discovery_fallback" }
                },
                new {
                    ProbeKind = "dns",
                    Summary = "DNS query validation against selected DNS servers.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "targets", "dns_queries", "domain_name", "protocol", "discovery_fallback" }
                },
                new {
                    ProbeKind = "kerberos",
                    Summary = "Credentialless Kerberos payload checks against KDCs.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "domain_name", "targets", "protocol", "split_protocol_results", "discovery_fallback" }
                },
                new {
                    ProbeKind = "ntp",
                    Summary = "NTP time-offset and delay checks.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "domain_name", "targets", "timeout_ms", "max_concurrency", "discovery_fallback" }
                },
                new {
                    ProbeKind = "replication",
                    Summary = "Replication topology/freshness checks with optional SYSVOL/port/ping diagnostics.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "domain_name", "domain_controller", "include_sysvol", "test_ports", "test_ping", "query_mode", "discovery_fallback" }
                },
                new {
                    ProbeKind = "port",
                    Summary = "TCP/UDP port checks for AD/DC targets.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "targets", "tcp_ports", "udp_ports", "include_udp", "use_ad_core_profile", "discovery_fallback" }
                },
                new {
                    ProbeKind = "https",
                    Summary = "HTTPS/TLS endpoint checks (URL/host targets + cert signal).",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "url", "targets", "verify_certificate", "port", "degraded_above_ms", "discovery_fallback" }
                },
                new {
                    ProbeKind = "dns_service",
                    Summary = "DNS service payload checks against DNS servers.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "targets", "dns_service_query_name", "dns_service_record_type", "protocol", "discovery_fallback" }
                },
                new {
                    ProbeKind = "adws",
                    Summary = "ADWS payload checks on domain controllers.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "targets", "port", "path", "bind_identity", "request_timeout_ms", "discovery_fallback" }
                },
                new {
                    ProbeKind = "directory",
                    Summary = "Directory health checks by sub-kind (root_dse/dns_registration/srv_coverage/fsmo/sysvol_gpt/netlogon_share/dns_soa/ldap_search/gc_readiness/client_path/rpc_endpoint/share_permissions).",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "directory_probe_kind", "targets", "domain_name", "directory_query_timeout_ms", "total_budget_ms", "discovery_fallback" }
                },
                new {
                    ProbeKind = "ping",
                    Summary = "ICMP reachability/latency checks with optional degradation thresholds.",
                    SupportsScopeDiscovery = true,
                    SupportsExplicitTargets = true,
                    KeyArguments = new[] { "targets", "latency_threshold_ms", "p95_latency_threshold_ms", "loss_threshold_percent", "discovery_fallback" }
                }
            },
            PreferredExecutionTool = "ad_monitoring_probe_run",
            Notes = new[] {
                "Use domain_controller for server-level checks when possible.",
                "Use domain_name or forest_name when running domain/forest-wide diagnostics.",
                "Use discovery_fallback=current_forest when you need forest-level discovery without explicit forest_name.",
                "Raw probe_result includes nested children and metadata for downstream correlation."
            }
        };

        var summary = ToolMarkdown.SummaryText(
            title: "AD Monitoring Probe Catalog",
            "Use `ad_monitoring_probe_run` with `probe_kind` set to one of: ldap, dns, kerberos, ntp, replication, port, https, dns_service, adws, directory, ping.",
            "Prefer `domain_controller` for single-server diagnostics, `domain_name` for domain-wide checks, and `discovery_fallback=current_forest` for forest-level discovery.");

        return Task.FromResult(ToolResponse.OkModel(model, summaryMarkdown: summary));
    }
}
