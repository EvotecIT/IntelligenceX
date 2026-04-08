using System;
using System.Collections.Generic;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.Dns;
using ADPlayground.Monitoring.Probes.Kerberos;
using ADPlayground.Monitoring.Probes.Port;
using ADPlayground.Monitoring.Probes.Replication;
using ADPlayground.Network;
using ADPlayground.Replication;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using MonitoringDnsProtocol = ADPlayground.Monitoring.Probes.Dns.DnsProtocol;

namespace IntelligenceX.Tools.ADPlayground;

public sealed partial class AdMonitoringProbeRunTool {
    private const int DefaultTimeoutMs = 5000;
    private const int MaxTimeoutMs = 300_000;
    private const int DefaultMaxConcurrency = 4;
    private const int MaxConcurrency = 128;
    private const int DefaultRetries = 0;
    private const int MaxRetries = 10;
    private const int DefaultRetryDelayMs = 250;
    private const int MaxRetryDelayMs = 10_000;
    private const int MaxViewTop = 5000;

    private static readonly string[] ProbeKinds = {
        "ldap",
        "dns",
        "kerberos",
        "ntp",
        "replication",
        "port",
        "https",
        "dns_service",
        "adws",
        "directory",
        "ping",
        "windows_update"
    };

    private static readonly string[] DirectoryProbeKinds = {
        "root_dse",
        "dns_registration",
        "srv_coverage",
        "fsmo",
        "sysvol_gpt",
        "netlogon_share",
        "dns_soa",
        "ldap_search",
        "gc_readiness",
        "client_path",
        "rpc_endpoint",
        "share_permissions"
    };

    private static readonly IReadOnlyDictionary<string, MonitoringDnsProtocol> DnsProtocols =
        new Dictionary<string, MonitoringDnsProtocol>(StringComparer.OrdinalIgnoreCase) {
            ["udp"] = MonitoringDnsProtocol.Udp,
            ["tcp"] = MonitoringDnsProtocol.Tcp,
            ["both"] = MonitoringDnsProtocol.Both
        };

    private static readonly IReadOnlyDictionary<string, KerberosTransport> KerberosProtocols =
        new Dictionary<string, KerberosTransport>(StringComparer.OrdinalIgnoreCase) {
            ["udp"] = KerberosTransport.Udp,
            ["tcp"] = KerberosTransport.Tcp,
            ["both"] = KerberosTransport.Both
        };

    private static readonly IReadOnlyDictionary<string, ReplicationQueryMode> ReplicationModes =
        new Dictionary<string, ReplicationQueryMode>(StringComparer.OrdinalIgnoreCase) {
            ["auto"] = ReplicationQueryMode.Auto,
            ["drsr"] = ReplicationQueryMode.Drsr,
            ["sda"] = ReplicationQueryMode.Sda
        };

    private static readonly IReadOnlyDictionary<string, PortIssueHandling> IssueHandlingModes =
        new Dictionary<string, PortIssueHandling>(StringComparer.OrdinalIgnoreCase) {
            ["ignore"] = PortIssueHandling.Ignore,
            ["degraded"] = PortIssueHandling.Degraded,
            ["down"] = PortIssueHandling.Down
        };

