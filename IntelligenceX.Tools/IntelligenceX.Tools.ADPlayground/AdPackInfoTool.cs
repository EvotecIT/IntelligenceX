using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Returns Active Directory pack capabilities and usage guidance for model-driven tool planning.
/// </summary>
public sealed class AdPackInfoTool : ActiveDirectoryToolBase, ITool {
    private sealed record PackInfoRequest;

    private static readonly ToolDefinition DefinitionValue = ToolPackDefinitionFactory.CreatePackInfoDefinition(
        toolName: "ad_pack_info",
        description: "Return Active Directory pack capabilities, output contract, and recommended usage patterns. Call this first when planning AD investigations.",
        packId: "active_directory",
        category: "active_directory",
        tags: new[] {
            "pack:active_directory",
            "domain_family:ad_domain",
            "domain_signals:dc,ldap,gpo,kerberos,replication,sysvol,netlogon,ntds,forest,trust,active_directory,adplayground"
        },
        domainIntentFamily: ToolSelectionMetadata.DomainIntentFamilyAd,
        domainIntentActionId: ToolSelectionMetadata.DomainIntentActionIdAd,
        domainSignalTokens: new[] {
            "dc",
            "ldap",
            "gpo",
            "kerberos",
            "replication",
            "sysvol",
            "netlogon",
            "ntds",
            "forest",
            "trust",
            "active_directory",
            "adplayground"
        });

    /// <summary>
    /// Initializes a new instance of the <see cref="AdPackInfoTool"/> class.
    /// </summary>
    public AdPackInfoTool(ActiveDirectoryToolOptions options) : base(options) { }

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

        var root = BuildGuidance(Options);

        var summary = ToolMarkdown.SummaryText(
            title: "Active Directory Pack",
            "Use raw payloads for reasoning/correlation; use `*_view` only for presentation.",
            "Prefer `ad_object_resolve` and paged queries to reduce repeated lookups.");

