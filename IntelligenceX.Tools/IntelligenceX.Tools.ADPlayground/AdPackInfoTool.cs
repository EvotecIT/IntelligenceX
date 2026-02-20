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
                "Use ad_scope_discovery for explicit scope + receipt output (forest/domain, naming contexts, domains/DCs, and missing-reason diagnostics).",
                "Use ad_forest_discover to make forest scope explicit and get a receipt (domains/trusts/DCs discovered and how).",
                "Use ad_forest_functional for forest-level functional posture and recommended target-level planning.",
                "Use ad_search/ad_groups_list/ad_spn_search for broad discovery.",
                "Use ad_password_policy/ad_password_policy_rollup/ad_password_policy_length and ad_trust for policy and trust-posture diagnostics.",
                "Use ad_domain_statistics/ad_domain_controller_facts/ad_dc_fleet_posture/ad_fsmo_roles/ad_krbtgt_health/ad_system_state_backup for domain resilience posture checks.",
                "Use ad_domain_container_defaults/ad_machine_account_quota for domain default and join-governance posture.",
                "Use ad_client_server_auth_posture/ad_legacy_cve_exposure/ad_firewall_profiles/ad_time_service_configuration/ad_llmnr_policy/ad_wdigest_policy/ad_winrm_policy/ad_proxy_policy/ad_schannel_policy/ad_terminal_services_redirection_policy/ad_terminal_services_timeout_policy/ad_name_resolution_policy/ad_lsa_protection_policy/ad_net_session_hardening_policy/ad_limit_blank_password_use_policy/ad_pku2u_policy/ad_hardened_paths_policy/ad_kdc_proxy_policy/ad_kerberos_pac_policy/ad_powershell_logging_policy/ad_no_lm_hash_policy/ad_ntlm_restrictions_policy/ad_restrict_ntlm_configuration/ad_logon_ux_uac_policy/ad_deny_logon_rights_policy/ad_defender_asr_policy/ad_everyone_includes_anonymous_policy/ad_enable_delegation_privilege_policy/ad_kerberos_crypto_posture/ad_lan_manager_settings/ad_null_session_posture/ad_domain_controller_security/ad_ds_heuristics/ad_laps_schema_posture/ad_azuread_sso for credential, schema, and endpoint hardening posture.",
                "Use ad_duplicate_accounts/ad_ou_protection/ad_registration_posture/ad_laps_coverage for AD hygiene and control-plane registration coverage.",
                "Use ad_shadow_credentials_risk/ad_dc_shadow_indicators/ad_dangerous_extended_rights/ad_smartcard_posture for ACL and identity abuse-path diagnostics.",
                "Use ad_pki_templates/ad_pki_posture for certificate template and PKI endpoint risk posture.",
                "Use ad_sites/ad_subnets/ad_site_links for AD topology inventory and schedule diagnostics.",
                "Use ad_site_coverage for per-site subnet/DC coverage and orphaned subnet visibility.",
                "Use ad_dns_server_config/ad_dns_zone_config/ad_dns_zone_security/ad_dns_delegation/ad_dns_scavenging for DNS server/zone/delegation posture diagnostics.",
                "Use ad_gpo_list/ad_gpo_changes/ad_gpo_health/ad_gpo_inventory_health/ad_gpo_duplicates/ad_gpo_blocked_inheritance/ad_gpo_ou_link_summary/ad_gpo_integrity/ad_gpo_redirect/ad_gpo_permission_read/ad_gpo_permission_administrative/ad_gpo_permission_consistency/ad_gpo_permission_unknown/ad_gpo_permission_root/ad_gpo_permission_report for GPO inventory, timeline, topology, and permission hygiene diagnostics.",
                "Use ad_handoff_prepare to normalize cross-pack entity_handoff payloads before AD queries.",
                "Example EventLog handoff flow: ad_handoff_prepare -> ad_object_resolve -> ad_search/ad_object_get for focused follow-up.",
                "Use ad_object_resolve to avoid N+1 object lookups when correlating identities.",
                "Use ad_ldap_query_paged for large exploratory queries and continue with cursor.",
                "Use ad_search_facets/ad_replication_summary/ad_replication_connections/ad_replication_status/ad_directory_discovery_diagnostics/ad_dns_server_config/ad_dns_zone_config/ad_dns_zone_security/ad_dns_delegation/ad_delegation_audit/ad_spn_stats for aggregated diagnostics.",
                "Use ad_monitoring_probe_catalog + ad_monitoring_probe_run for runtime AD monitoring probes (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping)."
            },
            flowSteps: new[] {
                ToolPackGuidance.FlowStep(
                    goal: "Discover candidate AD objects",
                    suggestedTools: new[] { "ad_search", "ad_groups_list", "ad_spn_search" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess GPO inventory, changes, and health",
                    suggestedTools: new[] { "ad_gpo_list", "ad_gpo_changes", "ad_gpo_health", "ad_gpo_inventory_health", "ad_gpo_duplicates", "ad_gpo_blocked_inheritance", "ad_gpo_ou_link_summary", "ad_gpo_integrity", "ad_gpo_redirect", "ad_gpo_permission_read", "ad_gpo_permission_administrative", "ad_gpo_permission_consistency", "ad_gpo_permission_unknown", "ad_gpo_permission_root", "ad_gpo_permission_report" }),
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
                    suggestedTools: new[] { "ad_duplicate_accounts", "ad_ou_protection", "ad_registration_posture", "ad_laps_coverage" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess ACL and identity abuse-path exposure",
                    suggestedTools: new[] { "ad_shadow_credentials_risk", "ad_dc_shadow_indicators", "ad_dangerous_extended_rights", "ad_smartcard_posture" }),
                ToolPackGuidance.FlowStep(
                    goal: "Assess PKI posture (templates and enrollment endpoint security)",
                    suggestedTools: new[] { "ad_pki_templates", "ad_pki_posture" }),
                ToolPackGuidance.FlowStep(
                    goal: "Resolve/expand identities for correlation",
                    suggestedTools: new[] { "ad_handoff_prepare", "ad_object_resolve", "ad_object_get", "ad_group_members_resolved" }),
                ToolPackGuidance.FlowStep(
                    goal: "Run diagnostics and aggregate analysis",
                    suggestedTools: new[] { "ad_search_facets", "ad_replication_summary", "ad_replication_connections", "ad_replication_status", "ad_directory_discovery_diagnostics", "ad_dns_server_config", "ad_dns_zone_config", "ad_dns_zone_security", "ad_dns_delegation", "ad_delegation_audit", "ad_spn_stats", "ad_spn_hygiene", "ad_ldap_diagnostics", "ad_dns_scavenging" }),
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
                    primaryTools: new[] { "ad_handoff_prepare", "ad_object_resolve", "ad_object_get", "ad_group_members", "ad_group_members_resolved" }),
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
                    summary: "Inspect duplicate principals, OU deletion-protection posture, DC registration gaps, and LAPS coverage hygiene.",
                    primaryTools: new[] { "ad_duplicate_accounts", "ad_ou_protection", "ad_registration_posture", "ad_laps_coverage" }),
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
                    primaryTools: new[] { "ad_gpo_list", "ad_gpo_changes", "ad_gpo_health", "ad_gpo_inventory_health", "ad_gpo_duplicates", "ad_gpo_blocked_inheritance", "ad_gpo_ou_link_summary", "ad_gpo_integrity", "ad_gpo_redirect", "ad_gpo_permission_read", "ad_gpo_permission_administrative", "ad_gpo_permission_consistency", "ad_gpo_permission_unknown", "ad_gpo_permission_root", "ad_gpo_permission_report" }),
                ToolPackGuidance.Capability(
                    id: "ad_runtime_monitoring",
                    summary: "Run ADPlayground.Monitoring probes (ldap/dns/kerberos/ntp/replication/port/https/dns_service/adws/directory/ping) for server/domain/forest scope.",
                    primaryTools: new[] { "ad_monitoring_probe_catalog", "ad_monitoring_probe_run" })
            },
            entityHandoffs: new[] {
                ToolPackGuidance.EntityHandoff(
                    id: "external_identity_to_ad_resolution",
                    summary: "Consume identity/host indicators from other packs and normalize them for AD object resolution.",
                    entityKinds: new[] { "identity", "user", "group", "computer", "host" },
                    sourceTools: new[] { "eventlog_named_events_query", "eventlog_timeline_query", "system_whoami", "powershell_run" },
                    targetTools: new[] { "ad_handoff_prepare", "ad_object_resolve", "ad_search", "ad_object_get", "ad_group_members_resolved" },
                    fieldMappings: new[] {
                        ToolPackGuidance.EntityFieldMapping("*.who", "identities", "Batch and deduplicate values for ad_object_resolve."),
                        ToolPackGuidance.EntityFieldMapping("*.object_affected", "identities", "Batch and deduplicate values for ad_object_resolve."),
                        ToolPackGuidance.EntityFieldMapping("*.computer", "identity", "Use hostname/FQDN as identity for ad_search/ad_object_get."),
                        ToolPackGuidance.EntityFieldMapping("identity.account_name", "identity", "Use as direct identity input for ad_search/ad_object_get."),
                        ToolPackGuidance.EntityFieldMapping("resolved[].distinguished_name", "identity", "Use direct DN for follow-up detail lookups.")
                    },
                    notes: "Prefer ad_object_resolve for bulk correlation to avoid N+1 lookups.")
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
