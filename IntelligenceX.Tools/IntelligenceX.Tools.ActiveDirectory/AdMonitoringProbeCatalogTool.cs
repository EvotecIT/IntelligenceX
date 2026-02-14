using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

/// <summary>
/// Returns available AD monitoring probe kinds and usage hints.
/// </summary>
public sealed class AdMonitoringProbeCatalogTool : ActiveDirectoryToolBase, ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_probe_catalog",
        "List available AD monitoring probe kinds (ldap/dns/kerberos/ntp/replication) with scope and argument hints.",
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
            "Use `ad_monitoring_probe_run` with `probe_kind` set to one of: ldap, dns, kerberos, ntp, replication.",
            "Prefer `domain_controller` for single-server diagnostics, `domain_name` for domain-wide checks, and `discovery_fallback=current_forest` for forest-level discovery.");

        return Task.FromResult(ToolResponse.OkModel(model, summaryMarkdown: summary));
    }
}