        return Task.FromResult(ToolResultV2.OkModel(root, summaryMarkdown: summary));
    }

    internal static ToolPackInfoModel BuildGuidance(ActiveDirectoryToolOptions options) {
        return ToolPackGuidance.Create(
            pack: "active_directory",
            engine: "ADPlayground",
            tools: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolNames(options),
            recommendedFlow: new[] {
                "Use ad_connectivity_probe first when RootDSE reachability, effective domain_controller/search_base_dn resolution, or basic domain-controller discovery is uncertain before broader AD discovery or monitoring work.",
                "Call ad_environment_discover first to learn effective domain_controller/search_base_dn and candidate DCs.",
                "Use ad_scope_discovery for explicit scope + receipt output (forest/domain, naming contexts, domains/DCs, and missing-reason diagnostics).",
                "Use ad_forest_discover to make forest scope explicit and get a receipt (domains/trusts/DCs discovered and how).",
                "Use ad_forest_functional for forest-level functional posture and recommended target-level planning.",
                "Use ad_search/ad_groups_list/ad_spn_search for broad discovery.",
                "Use ad_user_groups_resolved when the question is specifically about a user's effective access footprint or when lifecycle changes need read-only membership verification without looping over ad_object_get calls.",
                "Use ad_password_policy/ad_password_policy_rollup/ad_password_policy_length and ad_trust for policy and trust-posture diagnostics.",
                "Use ad_domain_statistics/ad_domain_controller_facts/ad_dc_fleet_posture/ad_fsmo_roles/ad_krbtgt_health/ad_system_state_backup for domain resilience posture checks.",
                "Use ad_domain_container_defaults/ad_machine_account_quota for domain default and join-governance posture.",
                "Use ad_service_account_usage/ad_never_logged_in_accounts/ad_stale_accounts/ad_kds_root_keys/ad_admin_count_report for account hygiene, dormant-identity exposure, privileged-account hygiene, and gMSA readiness checks.",
                "Use ad_client_server_auth_posture/ad_legacy_cve_exposure/ad_firewall_profiles/ad_time_service_configuration/ad_llmnr_policy/ad_wdigest_policy/ad_winrm_policy/ad_proxy_policy/ad_schannel_policy/ad_terminal_services_redirection_policy/ad_terminal_services_timeout_policy/ad_name_resolution_policy/ad_lsa_protection_policy/ad_net_session_hardening_policy/ad_limit_blank_password_use_policy/ad_pku2u_policy/ad_hardened_paths_policy/ad_kdc_proxy_policy/ad_kerberos_pac_policy/ad_powershell_logging_policy/ad_no_lm_hash_policy/ad_ntlm_restrictions_policy/ad_restrict_ntlm_configuration/ad_logon_ux_uac_policy/ad_deny_logon_rights_policy/ad_defender_asr_policy/ad_everyone_includes_anonymous_policy/ad_enable_delegation_privilege_policy/ad_kerberos_crypto_posture/ad_lan_manager_settings/ad_null_session_posture/ad_domain_controller_security/ad_ds_heuristics/ad_laps_schema_posture/ad_azuread_sso for credential, schema, and endpoint hardening posture.",
                "Use ad_duplicate_accounts/ad_ou_protection/ad_registration_posture/ad_laps_coverage for AD hygiene and control-plane registration coverage.",
                "Use ad_shadow_credentials_risk/ad_dc_shadow_indicators/ad_dangerous_extended_rights/ad_smartcard_posture for ACL and identity abuse-path diagnostics.",
                "Use ad_pki_templates/ad_pki_posture for certificate template and PKI endpoint risk posture.",
                "Use ad_sites/ad_subnets/ad_site_links for AD topology inventory and schedule diagnostics.",
                "Use ad_site_coverage for per-site subnet/DC coverage and orphaned subnet visibility.",
                "Use ad_dns_server_config/ad_dns_zone_config/ad_dns_zone_security/ad_dns_delegation/ad_dns_scavenging for DNS server/zone/delegation posture diagnostics.",
                "Use ad_gpo_list/ad_gpo_changes/ad_gpo_health/ad_gpo_inventory_health/ad_gpo_duplicates/ad_gpo_blocked_inheritance/ad_gpo_ou_link_summary/ad_gpo_integrity/ad_gpo_redirect/ad_gpo_permission_read/ad_gpo_permission_administrative/ad_gpo_permission_consistency/ad_gpo_permission_unknown/ad_gpo_permission_root/ad_gpo_permission_report/ad_wmi_filters/ad_wsus_configuration for GPO inventory, timeline, topology, permission hygiene, and WSUS/WMI filter diagnostics.",
                "Use ad_handoff_prepare to normalize cross-pack entity_handoff payloads before AD queries.",
                "Example EventLog handoff flow: ad_handoff_prepare -> ad_scope_discovery -> ad_object_resolve -> ad_search/ad_object_get for focused follow-up.",
                "Use ad_object_resolve to avoid N+1 object lookups when correlating identities.",
                "For authoritative last-logon investigations, enumerate DCs first (ad_scope_discovery/ad_forest_discover), then query each DC with ad_ldap_query for lastLogon and compare max value; treat lastLogonTimestamp as replicated approximation.",
                "Use ad_ldap_query_paged for large exploratory queries and continue with cursor.",
                "Use ad_search_facets/ad_replication_summary/ad_replication_connections/ad_replication_status/ad_directory_discovery_diagnostics/ad_dns_server_config/ad_dns_zone_config/ad_dns_zone_security/ad_dns_delegation/ad_delegation_audit/ad_spn_stats for aggregated diagnostics.",
                "Use ad_monitoring_probe_catalog + ad_monitoring_probe_run for runtime AD monitoring probes (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping/windows_update). For probe_kind=directory, inspect the catalog's directory_probe_subkinds and follow those preferred follow-up tools before generic host pivots. For ldap, dns, kerberos, ntp, replication, ping, windows_update, port, https, and adws, inspect follow_up_profiles when the next diagnostic step depends on transport, TLS, WSUS, preflight, listener, or runtime pressure context, and inspect result_signal_profiles when probe output already points to a specific failure shape.",
                "For LDAP/LDAPS certificate questions, prefer ad_ldap_diagnostics or ad_monitoring_probe_run with probe_kind=ldap and verify_certificate=true for endpoint evidence. Use system_certificate_posture only when the follow-up is explicitly about machine certificate stores or trust-store posture on the same host.",
                "Use ad_monitoring_service_heartbeat_get/ad_monitoring_diagnostics_get/ad_monitoring_metrics_get/ad_monitoring_dashboard_state_get for persisted monitoring-service health, scheduler pressure, queue pressure, and dashboard-state inspection from an allowed monitoring directory.",
                "When AD discovery or monitoring results identify specific domain controllers or hosts, pivot into system_info/system_time_sync/system_metrics_summary/system_hardware_summary/system_logical_disks_list/system_backup_posture/system_office_posture/system_browser_posture/system_tls_posture/system_winrm_posture/system_powershell_logging_posture/system_uac_posture/system_ldap_policy_posture/system_network_client_posture/system_account_policy_posture/system_interactive_logon_posture/system_device_guard_posture/system_defender_asr_posture with computer_name instead of asking the model to improvise the cross-pack jump."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Validate AD context and basic LDAP reachability",
                    suggestedTools: new[] { "ad_connectivity_probe", "ad_environment_discover", "ad_scope_discovery" },
                    notes: "Start here when the effective domain controller, search base, or basic RootDSE reachability is still uncertain."),
                ToolPackGuidance.FlowStep(
                    goal: "Discover candidate AD objects",
                    suggestedTools: new[] { "ad_search", "ad_groups_list", "ad_spn_search" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess GPO inventory, changes, and health",
                    suggestedTools: new[] { "ad_gpo_list", "ad_gpo_changes", "ad_gpo_health", "ad_gpo_inventory_health", "ad_gpo_duplicates", "ad_gpo_blocked_inheritance", "ad_gpo_ou_link_summary", "ad_gpo_integrity", "ad_gpo_redirect", "ad_gpo_permission_read", "ad_gpo_permission_administrative", "ad_gpo_permission_consistency", "ad_gpo_permission_unknown", "ad_gpo_permission_root", "ad_gpo_permission_report", "ad_wmi_filters", "ad_wsus_configuration" }),
                ToolPackGuidance.FlowStep(
                    goal: "Discover forest scope and enumerate domains/DCs/trusts",
                    suggestedTools: new[] { "ad_scope_discovery", "ad_forest_discover", "ad_forest_functional" }),
                ToolPackGuidance.FlowStep(
                    goal: "Inspect AD topology (sites, subnets, and site links)",
                    suggestedTools: new[] { "ad_sites", "ad_subnets", "ad_site_links" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess password policy and trust posture",
                    suggestedTools: new[] { "ad_password_policy", "ad_password_policy_rollup", "ad_password_policy_length", "ad_trust", "ad_site_coverage", "ad_schema_version" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess domain resilience posture (functional level/FSMO/KRBTGT/backups)",
                    suggestedTools: new[] { "ad_domain_statistics", "ad_domain_controller_facts", "ad_dc_fleet_posture", "ad_domain_container_defaults", "ad_machine_account_quota", "ad_fsmo_roles", "ad_krbtgt_health", "ad_recycle_bin_lifetime", "ad_system_state_backup" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess auth hardening posture (Kerberos crypto and null-session exposure)",
                    suggestedTools: new[] { "ad_client_server_auth_posture", "ad_legacy_cve_exposure", "ad_firewall_profiles", "ad_time_service_configuration", "ad_llmnr_policy", "ad_wdigest_policy", "ad_winrm_policy", "ad_proxy_policy", "ad_schannel_policy", "ad_terminal_services_redirection_policy", "ad_terminal_services_timeout_policy", "ad_name_resolution_policy", "ad_lsa_protection_policy", "ad_net_session_hardening_policy", "ad_limit_blank_password_use_policy", "ad_pku2u_policy", "ad_hardened_paths_policy", "ad_kdc_proxy_policy", "ad_kerberos_pac_policy", "ad_powershell_logging_policy", "ad_no_lm_hash_policy", "ad_ntlm_restrictions_policy", "ad_restrict_ntlm_configuration", "ad_logon_ux_uac_policy", "ad_deny_logon_rights_policy", "ad_defender_asr_policy", "ad_everyone_includes_anonymous_policy", "ad_enable_delegation_privilege_policy", "ad_kerberos_crypto_posture", "ad_lan_manager_settings", "ad_null_session_posture", "ad_domain_controller_security", "ad_ds_heuristics", "ad_laps_schema_posture", "ad_azuread_sso" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess AD hygiene and registration posture",
                    suggestedTools: new[] { "ad_duplicate_accounts", "ad_ou_protection", "ad_registration_posture", "ad_laps_coverage", "ad_service_account_usage", "ad_never_logged_in_accounts", "ad_stale_accounts", "ad_kds_root_keys", "ad_admin_count_report" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess ACL and identity abuse-path exposure",
                    suggestedTools: new[] { "ad_shadow_credentials_risk", "ad_dc_shadow_indicators", "ad_dangerous_extended_rights", "ad_smartcard_posture" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess PKI posture (templates and enrollment endpoint security)",
                    suggestedTools: new[] { "ad_pki_templates", "ad_pki_posture" }),
                ToolPackGuidance.FlowStep(
                    goal: "Resolve/expand identities for correlation",
                    suggestedTools: new[] { "ad_handoff_prepare", "ad_scope_discovery", "ad_object_resolve", "ad_object_get", "ad_group_members_resolved", "ad_user_groups_resolved" }),
                ToolPackGuidance.FlowStep(
                    goal: "Confirm authoritative user/computer logon recency across DCs",
                    suggestedTools: new[] { "ad_scope_discovery", "ad_forest_discover", "ad_ldap_query", "ad_ldap_query_paged" }),
                ToolPackGuidance.FlowStep(
                    goal: "Run diagnostics and aggregate analysis",
                    suggestedTools: new[] { "ad_search_facets", "ad_replication_summary", "ad_replication_connections", "ad_replication_status", "ad_directory_discovery_diagnostics", "ad_dns_server_config", "ad_dns_zone_config", "ad_dns_zone_security", "ad_dns_delegation", "ad_delegation_audit", "ad_spn_stats", "ad_spn_hygiene", "ad_ldap_diagnostics", "ad_dns_scavenging" }),
                ToolPackGuidance.FlowStep(
                    goal: "Run AD runtime monitoring probes",
                    suggestedTools: new[] { "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" })
            },
            capabilities: new[] {
                ToolPackGuidance.Capability(
                    id: "connectivity_preflight",
                    summary: "Validate RootDSE/context reachability and gather a small domain-controller sample before broader AD discovery, monitoring, or LDAP reads.",
                    primaryTools: new[] { "ad_connectivity_probe", "ad_environment_discover", "ad_scope_discovery" }),
                ToolPackGuidance.Capability(
                    id: "directory_discovery",
                    summary: "Search and list AD users/groups/computers with optional dynamic attribute bags.",
                    primaryTools: new[] { "ad_search", "ad_groups_list", "ad_spn_search" }),
                ToolPackGuidance.Capability(
                    id: "identity_resolution",
                    summary: "Resolve identities and membership details for cross-tool correlation.",
                    primaryTools: new[] { "ad_handoff_prepare", "ad_object_resolve", "ad_object_get", "ad_group_members", "ad_group_members_resolved", "ad_user_groups_resolved" }),
                ToolPackGuidance.Capability(
                    id: "authoritative_logon_correlation",
                    summary: "Correlate last-logon evidence per-DC using direct LDAP reads and forest/domain discovery context.",
                    primaryTools: new[] { "ad_scope_discovery", "ad_forest_discover", "ad_ldap_query", "ad_ldap_query_paged" }),
                ToolPackGuidance.Capability(
                    id: "ad_diagnostics",
                    summary: "Provide LDAP diagnostics and aggregated security/replication insights.",
                    primaryTools: new[] { "ad_ldap_diagnostics", "ad_search_facets", "ad_replication_summary", "ad_replication_connections", "ad_replication_status", "ad_directory_discovery_diagnostics", "ad_dns_server_config", "ad_dns_zone_config", "ad_dns_zone_security", "ad_dns_delegation", "ad_delegation_audit", "ad_spn_stats", "ad_spn_hygiene", "ad_dns_scavenging" }),
                ToolPackGuidance.Capability(
                    id: "scope_discovery_receipt",
                    summary: "Discover effective AD scope and emit probe receipts with endpoints, timeouts, and missing reasons.",
                    primaryTools: new[] { "ad_scope_discovery", "ad_forest_discover" }),
                ToolPackGuidance.Capability(
                    id: "topology_inventory",
                    summary: "Inspect Active Directory site/subnet/link topology and link schedule coverage.",
                    primaryTools: new[] { "ad_sites", "ad_subnets", "ad_site_links" }),
                ToolPackGuidance.Capability(
                    id: "domain_trust_policy",
                    summary: "Inspect password policy posture, trust relationships, and site coverage indicators.",
                    primaryTools: new[] { "ad_password_policy", "ad_password_policy_rollup", "ad_password_policy_length", "ad_schema_version", "ad_trust", "ad_site_coverage" }),
                ToolPackGuidance.Capability(
                    id: "domain_resilience",
                    summary: "Inspect functional level, FSMO placement, KRBTGT posture, recycle-bin lifetimes, and DC backup recency.",
                    primaryTools: new[] { "ad_forest_functional", "ad_domain_statistics", "ad_domain_controller_facts", "ad_dc_fleet_posture", "ad_domain_container_defaults", "ad_machine_account_quota", "ad_fsmo_roles", "ad_krbtgt_health", "ad_recycle_bin_lifetime", "ad_system_state_backup" }),
                ToolPackGuidance.Capability(
                    id: "auth_hardening",
                    summary: "Inspect Kerberos crypto legacy exposure, null-session posture, dsHeuristics, LAPS schema, and Azure AD Seamless SSO account hardening.",
                    primaryTools: new[] { "ad_client_server_auth_posture", "ad_legacy_cve_exposure", "ad_firewall_profiles", "ad_time_service_configuration", "ad_llmnr_policy", "ad_wdigest_policy", "ad_winrm_policy", "ad_proxy_policy", "ad_schannel_policy", "ad_terminal_services_redirection_policy", "ad_terminal_services_timeout_policy", "ad_name_resolution_policy", "ad_lsa_protection_policy", "ad_net_session_hardening_policy", "ad_limit_blank_password_use_policy", "ad_pku2u_policy", "ad_hardened_paths_policy", "ad_kdc_proxy_policy", "ad_kerberos_pac_policy", "ad_powershell_logging_policy", "ad_no_lm_hash_policy", "ad_ntlm_restrictions_policy", "ad_restrict_ntlm_configuration", "ad_logon_ux_uac_policy", "ad_deny_logon_rights_policy", "ad_defender_asr_policy", "ad_everyone_includes_anonymous_policy", "ad_enable_delegation_privilege_policy", "ad_kerberos_crypto_posture", "ad_lan_manager_settings", "ad_null_session_posture", "ad_domain_controller_security", "ad_ds_heuristics", "ad_laps_schema_posture", "ad_azuread_sso" }),
                ToolPackGuidance.Capability(
                    id: "directory_hygiene",
                    summary: "Inspect duplicate principals, OU deletion-protection posture, DC registration gaps, LAPS coverage, and privileged adminCount account hygiene.",
                    primaryTools: new[] { "ad_duplicate_accounts", "ad_ou_protection", "ad_registration_posture", "ad_laps_coverage", "ad_service_account_usage", "ad_never_logged_in_accounts", "ad_stale_accounts", "ad_kds_root_keys", "ad_admin_count_report" }),
                ToolPackGuidance.Capability(
                    id: "acl_identity_exposure",
                    summary: "Inspect shadow credential, replication-right, dangerous ACE, and smart-card posture abuse-path exposure.",
                    primaryTools: new[] { "ad_shadow_credentials_risk", "ad_dc_shadow_indicators", "ad_dangerous_extended_rights", "ad_smartcard_posture" }),
                ToolPackGuidance.Capability(
                    id: "pki_posture",
                    summary: "Inspect certificate template risks and PKI endpoint posture (ROCA, weak RSA, endpoint HTTPS).",
                    primaryTools: new[] { "ad_pki_templates", "ad_pki_posture" }),
                ToolPackGuidance.Capability(
                    id: "gpo_hygiene",
                    summary: "Inspect Group Policy inventory, modification history, AD/SYSVOL health state, and permission baselines.",
                    primaryTools: new[] { "ad_gpo_list", "ad_gpo_changes", "ad_gpo_health", "ad_gpo_inventory_health", "ad_gpo_duplicates", "ad_gpo_blocked_inheritance", "ad_gpo_ou_link_summary", "ad_gpo_integrity", "ad_gpo_redirect", "ad_gpo_permission_read", "ad_gpo_permission_administrative", "ad_gpo_permission_consistency", "ad_gpo_permission_unknown", "ad_gpo_permission_root", "ad_gpo_permission_report", "ad_wmi_filters", "ad_wsus_configuration" }),
                ToolPackGuidance.Capability(
                    id: "ad_runtime_monitoring",
                    summary: "Run ADPlayground.Monitoring probes (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping/windows_update) for server/domain/forest scope, and use the catalog metadata for probe-specific follow-through including directory sub-kinds, follow-up profiles, and result-signal profiles.",
                    primaryTools: new[] { "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" }),
                ToolPackGuidance.Capability(
                    id: "ldap_certificate_followthrough",
                    summary: "Capture LDAP/LDAPS endpoint certificate evidence. Use remote ComputerX certificate-store posture only when the follow-up is explicitly about the host machine stores rather than the LDAP service certificate itself.",
                    primaryTools: new[] { "ad_ldap_diagnostics", "ad_monitoring_probe_run", "system_certificate_posture", "system_tls_posture" }),
                ToolPackGuidance.Capability(
                    id: "ad_monitoring_runtime_state",
                    summary: "Inspect persisted ADPlayground.Monitoring heartbeat, diagnostics, scheduler pressure, and dashboard auto-generation state.",
                    primaryTools: new[] { "ad_monitoring_service_heartbeat_get", "ad_monitoring_diagnostics_get", "ad_monitoring_metrics_get", "ad_monitoring_dashboard_state_get" })
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "external_identity_to_ad_resolution",
                    summary: "Consume identity/host indicators from other packs and normalize them for AD object resolution.",
                    entityKinds: new[] { "identity", "user", "group", "computer", "host" },
                    sourceTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query", "system_whoami", "powershell_run" },
                    targetTools: new[] { "ad_handoff_prepare", "ad_scope_discovery", "ad_object_resolve", "ad_search", "ad_object_get", "ad_group_members_resolved" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("*.who", "identities", "Batch and deduplicate values for ad_object_resolve."),
                        ToolPackGuidance.EntityFieldMapping("*.object_affected", "identities", "Batch and deduplicate values for ad_object_resolve."),
                        ToolPackGuidance.EntityFieldMapping("*.computer", "identity", "Use hostname/FQDN as identity for ad_search/ad_object_get."),
                        ToolPackGuidance.EntityFieldMapping("identity.account_name", "identity", "Use as direct identity input for ad_search/ad_object_get."),
                        ToolPackGuidance.EntityFieldMapping("resolved[].distinguished_name", "identity", "Use direct DN for follow-up detail lookups.")
                    },
                    notes: "Prefer ad_scope_discovery before lookups when domain/DC context is unclear; use ad_object_resolve for bulk correlation to avoid N+1 lookups."),
                ToolPackGuidance.EntityHandoff(
                    id: "ad_host_context_to_system_remote_scope",
                    summary: "Promote discovered domain-controller and AD host evidence into remote ComputerX/System host diagnostics.",
                    entityKinds: new[] { "computer", "host", "domain_controller" },
                    sourceTools: new[] { "ad_environment_discover", "ad_scope_discovery", "ad_forest_discover", "ad_domain_controllers", "ad_monitoring_probe_run" },
                    targetTools: new[] { "system_info", "system_time_sync", "system_metrics_summary", "system_hardware_summary", "system_process_list", "system_service_list", "system_ports_list", "system_network_adapters", "system_logical_disks_list", "system_disks_list", "system_devices_summary", "system_features_list", "system_windows_update_client_status", "system_windows_update_telemetry", "system_backup_posture", "system_office_posture", "system_browser_posture", "system_tls_posture", "system_winrm_posture", "system_powershell_logging_posture", "system_uac_posture", "system_ldap_policy_posture", "system_network_client_posture", "system_account_policy_posture", "system_interactive_logon_posture", "system_device_guard_posture", "system_defender_asr_posture", "system_certificate_posture" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("domain_controllers[].value", "computer_name", "Use discovered DC FQDN/host values directly as remote ComputerX targets."),
                        ToolPackGuidance.EntityFieldMapping("domain_controllers[]", "computer_name", "Handle flat DC arrays returned by scope-discovery and discovery receipts."),
                        ToolPackGuidance.EntityFieldMapping("domain_controller", "computer_name", "Use explicit domain_controller when a single-server pivot is intended."),
                        ToolPackGuidance.EntityFieldMapping("targets[]", "computer_name", "Use explicit monitoring probe targets as remote ComputerX follow-up hosts.")
                    },
                    notes: "Prefer one host or a small deduplicated host batch at a time; use computer_name for all remote System pack follow-up tools. LDAP/LDAPS certificate requests should normally stay in the AD tools unless the user explicitly asks for machine-store or trust-store posture on the same host. For NTP/time-skew follow-up prefer system_time_sync; for host backup coverage prefer system_backup_posture; for host application hardening prefer system_office_posture or system_browser_posture; for crypto, remote-management, script-auditing, elevation, host LDAP/network client policy, effective host account/logon policy, or virtualization/ASR follow-up prefer system_tls_posture, system_winrm_posture, system_powershell_logging_posture, system_uac_posture, system_ldap_policy_posture, system_network_client_posture, system_account_policy_posture, system_interactive_logon_posture, system_device_guard_posture, or system_defender_asr_posture.")
            },
            recipes: new[] {
                ToolPackGuidance.Recipe(
                    id: "forest_scope_bootstrap",
                    summary: "Bootstrap forest/domain scope before deeper AD analysis or cross-pack pivots.",
                    whenToUse: "Use when the domain controller, naming context, forest boundary, or candidate DC list is not explicit yet.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Validate connectivity and discover effective local AD context",
                            suggestedTools: new[] { "ad_connectivity_probe", "ad_environment_discover", "ad_scope_discovery" },
                            notes: "Start here to confirm RootDSE reachability, effective domain controller, naming contexts, and missing-reason diagnostics."),
                        ToolPackGuidance.FlowStep(
                            goal: "Expand scope to forest-level context",
                            suggestedTools: new[] { "ad_forest_discover", "ad_forest_functional" },
                            notes: "Use these when trusts, multiple domains, or forest-wide posture matter."),
                        ToolPackGuidance.FlowStep(
                            goal: "Capture domain controller and resilience facts",
                            suggestedTools: new[] { "ad_domain_controller_facts", "ad_dc_fleet_posture", "ad_fsmo_roles" },
                            notes: "Use this step before operational follow-up when the investigation depends on which DCs matter most.")
                    },
                    verificationTools: new[] { "ad_scope_discovery", "ad_forest_discover", "ad_domain_controller_facts" }),
                ToolPackGuidance.Recipe(
                    id: "authoritative_last_logon_investigation",
                    summary: "Investigate authoritative last-logon activity by enumerating DCs and comparing per-DC LDAP results.",
                    whenToUse: "Use when lastLogonTimestamp is too approximate and the question needs an authoritative per-DC lastLogon answer.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Enumerate the effective DC scope",
                            suggestedTools: new[] { "ad_scope_discovery", "ad_forest_discover" },
                            notes: "Identify the domains and DCs that must participate in the per-DC comparison."),
                        ToolPackGuidance.FlowStep(
                            goal: "Resolve the target identity and gather direct LDAP values",
                            suggestedTools: new[] { "ad_object_resolve", "ad_ldap_query", "ad_ldap_query_paged" },
                            notes: "Query each DC for lastLogon and compare the maximum value rather than trusting replicated approximation fields."),
                        ToolPackGuidance.FlowStep(
                            goal: "Confirm final object context and interpretation",
                            suggestedTools: new[] { "ad_object_get", "ad_search" },
                            notes: "Use direct object detail to verify the final identity, DN, and any adjacent posture needed for the report.")
                    },
                    verificationTools: new[] { "ad_object_get", "ad_ldap_query" },
                    notes: "Treat lastLogonTimestamp as replicated approximation; use the per-DC lastLogon values for the authoritative answer."),
                ToolPackGuidance.Recipe(
                    id: "dc_runtime_health_followup",
                    summary: "Move from discovered AD scope into runtime health probes and same-host system diagnostics for domain controllers.",
                    whenToUse: "Use when AD scope discovery or diagnostics identified a concrete DC and the next step is LDAP/DNS/Kerberos/replication/runtime follow-up.",
                    steps: new[] {
                        ToolPackGuidance.FlowStep(
                            goal: "Validate connectivity and confirm the target DC scope",
                            suggestedTools: new[] { "ad_connectivity_probe", "ad_scope_discovery", "ad_domain_controller_facts", "ad_directory_discovery_diagnostics" }),
                        ToolPackGuidance.FlowStep(
                            goal: "Select and run the appropriate AD monitoring probes",
                            suggestedTools: new[] { "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" },
                            notes: "Use the catalog first when the probe kind depends on the failure shape or when directory sub-kinds matter."),
                        ToolPackGuidance.FlowStep(
                            goal: "Pivot the same host into focused system diagnostics",
                            suggestedTools: new[] { "system_time_sync", "system_metrics_summary", "system_logical_disks_list", "system_backup_posture" },
                            notes: "Reuse the discovered DC host as computer_name so time, runtime, disk, and backup checks stay tied to the same machine.")
                    },
                    verificationTools: new[] { "ad_monitoring_probe_run", "system_time_sync", "system_metrics_summary" })
            },
            toolCatalog: ToolRegistryActiveDirectoryExtensions.GetRegisteredToolCatalog(options),
            runtimeCapabilities: new ToolPackRuntimeCapabilitiesModel {
                PreferredEntryTools = new[] { "ad_environment_discover", "ad_scope_discovery", "ad_forest_discover" },
                PreferredProbeTools = new[] { "ad_connectivity_probe", "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" },
                ProbeHelperFreshnessWindowSeconds = 600,
                SetupHelperFreshnessWindowSeconds = 1800,
                RecipeHelperFreshnessWindowSeconds = 900,
                RuntimePrerequisites = new[] {
                    "Use ad_connectivity_probe when RootDSE reachability, effective LDAP context resolution, or a small DC sample is still uncertain.",
                    "Call ad_environment_discover or ad_scope_discovery first when domain_controller, search_base_dn, or forest/domain boundaries are not explicit yet.",
                    "Persisted AD monitoring state tools require monitoring_directory under one of the configured AllowedMonitoringRoots locations.",
                    "When discovery or monitoring output identifies specific DCs or hosts, reuse those values as computer_name in ComputerX/System follow-up tools instead of improvising a new host scope."
                },
                Notes = "Use ad_connectivity_probe before broader AD discovery when LDAP context is uncertain, then use ad_monitoring_probe_catalog before ad_monitoring_probe_run when the next step depends on probe-specific preflight, follow_up_profiles, or result_signal_profiles."
            },
            rawPayloadPolicy: "Preserve raw engine payloads (including dynamic LDAP attribute bags and nested objects).",
            viewProjectionPolicy: "Projection arguments are optional and view-only; they must not replace raw payload.",
            correlationGuidance: "Correlate users/groups/computers via raw payload fields across multiple AD tools.",
            setupHints: new {
                DomainController = options.DomainController ?? string.Empty,
                SearchBaseDn = options.DefaultSearchBaseDn ?? string.Empty,
                AllowedMonitoringRootsCount = options.AllowedMonitoringRoots.Count,
                Note = "Use ad_environment_discover first to bootstrap context; provide domain_controller/search_base_dn only when discovery cannot reach your target. Persisted monitoring-state tools require monitoring_directory inside AllowedMonitoringRoots."
            });
    }
}
