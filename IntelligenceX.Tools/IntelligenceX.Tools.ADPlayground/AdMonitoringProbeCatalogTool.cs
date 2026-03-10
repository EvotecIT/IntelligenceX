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
    private sealed record MonitoringProbeCatalogRequest;

    private static readonly ToolDefinition DefinitionValue = new(
        "ad_monitoring_probe_catalog",
        "List available AD monitoring probe kinds (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping/windows_update) with scope and argument hints.",
        ToolSchema.Object().NoAdditionalProperties());

    /// <summary>
    /// Initializes a new instance of the <see cref="AdMonitoringProbeCatalogTool"/> class.
    /// </summary>
    public AdMonitoringProbeCatalogTool(ActiveDirectoryToolOptions options) : base(options) { }

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

    private static ToolRequestBindingResult<MonitoringProbeCatalogRequest> BindRequest(JsonObject? arguments) {
        _ = arguments;
        return ToolRequestBindingResult<MonitoringProbeCatalogRequest>.Success(new MonitoringProbeCatalogRequest());
    }

    private Task<string> ExecuteAsync(ToolPipelineContext<MonitoringProbeCatalogRequest> context, CancellationToken cancellationToken) {
        _ = context;
        cancellationToken.ThrowIfCancellationRequested();

        var model = new {
            ProbeKinds = new object[] {
                CreateProbeKind(
                    probeKind: "ldap",
                    summary: "LDAP/LDAPS/GC bind + certificate + optional identity checks.",
                    keyArguments: new[] { "domain_name", "targets", "domain_controller", "identity", "verify_certificate", "include_global_catalog", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_ldap_policy_posture", "system_info", "system_metrics_summary" },
                    followUpProfiles: CreateLdapFollowUpProfiles(),
                    resultSignalProfiles: CreateLdapResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "dns",
                    summary: "DNS query validation against selected DNS servers.",
                    keyArguments: new[] { "targets", "dns_queries", "domain_name", "protocol", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_network_client_posture", "system_info", "system_network_adapters" },
                    followUpProfiles: CreateDnsFollowUpProfiles(),
                    resultSignalProfiles: CreateDnsResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "kerberos",
                    summary: "Credentialless Kerberos payload checks against KDCs.",
                    keyArguments: new[] { "domain_name", "targets", "protocol", "split_protocol_results", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_time_sync", "system_info" },
                    followUpProfiles: CreateKerberosFollowUpProfiles(),
                    resultSignalProfiles: CreateKerberosResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "ntp",
                    summary: "NTP time-offset and delay checks.",
                    keyArguments: new[] { "domain_name", "targets", "timeout_ms", "max_concurrency", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_time_sync", "system_info" },
                    followUpProfiles: CreateNtpFollowUpProfiles(),
                    resultSignalProfiles: CreateNtpResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "replication",
                    summary: "Replication topology/freshness checks with optional SYSVOL/port/ping diagnostics.",
                    keyArguments: new[] { "domain_name", "domain_controller", "include_sysvol", "test_ports", "test_ping", "query_mode", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_info", "system_metrics_summary", "system_logical_disks_list" },
                    followUpProfiles: CreateReplicationFollowUpProfiles(),
                    resultSignalProfiles: CreateReplicationResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "port",
                    summary: "TCP/UDP port checks for AD/DC targets.",
                    keyArguments: new[] { "targets", "tcp_ports", "udp_ports", "include_udp", "use_ad_core_profile", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_ports_list", "system_process_list" },
                    followUpProfiles: CreatePortFollowUpProfiles(),
                    resultSignalProfiles: CreatePortResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "https",
                    summary: "HTTPS/TLS endpoint checks (URL/host targets + cert signal).",
                    keyArguments: new[] { "url", "targets", "verify_certificate", "port", "degraded_above_ms", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_certificate_posture", "system_tls_posture", "system_info" },
                    followUpProfiles: CreateHttpsFollowUpProfiles(),
                    resultSignalProfiles: CreateHttpsResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "dns_service",
                    summary: "DNS service payload checks against DNS servers.",
                    keyArguments: new[] { "targets", "dns_service_query_name", "dns_service_record_type", "protocol", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_info", "system_network_adapters" },
                    followUpProfiles: CreateDnsServiceFollowUpProfiles(),
                    resultSignalProfiles: CreateDnsServiceResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "adws",
                    summary: "ADWS payload checks on domain controllers.",
                    keyArguments: new[] { "targets", "port", "path", "bind_identity", "request_timeout_ms", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_ports_list", "system_info" },
                    followUpProfiles: CreateAdwsFollowUpProfiles(),
                    resultSignalProfiles: CreateAdwsResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "directory",
                    summary: "Directory health checks by sub-kind (root_dse/dns_registration/srv_coverage/fsmo/sysvol_gpt/netlogon_share/dns_soa/ldap_search/gc_readiness/client_path/rpc_endpoint/share_permissions).",
                    keyArguments: new[] { "directory_probe_kind", "targets", "domain_name", "directory_query_timeout_ms", "total_budget_ms", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_info", "system_logical_disks_list", "system_service_list", "ad_ldap_diagnostics" },
                    directoryProbeSubKinds: CreateDirectoryProbeSubKinds()),
                CreateProbeKind(
                    probeKind: "ping",
                    summary: "ICMP reachability/latency checks with optional degradation thresholds.",
                    keyArguments: new[] { "targets", "latency_threshold_ms", "p95_latency_threshold_ms", "loss_threshold_percent", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_info", "system_metrics_summary" },
                    followUpProfiles: CreatePingFollowUpProfiles(),
                    resultSignalProfiles: CreatePingResultSignalProfiles()),
                CreateProbeKind(
                    probeKind: "windows_update",
                    summary: "Windows Update / WSUS client telemetry checks for selected hosts or discovered AD scope.",
                    keyArguments: new[] { "domain_name", "targets", "domain_controller", "require_wsus", "max_concurrency", "discovery_fallback" },
                    preferredFollowUpTools: new[] { "system_windows_update_client_status", "system_windows_update_telemetry", "system_updates_installed", "system_patch_compliance", "system_info" },
                    followUpProfiles: CreateWindowsUpdateFollowUpProfiles(),
                    resultSignalProfiles: CreateWindowsUpdateResultSignalProfiles())
            },
            PreferredExecutionTool = "ad_monitoring_probe_run",
            PreferredStateTools = new[] {
                "ad_monitoring_service_heartbeat_get",
                "ad_monitoring_diagnostics_get",
                "ad_monitoring_metrics_get",
                "ad_monitoring_dashboard_state_get"
            },
            Notes = new[] {
                "Use domain_controller for server-level checks when possible.",
                "Use domain_name or forest_name when running domain/forest-wide diagnostics.",
                "Use discovery_fallback=current_forest when you need forest-level discovery without explicit forest_name.",
                "Raw probe_result includes nested children and metadata for downstream correlation.",
                "LDAP probe results already include LDAPS certificate signal; if the user asks for LDAP certificates or LDAPS certificate details, stay on the LDAP probe/diagnostic path.",
                "For probe_kind=directory, inspect directory_probe_subkinds and use the matching preferred_follow_up_tools for the selected directory_probe_kind before generic host pivots.",
                "For ldap, dns, kerberos, ntp, replication, ping, windows_update, https, port, and adws, inspect follow_up_profiles when the user asks for the likely next diagnostic step rather than relying on only the top-level preferred_follow_up_tools list.",
                "For ldap, dns, kerberos, ntp, replication, ping, windows_update, https, port, and adws, inspect result_signal_profiles when probe output points to a specific failure shape such as skew, missing answers, TLS failures, packet loss, patch drift, endpoint reachability, stale neighbors, or SYSVOL/share failures.",
                "Use each probe kind's PreferredFollowUpTools list as the first cross-pack pivot hint before free-form reasoning.",
                "Use system_certificate_posture only when the follow-up is explicitly about machine certificate stores or trust-store posture on the same host.",
                "Use the ad_monitoring_*_get tools when you need persisted monitoring-service state instead of running fresh probes.",
                "When probe results identify concrete domain controllers or hosts, pivot into remote system_* tools with computer_name for follow-up host diagnostics."
            }
        };

        var summary = ToolMarkdown.SummaryText(
            title: "AD Monitoring Probe Catalog",
            "Use `ad_monitoring_probe_run` with `probe_kind` set to one of: ldap, dns, kerberos, ntp, replication, port, https, dns_service, adws, directory, ping, windows_update.",
            "Prefer `domain_controller` for single-server diagnostics, `domain_name` for domain-wide checks, and `discovery_fallback=current_forest` for forest-level discovery.",
            "LDAP probe rows already carry LDAPS certificate signal; for LDAP certificate questions, prefer the LDAP probe or `ad_ldap_diagnostics` again rather than a general host certificate-store query.",
            "For persisted monitoring-service health, use `ad_monitoring_service_heartbeat_get`, `ad_monitoring_diagnostics_get`, `ad_monitoring_metrics_get`, or `ad_monitoring_dashboard_state_get`.");

        return Task.FromResult(ToolResultV2.OkModel(model, summaryMarkdown: summary));
    }

    private static object CreateProbeKind(
        string probeKind,
        string summary,
        string[] keyArguments,
        string[] preferredFollowUpTools,
        object[]? directoryProbeSubKinds = null,
        object[]? followUpProfiles = null,
        object[]? resultSignalProfiles = null) {
        return new {
            ProbeKind = probeKind,
            Summary = summary,
            SupportsScopeDiscovery = true,
            SupportsExplicitTargets = true,
            KeyArguments = keyArguments,
            PreferredFollowUpTools = preferredFollowUpTools,
            DirectoryProbeSubKinds = directoryProbeSubKinds ?? System.Array.Empty<object>(),
            FollowUpProfiles = followUpProfiles ?? System.Array.Empty<object>(),
            ResultSignalProfiles = resultSignalProfiles ?? System.Array.Empty<object>()
        };
    }

    private static object[] CreateKerberosFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "transport_split",
                summary: "Use when UDP and TCP need to be compared separately or when intermittent Kerberos transport failures are suspected.",
                activatingArguments: new[] { "protocol=both", "split_protocol_results=true" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_network_adapters", "system_info" }),
            CreateFollowUpProfile(
                id: "time_skew_and_kdc_health",
                summary: "Use when AS-REQ failures or degraded latency suggest skew, reachability, or unhealthy KDC runtime state.",
                activatingArguments: new[] { "targets", "domain_name", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_time_sync", "system_info", "system_metrics_summary" }),
            CreateFollowUpProfile(
                id: "realm_override_validation",
                summary: "Use when explicit realm overrides or cross-domain targeting need validation before wider follow-up.",
                activatingArguments: new[] { "realm", "targets", "domain_name" },
                preferredFollowUpTools: new[] { "system_info", "system_network_client_posture" })
        };
    }

    private static object[] CreateLdapFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "ldaps_certificate_focus",
                summary: "Use when the operator explicitly asks about LDAP/LDAPS certificates, SANs, expiry, or chain health.",
                activatingArguments: new[] { "verify_certificate", "identity", "include_global_catalog" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_ldap_policy_posture", "system_info" }),
            CreateFollowUpProfile(
                id: "identity_or_bind_focus",
                summary: "Use when identity lookup, bind behavior, or LDAP search validation matters more than certificate detail.",
                activatingArguments: new[] { "identity", "domain_controller", "targets" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_info", "system_metrics_summary" }),
            CreateFollowUpProfile(
                id: "host_policy_focus",
                summary: "Use when LDAP succeeds but the next question is about host signing, channel binding, or broader DC runtime state.",
                activatingArguments: new[] { "domain_name", "targets", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_ldap_policy_posture", "system_metrics_summary", "system_info" })
        };
    }

    private static object[] CreateLdapResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "ldaps_certificate_problem",
                summary: "Use when LDAP probe output highlights certificate chain, expiry, SAN, or endpoint-identity issues.",
                signalHints: new[] { "certificate_chain_failed", "certificate_expired", "certificate_name_mismatch", "endpoint_identity_mismatch", "dns_name_failed" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_ldap_policy_posture", "system_info" }),
            CreateResultSignalProfile(
                id: "bind_or_identity_failure",
                summary: "Use when LDAP bind, search, or identity validation fails even though ports are reachable.",
                signalHints: new[] { "bind_failed", "invalid_credentials", "identity_failed", "search_failed", "access_denied" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_info", "system_metrics_summary" }),
            CreateResultSignalProfile(
                id: "port_or_latency_issue",
                summary: "Use when LDAP/LDAPS/GC ports are unreachable, slow, or inconsistent across the same host.",
                signalHints: new[] { "port_failed", "ldaps_unreachable", "gc_unreachable", "degraded_latency", "timeout" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_metrics_summary", "system_info" })
        };
    }

    private static object[] CreateDnsFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "query_shape_focus",
                summary: "Use when the next step depends on record type, query template, or UDP versus TCP resolution behavior.",
                activatingArguments: new[] { "dns_queries", "protocol", "domain_name" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateFollowUpProfile(
                id: "server_runtime_focus",
                summary: "Use when selected DNS servers are known and the next step is broader host inspection rather than more query tuning.",
                activatingArguments: new[] { "targets", "domain_name", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_info", "system_metrics_summary", "system_network_adapters" })
        };
    }

    private static object[] CreateDnsResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "missing_or_wrong_answers",
                summary: "Use when DNS responses return empty, wrong, or mismatched answers for the requested records.",
                signalHints: new[] { "no_answers", "wrong_answer", "record_missing", "query_name_mismatch", "srv_target_missing" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateResultSignalProfile(
                id: "resolver_timeout_or_transport_failure",
                summary: "Use when DNS checks fail by timeout, truncation, or protocol-specific transport problems.",
                signalHints: new[] { "timeout", "udp_failed", "tcp_failed", "truncated", "server_unreachable" },
                preferredFollowUpTools: new[] { "system_network_adapters", "system_ports_list", "system_info" })
        };
    }

    private static object[] CreateKerberosResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "clock_skew_or_kdc_latency",
                summary: "Use when Kerberos probe output suggests clock skew, timeout pressure, or high KDC round-trip time.",
                signalHints: new[] { "clock_skew", "time_offset", "timeout", "degraded_latency", "kdc_unreachable" },
                preferredFollowUpTools: new[] { "system_time_sync", "system_metrics_summary", "system_info" }),
            CreateResultSignalProfile(
                id: "transport_specific_failure",
                summary: "Use when UDP/TCP behavior differs or when one Kerberos transport fails while the other succeeds.",
                signalHints: new[] { "udp_failed", "tcp_failed", "split_protocol_child_down", "protocol_mismatch" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_network_adapters", "system_info" })
        };
    }

    private static object[] CreateNtpFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "time_skew_focus",
                summary: "Use when the operator wants to dig into skew, source selection, or w32time state on the same DCs.",
                activatingArguments: new[] { "targets", "domain_name", "timeout_ms" },
                preferredFollowUpTools: new[] { "system_time_sync", "system_info", "system_metrics_summary" }),
            CreateFollowUpProfile(
                id: "latency_and_runtime_focus",
                summary: "Use when NTP is reachable but delay or degradation suggests broader host runtime follow-up.",
                activatingArguments: new[] { "max_concurrency", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_info", "system_network_adapters" })
        };
    }

    private static object[] CreateNtpResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "clock_skew_detected",
                summary: "Use when NTP output reports excessive skew or offset beyond expected thresholds.",
                signalHints: new[] { "time_offset_high", "clock_skew", "offset_exceeded", "drift_detected" },
                preferredFollowUpTools: new[] { "system_time_sync", "system_info", "system_metrics_summary" }),
            CreateResultSignalProfile(
                id: "timeout_or_packet_loss",
                summary: "Use when NTP requests fail intermittently, time out, or show network-path loss symptoms.",
                signalHints: new[] { "timeout", "packet_loss", "server_unreachable", "response_missing" },
                preferredFollowUpTools: new[] { "system_network_adapters", "system_time_sync", "system_info" })
        };
    }

    private static object[] CreateDnsServiceFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "srv_answer_validation",
                summary: "Use when SRV answer presence or query-template issues need DNS client and adapter follow-through.",
                activatingArguments: new[] { "dns_service_query_name", "dns_service_record_type", "dns_service_require_answers" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateFollowUpProfile(
                id: "transport_fallback",
                summary: "Use when UDP versus TCP differences or truncation behavior matter for DNS service checks.",
                activatingArguments: new[] { "protocol" },
                preferredFollowUpTools: new[] { "system_network_adapters", "system_ports_list", "system_info" }),
            CreateFollowUpProfile(
                id: "server_runtime_focus",
                summary: "Use when selected DNS servers are slow or inconsistent and host runtime context matters more than query shape.",
                activatingArguments: new[] { "targets", "domain_name", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_info", "system_metrics_summary", "system_service_list" })
        };
    }

    private static object[] CreateDnsServiceResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "missing_answers_or_nxdomain",
                summary: "Use when DNS service output shows empty answers, NXDOMAIN, or mismatched SRV expectations.",
                signalHints: new[] { "no_answers", "nxdomain", "answer_count_zero", "query_name_mismatch" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateResultSignalProfile(
                id: "timeout_or_transport_issue",
                summary: "Use when DNS service checks fail by timeout, truncation, or transport-specific issues.",
                signalHints: new[] { "timeout", "truncated", "udp_failed", "tcp_failed", "server_unreachable" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_network_adapters", "system_info" })
        };
    }

    private static object[] CreatePortFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "ad_core_profile_validation",
                summary: "Use when the built-in AD core port set is being validated across discovered DCs.",
                activatingArguments: new[] { "use_ad_core_profile", "targets", "domain_name" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_process_list", "system_info" }),
            CreateFollowUpProfile(
                id: "udp_payload_focus",
                summary: "Use when UDP validation, DNS payloads, or NTP payloads matter more than generic TCP connectivity.",
                activatingArguments: new[] { "include_udp", "dns_queries", "latency_threshold_ms" },
                preferredFollowUpTools: new[] { "system_network_adapters", "system_time_sync", "system_info" }),
            CreateFollowUpProfile(
                id: "adws_payload_focus",
                summary: "Use when ADWS payload validation is enabled through the port probe and the next step should inspect ADWS-specific reachability.",
                activatingArguments: new[] { "port", "path", "bind_identity" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_service_list", "system_info" })
        };
    }

    private static object[] CreatePortResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "rpc_or_listener_missing",
                summary: "Use when expected listeners are missing, reset, or refused on targeted DC ports.",
                signalHints: new[] { "connection_refused", "port_closed", "listener_missing", "tcp_reset" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_process_list", "system_service_list" }),
            CreateResultSignalProfile(
                id: "udp_payload_or_ntp_failure",
                summary: "Use when UDP, DNS payload, or NTP payload validation fails even though basic reachability exists.",
                signalHints: new[] { "udp_timeout", "dns_payload_failed", "ntp_payload_failed", "answer_count_zero" },
                preferredFollowUpTools: new[] { "system_network_adapters", "system_time_sync", "system_info" }),
            CreateResultSignalProfile(
                id: "adws_payload_failure",
                summary: "Use when the ADWS payload path fails despite basic TCP reachability on 9389.",
                signalHints: new[] { "adws_payload_failed", "http_status_error", "soap_fault", "enumeration_failed" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_service_list", "system_info" })
        };
    }

    private static object[] CreateHttpsFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "certificate_validation_focus",
                summary: "Use when the operator explicitly wants certificate chain, expiry, or name validation detail for HTTPS endpoints.",
                activatingArguments: new[] { "verify_certificate", "url", "port" },
                preferredFollowUpTools: new[] { "system_tls_posture", "system_certificate_posture", "system_info" }),
            CreateFollowUpProfile(
                id: "latency_and_runtime_focus",
                summary: "Use when HTTPS is reachable but slow or intermittently degraded and host runtime context matters next.",
                activatingArguments: new[] { "degraded_above_ms", "targets", "domain_name" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_info", "system_network_adapters" })
        };
    }

    private static object[] CreateHttpsResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "tls_or_chain_failure",
                summary: "Use when HTTPS probe output shows handshake, chain-build, or protocol/cipher failures.",
                signalHints: new[] { "tls_handshake_failed", "chain_build_failed", "protocol_mismatch", "cipher_mismatch" },
                preferredFollowUpTools: new[] { "system_tls_posture", "system_certificate_posture", "system_info" }),
            CreateResultSignalProfile(
                id: "name_or_expiry_problem",
                summary: "Use when certificate name mismatch or expiry windows are the main HTTPS finding.",
                signalHints: new[] { "certificate_name_mismatch", "certificate_expired", "certificate_expiring_soon", "san_mismatch" },
                preferredFollowUpTools: new[] { "system_certificate_posture", "system_tls_posture", "system_info" }),
            CreateResultSignalProfile(
                id: "endpoint_reachable_but_slow",
                summary: "Use when the HTTPS endpoint works but latency or runtime pressure is the likely next branch.",
                signalHints: new[] { "degraded_latency", "timeout_near_threshold", "connect_slow", "response_slow" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_network_adapters", "system_info" })
        };
    }

    private static object[] CreateAdwsFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "endpoint_path_validation",
                summary: "Use when the operator wants to validate ADWS endpoint path, port, and bind behavior specifically.",
                activatingArguments: new[] { "port", "path", "bind_identity", "request_timeout_ms" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_service_list", "system_info" }),
            CreateFollowUpProfile(
                id: "host_runtime_focus",
                summary: "Use when ADWS targets are identified and the next step is broader host diagnostics rather than more payload detail.",
                activatingArguments: new[] { "targets", "domain_name", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_info", "system_metrics_summary", "system_service_list" })
        };
    }

    private static object[] CreateAdwsResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "listener_or_port_failure",
                summary: "Use when ADWS reachability fails at the TCP listener or port level.",
                signalHints: new[] { "connection_refused", "port_closed", "listener_missing", "timeout" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_service_list", "system_info" }),
            CreateResultSignalProfile(
                id: "authentication_or_bind_failure",
                summary: "Use when ADWS responds but bind or authentication behavior fails.",
                signalHints: new[] { "bind_failed", "authentication_failed", "access_denied", "credential_rejected" },
                preferredFollowUpTools: new[] { "system_service_list", "system_info", "system_metrics_summary" }),
            CreateResultSignalProfile(
                id: "path_or_payload_failure",
                summary: "Use when the service is up but the ADWS endpoint path or SOAP payload behavior is broken.",
                signalHints: new[] { "path_not_found", "soap_fault", "enumeration_failed", "http_status_error" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_service_list", "system_info" })
        };
    }

    private static object[] CreateReplicationFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "sysvol_follow_through",
                summary: "Use when SYSVOL or NETLOGON health is part of the replication investigation.",
                activatingArguments: new[] { "include_sysvol", "test_sysvol_shares" },
                preferredFollowUpTools: new[] { "system_logical_disks_list", "system_service_list", "system_info" }),
            CreateFollowUpProfile(
                id: "connectivity_preflight",
                summary: "Use when port or ping preflight is needed before trusting replication freshness findings.",
                activatingArguments: new[] { "test_ports", "test_ping", "query_mode" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_network_adapters", "system_info" }),
            CreateFollowUpProfile(
                id: "resource_pressure",
                summary: "Use when stale neighbors or degraded replication suggest CPU, memory, or disk pressure on DCs.",
                activatingArguments: new[] { "stale_threshold_hours", "domain_controller", "targets" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_hardware_summary", "system_logical_disks_list" })
        };
    }

    private static object[] CreateReplicationResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "stale_neighbors_or_backlog",
                summary: "Use when replication output highlights stale neighbors, backlog pressure, or degraded freshness.",
                signalHints: new[] { "stale_neighbor", "backlog_warning", "backlog_critical", "consecutive_failures" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_logical_disks_list", "system_info" }),
            CreateResultSignalProfile(
                id: "sysvol_or_share_failure",
                summary: "Use when replication details show SYSVOL, DFSR, NETLOGON, or share-access failures.",
                signalHints: new[] { "sysvol_failed", "dfsr_issue", "netlogon_unavailable", "share_access_denied" },
                preferredFollowUpTools: new[] { "system_service_list", "system_logical_disks_list", "system_info" }),
            CreateResultSignalProfile(
                id: "endpoint_connectivity_failure",
                summary: "Use when replication details show RPC, port, or ping preflight failures for involved DCs.",
                signalHints: new[] { "rpc_failed", "port_check_failed", "ping_failed", "preflight_below_threshold" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_network_adapters", "system_info" })
        };
    }

    private static object[] CreatePingFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "latency_focus",
                summary: "Use when the next step is to understand why a host is slow rather than fully unreachable.",
                activatingArguments: new[] { "latency_threshold_ms", "p95_latency_threshold_ms", "targets" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_network_adapters", "system_info" }),
            CreateFollowUpProfile(
                id: "reachability_focus",
                summary: "Use when the operator wants to branch from ICMP reachability into broader host diagnostics for the same targets.",
                activatingArguments: new[] { "targets", "domain_name", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_info", "system_network_adapters", "system_metrics_summary" })
        };
    }

    private static object[] CreatePingResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "high_latency_or_jitter",
                summary: "Use when ping succeeds but RTT, p95 latency, or jitter suggests runtime or path pressure.",
                signalHints: new[] { "degraded_latency", "p95_high", "jitter_high", "slow_response" },
                preferredFollowUpTools: new[] { "system_metrics_summary", "system_network_adapters", "system_info" }),
            CreateResultSignalProfile(
                id: "packet_loss_or_unreachable",
                summary: "Use when ping shows loss, unreachable targets, or ICMP suppression behavior.",
                signalHints: new[] { "packet_loss", "unreachable", "icmp_blocked", "host_down" },
                preferredFollowUpTools: new[] { "system_network_adapters", "system_ports_list", "system_info" })
        };
    }

    private static object[] CreateWindowsUpdateFollowUpProfiles() {
        return new object[] {
            CreateFollowUpProfile(
                id: "wsus_management_focus",
                summary: "Use when the next step is to validate WSUS/client-management posture rather than enumerate installed patches.",
                activatingArguments: new[] { "require_wsus", "targets", "domain_controller" },
                preferredFollowUpTools: new[] { "system_windows_update_client_status", "system_windows_update_telemetry", "system_info" }),
            CreateFollowUpProfile(
                id: "patch_inventory_focus",
                summary: "Use when the next step is patch inventory or compliance follow-through on the same hosts.",
                activatingArguments: new[] { "domain_name", "targets", "discovery_fallback" },
                preferredFollowUpTools: new[] { "system_updates_installed", "system_patch_compliance", "system_windows_update_client_status" })
        };
    }

    private static object[] CreateWindowsUpdateResultSignalProfiles() {
        return new object[] {
            CreateResultSignalProfile(
                id: "wsus_misconfiguration_or_scan_failure",
                summary: "Use when Windows Update probe output shows WSUS misconfiguration, scan failures, or stale client state.",
                signalHints: new[] { "wsus_missing", "scan_failed", "last_scan_stale", "client_misconfigured" },
                preferredFollowUpTools: new[] { "system_windows_update_client_status", "system_windows_update_telemetry", "system_info" }),
            CreateResultSignalProfile(
                id: "missing_updates_or_reboot_required",
                summary: "Use when the primary issue is patch drift, reboot pressure, or stale installed-update state.",
                signalHints: new[] { "missing_updates", "reboot_required", "pending_reboot", "compliance_failed" },
                preferredFollowUpTools: new[] { "system_patch_compliance", "system_updates_installed", "system_windows_update_telemetry" })
        };
    }

    private static object[] CreateDirectoryProbeSubKinds() {
        return new object[] {
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "root_dse",
                summary: "Validate RootDSE responsiveness and advertised capability flags on discovered or selected DCs.",
                keyArguments: new[] { "directory_require_global_catalog_ready", "directory_require_synchronized", "directory_use_ldaps" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_ldap_policy_posture", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "dns_registration",
                summary: "Check whether expected A/SRV/PTR DNS records are present and coherent for selected DCs.",
                keyArguments: new[] { "directory_query_name", "directory_dns_servers", "directory_use_all_dns_servers" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "srv_coverage",
                summary: "Validate SRV record coverage across sites and service roles.",
                keyArguments: new[] { "directory_sites", "directory_exclude_sites", "directory_query_name" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "fsmo",
                summary: "Check FSMO role-holder reachability and writability.",
                keyArguments: new[] { "directory_include_forest_roles", "domain_name", "targets" },
                preferredFollowUpTools: new[] { "system_info", "system_time_sync", "system_service_list" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "sysvol_gpt",
                summary: "Validate SYSVOL GPT.ini presence and version consistency across DCs.",
                keyArguments: new[] { "domain_name", "targets", "total_budget_ms" },
                preferredFollowUpTools: new[] { "system_logical_disks_list", "system_service_list", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "netlogon_share",
                summary: "Verify NETLOGON share availability and related share exposure on DCs.",
                keyArguments: new[] { "directory_share_name", "directory_ignore_drive_shares", "targets" },
                preferredFollowUpTools: new[] { "system_service_list", "system_logical_disks_list", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "dns_soa",
                summary: "Check SOA record parity across DNS servers serving AD-integrated zones.",
                keyArguments: new[] { "directory_zones", "directory_dns_servers", "directory_use_all_dns_servers" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "ldap_search",
                summary: "Run LDAP search sanity checks, optionally over LDAPS or StartTLS, against selected DCs.",
                keyArguments: new[] { "directory_search_base", "directory_filter", "directory_attribute", "directory_use_ldaps", "directory_use_start_tls" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_ldap_policy_posture", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "gc_readiness",
                summary: "Confirm advertised GC targets are actually GC-ready and synchronized.",
                keyArguments: new[] { "directory_require_global_catalog_ready", "directory_require_synchronized", "directory_query_name" },
                preferredFollowUpTools: new[] { "ad_ldap_diagnostics", "system_ldap_policy_posture", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "client_path",
                summary: "Validate client SRV lookup path and selected endpoint suitability by site.",
                keyArguments: new[] { "directory_sites", "directory_dns_servers", "directory_query_name", "directory_use_ldaps" },
                preferredFollowUpTools: new[] { "system_network_client_posture", "system_network_adapters", "system_info" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "rpc_endpoint",
                summary: "Check RPC endpoint mapper reachability on TCP 135 for the selected hosts.",
                keyArguments: new[] { "targets", "port", "degraded_above_ms" },
                preferredFollowUpTools: new[] { "system_ports_list", "system_service_list", "system_process_list" }),
            CreateDirectoryProbeSubKind(
                directoryProbeKind: "share_permissions",
                summary: "Validate required and optional share exposure plus share permission expectations.",
                keyArguments: new[] { "directory_required_shares", "directory_optional_shares", "directory_allowed_shares" },
                preferredFollowUpTools: new[] { "system_service_list", "system_logical_disks_list", "system_local_identity_inventory" })
        };
    }

    private static object CreateDirectoryProbeSubKind(
        string directoryProbeKind,
        string summary,
        string[] keyArguments,
        string[] preferredFollowUpTools) {
        return new {
            DirectoryProbeKind = directoryProbeKind,
            Summary = summary,
            KeyArguments = keyArguments,
            PreferredFollowUpTools = preferredFollowUpTools
        };
    }

    private static object CreateFollowUpProfile(
        string id,
        string summary,
        string[] activatingArguments,
        string[] preferredFollowUpTools) {
        return new {
            Id = id,
            Summary = summary,
            ActivatingArguments = activatingArguments,
            PreferredFollowUpTools = preferredFollowUpTools
        };
    }

    private static object CreateResultSignalProfile(
        string id,
        string summary,
        string[] signalHints,
        string[] preferredFollowUpTools) {
        return new {
            Id = id,
            Summary = summary,
            SignalHints = signalHints,
            PreferredFollowUpTools = preferredFollowUpTools
        };
    }
}