    private static readonly IReadOnlyDictionary<string, DirectoryDiscoveryFallback> DiscoveryFallbackModes =
        new Dictionary<string, DirectoryDiscoveryFallback>(StringComparer.OrdinalIgnoreCase) {
            ["none"] = DirectoryDiscoveryFallback.None,
            ["current_domain"] = DirectoryDiscoveryFallback.CurrentDomain,
            ["current-domain"] = DirectoryDiscoveryFallback.CurrentDomain,
            ["currentdomain"] = DirectoryDiscoveryFallback.CurrentDomain,
            ["current_forest"] = DirectoryDiscoveryFallback.CurrentForest,
            ["current-forest"] = DirectoryDiscoveryFallback.CurrentForest,
            ["currentforest"] = DirectoryDiscoveryFallback.CurrentForest
        };

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_probe_run",
        "Run an AD monitoring probe through ADPlayground.Monitoring (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping/windows_update) with optional domain/forest/DC scoping.",
        ToolSchema.Object(
                ("probe_kind", ToolSchema.String("Probe kind to execute.").Enum("ldap", "dns", "kerberos", "ntp", "replication", "port", "https", "dns_service", "adws", "directory", "ping", "windows_update")),
                ("directory_probe_kind", ToolSchema.String("Directory probe kind (required when probe_kind=directory).").Enum("root_dse", "dns_registration", "srv_coverage", "fsmo", "sysvol_gpt", "netlogon_share", "dns_soa", "ldap_search", "gc_readiness", "client_path", "rpc_endpoint", "share_permissions")),
                ("name", ToolSchema.String("Optional probe execution name. If omitted, a generated name is used.")),
                ("targets", ToolSchema.Array(ToolSchema.String(), "Optional explicit target hosts. When omitted, AD discovery can be used via domain/forest/include filters.")),
                ("url", ToolSchema.String("HTTPS only: optional endpoint URL/host/host:port.")),
                ("domain_controller", ToolSchema.String("Optional single DC host shortcut. If set and targets are omitted, this DC is used as target.")),
                ("domain_name", ToolSchema.String("Optional DNS domain scope used for discovery and probe defaults.")),
                ("forest_name", ToolSchema.String("Optional forest scope used for discovery.")),
                ("include_domains", ToolSchema.Array(ToolSchema.String(), "Optional include-domain filter for discovery.")),
                ("exclude_domains", ToolSchema.Array(ToolSchema.String(), "Optional exclude-domain filter for discovery.")),
                ("include_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional include-DC filter for discovery.")),
                ("exclude_domain_controllers", ToolSchema.Array(ToolSchema.String(), "Optional exclude-DC filter for discovery.")),
                ("skip_rodc", ToolSchema.Boolean("When true, excludes RODCs from discovered targets.")),
                ("include_trusts", ToolSchema.Boolean("When true, includes trusted domains in discovery.")),
                ("discovery_fallback",
                    ToolSchema.String("Fallback discovery policy when no explicit targets/domain/forest are provided.")
                        .Enum("none", "current_domain", "current_forest")),
                ("timeout_ms", ToolSchema.Integer("Probe timeout in milliseconds. Default 5000.")),
                ("retries", ToolSchema.Integer("Retry count. Default 0.")),
                ("retry_delay_ms", ToolSchema.Integer("Retry delay in milliseconds. Default 250.")),
                ("max_concurrency", ToolSchema.Integer("Maximum target concurrency. Default 4.")),
                ("protocol", ToolSchema.String("Transport/profile for applicable probes. dns/kerberos/dns_service: udp/tcp/both.")),
                ("split_protocol_results", ToolSchema.Boolean("When true (where supported), returns separate protocol child rows.")),
                ("dns_queries", ToolSchema.Array(
                    ToolSchema.Object(
                            ("name", ToolSchema.String("DNS query name/FQDN.")),
                            ("type", ToolSchema.String("DNS type (A, AAAA, SRV, CNAME, PTR, TXT, MX, NS, SOA, etc.).")))
                        .Required("name", "type")
                        .NoAdditionalProperties(),
                    "Optional DNS queries; if omitted and domain_name is available, defaults are derived.")),
                ("verify_certificate", ToolSchema.Boolean("LDAP only: verify LDAPS certificates. Default true.")),
                ("include_global_catalog", ToolSchema.Boolean("LDAP only: include GC ports 3268/3269. Default true.")),
                ("include_facts", ToolSchema.Boolean("LDAP only: include quickly retrievable DC facts. Default true.")),
                ("identity", ToolSchema.String("LDAP only: optional identity to validate (samAccountName/UPN/DN/GUID/SID).")),
                ("stale_threshold_hours", ToolSchema.Integer("Replication only: stale threshold in hours. Default 12.")),
                ("require_wsus", ToolSchema.Boolean("Windows Update probe only: require WSUS management signal. Default true.")),
                ("include_sysvol", ToolSchema.Boolean("Replication only: include SYSVOL/DFSR snapshot.")),
                ("test_sysvol_shares", ToolSchema.Boolean("Replication only: validate SYSVOL/NETLOGON share access.")),
                ("test_ports", ToolSchema.Boolean("Replication only: include TCP connectivity precheck details.")),
                ("test_ping", ToolSchema.Boolean("Replication only: include ICMP ping check details.")),
                ("query_mode", ToolSchema.String("Replication only: data source mode.").Enum("auto", "drsr", "sda")),
                ("include_children", ToolSchema.Boolean("When false, omits nested child results in raw probe_result while keeping parent status.")),
                ("port", ToolSchema.Integer("Optional port override for adws/https/directory checks.")),
                ("path", ToolSchema.String("ADWS only: endpoint path override.")),
                ("request_timeout_ms", ToolSchema.Integer("ADWS only: request timeout override (ms).")),
                ("bind_identity", ToolSchema.String("ADWS and directory LDAP checks: optional bind identity.")),
                ("bind_secret", ToolSchema.String("ADWS and directory LDAP checks: optional bind secret.")),
                ("tcp_ports", ToolSchema.Array(ToolSchema.Integer(), "Port probe only: explicit TCP ports.")),
                ("udp_ports", ToolSchema.Array(ToolSchema.Integer(), "Port probe only: explicit UDP ports.")),
                ("use_ad_core_profile", ToolSchema.Boolean("Port probe only: use built-in AD TCP profile when tcp_ports is empty. Default true.")),
                ("include_udp", ToolSchema.Boolean("Port probe only: include UDP checks. Default false.")),
                ("dns_service_query_name", ToolSchema.String("DNS service probe only: query name template.")),
                ("dns_service_record_type", ToolSchema.String("DNS service probe only: query record type. Default SRV.")),
                ("dns_service_require_answers", ToolSchema.Boolean("DNS service probe only: require answers in response. Default true.")),
                ("latency_threshold_ms", ToolSchema.Integer("Ping probe only: latency threshold for degraded status.")),
                ("p95_latency_threshold_ms", ToolSchema.Integer("Ping probe only: p95 latency threshold for degraded status.")),
                ("loss_threshold_percent", ToolSchema.Integer("Ping probe only: loss threshold percent for degraded status (0-100).")),
                ("degraded_above_ms", ToolSchema.Integer("HTTPS/directory checks: degraded threshold in milliseconds.")),
                ("directory_search_base", ToolSchema.String("Directory LDAP search probe: search base DN.")),
                ("directory_filter", ToolSchema.String("Directory LDAP search probe: LDAP filter.")),
                ("directory_attribute", ToolSchema.String("Directory LDAP search probe: attribute to fetch.")),
                ("directory_use_ldaps", ToolSchema.Boolean("Directory LDAP/client-path checks: use LDAPS.")),
                ("directory_use_start_tls", ToolSchema.Boolean("Directory LDAP search check: use StartTLS.")),
                ("directory_use_anonymous_bind", ToolSchema.Boolean("Directory LDAP-related checks: use anonymous bind first.")),
                ("directory_allow_authenticated_fallback", ToolSchema.Boolean("Directory LDAP-related checks: allow fallback to authenticated bind. Default true.")),
                ("directory_require_global_catalog_ready", ToolSchema.Boolean("Directory root_dse/gc_readiness checks: require GC ready.")),
                ("directory_require_synchronized", ToolSchema.Boolean("Directory root_dse/gc_readiness checks: require synchronized flag.")),
                ("directory_include_forest_roles", ToolSchema.Boolean("Directory fsmo check: include forest roles. Default true.")),
                ("directory_share_name", ToolSchema.String("Directory netlogon_share check: share name override.")),
                ("directory_required_shares", ToolSchema.Array(ToolSchema.String(), "Directory netlogon/share_permissions checks: required shares.")),
                ("directory_allowed_shares", ToolSchema.Array(ToolSchema.String(), "Directory netlogon/share_permissions checks: allowed shares.")),
                ("directory_optional_shares", ToolSchema.Array(ToolSchema.String(), "Directory share_permissions check: optional shares.")),
                ("directory_ignore_drive_shares", ToolSchema.Boolean("Directory netlogon_share check: ignore drive shares. Default true.")),
                ("directory_query_name", ToolSchema.String("Directory DNS/client-path/gc checks: query template override.")),
                ("directory_dns_servers", ToolSchema.Array(ToolSchema.String(), "Directory DNS-related checks: explicit DNS servers.")),
                ("directory_use_all_dns_servers", ToolSchema.Boolean("Directory DNS-related checks: query all configured DNS servers.")),
                ("directory_sites", ToolSchema.Array(ToolSchema.String(), "Directory srv_coverage/client_path checks: include sites.")),
                ("directory_exclude_sites", ToolSchema.Array(ToolSchema.String(), "Directory srv_coverage/client_path checks: exclude sites.")),
                ("directory_zones", ToolSchema.Array(ToolSchema.String(), "Directory dns_soa check: explicit zones.")),
                ("directory_query_timeout_ms", ToolSchema.Integer("Directory checks: query timeout override in milliseconds.")),
                ("total_budget_ms", ToolSchema.Integer("Directory checks: optional total time budget in milliseconds.")))
            .WithTableViewOptions(
                columnsDescription: "Optional columns projected from flattened probe rows (parent + children + metadata).",
                sortByDescription: "Optional sort column for flattened probe rows.",
                topDescription: "Optional top-N limit for flattened probe rows.")
            .Required("probe_kind")
            .NoAdditionalProperties(),
        category: "active_directory",
        tags: new[] {
            "pack:active_directory",
            "intent:monitoring_probe",
            "intent:replication_probe",
            "intent:replikacja",
            "intent:diagnostyka_replikacji",
            "scope:forest",
            "scope:domain_controller"
        },
        aliases: new[] {
            new ToolAliasDefinition("ad_replication_probe_run", "Run a replication-focused AD monitoring probe."),
            new ToolAliasDefinition("ad_replikacja_probe", "Uruchom probe replikacji Active Directory."),
            new ToolAliasDefinition("ad_replikacja_diagnostyka", "Uruchom diagnostyke replikacji AD dla wskazanego scope.")
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringProbeRunTool"/> class.
    /// </summary>
    public AdMonitoringProbeRunTool(ActiveDirectoryToolOptions options) : base(options) { }

    /// <inheritdoc />
    public override ToolDefinition Definition => DefinitionValue;
}
