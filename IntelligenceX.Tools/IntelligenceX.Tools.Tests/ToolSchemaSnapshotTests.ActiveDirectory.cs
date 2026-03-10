using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.Tests;

public partial class ToolSchemaSnapshotTests {
    private static IEnumerable<object[]> ActiveDirectorySchemaSnapshots() {
        yield return new object[] {
            "ad_pack_info",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_delegation_audit",
            new[] { "kind", "enabled_only", "include_spns", "include_allowed_to_delegate_to", "max_values_per_attribute", "search_base_dn", "domain_controller", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_admins_summary",
            new[] { "domain_name", "domain_controller", "search_base_dn", "include_members", "include_nested", "max_results", "users_only", "computers_only" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_controllers",
            new[] { "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_controller_facts",
            new[] { "domain_name", "forest_name", "additional_attributes", "include_attributes", "only_global_catalog", "only_rodc", "timeout_ms", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_controller_security",
            new[] { "domain_name", "forest_name", "domain_controller", "insecure_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_registration_posture",
            new[] { "domain_name", "forest_name", "dns_failed_only", "missing_site_only", "missing_subnet_only", "include_details", "max_detail_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_dc_fleet_posture",
            new[] { "domain_name", "forest_name", "include_details", "max_detail_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_info",
            new[] { "domain_controller" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_forest_functional",
            new[] { "forest_name", "include_domain_overview", "max_domain_rows", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_ds_heuristics",
            new[] { "forest_name", "include_positions", "non_default_only", "max_position_rows", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_laps_schema_posture",
            new[] { "domain_name", "forest_name", "only_findings", "include_details", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_laps_coverage",
            new[] { "domain_name", "forest_name", "coverage_below_percent", "expired_only", "include_samples", "max_sample_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_azuread_sso",
            new[] { "domain_name", "forest_name", "only_present", "risky_only", "include_spns", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_container_defaults",
            new[] { "domain_name", "forest_name", "changed_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_domain_statistics",
            new[] { "domain_name", "forest_name", "include_domain_controllers", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_environment_discover",
            new[] { "domain_controller", "search_base_dn", "include_domain_controllers", "max_domain_controllers", "include_forest_domains", "include_trusts" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_scope_discovery",
            new[] { "forest_name", "domain_name", "domain_controller", "include_domains", "exclude_domains", "include_domain_controllers", "exclude_domain_controllers", "skip_rodc", "include_trusts", "discovery_fallback", "max_domains", "max_domain_controllers_total", "max_domain_controllers_per_domain", "rootdse_timeout_ms", "domain_enumeration_timeout_ms", "dc_source_timeout_ms" },
            new[] { "discovery_fallback" }
        };

        yield return new object[] {
            "ad_forest_discover",
            new[] { "forest_name", "domain_name", "domain_controller", "include_domains", "exclude_domains", "include_domain_controllers", "exclude_domain_controllers", "skip_rodc", "include_trusts", "discovery_fallback", "max_domains", "max_domain_controllers_total", "max_domain_controllers_per_domain", "include_trust_relationships", "include_domain_trust_relationships", "trust_timeout_ms", "max_trusts" },
            new[] { "discovery_fallback" }
        };

        yield return new object[] {
            "ad_gpo_list",
            new[] { "forest_name", "domain_name", "name_contains", "consistency", "link_state", "modified_since_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_gpo_changes",
            new[] { "domain_name", "since_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_health",
            new[] { "domain_name", "gpo_ids", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "gpo_ids" }
        };

        yield return new object[] {
            "ad_gpo_inventory_health",
            new[] { "domain_name", "slice", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_duplicates",
            new[] { "domain_name", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_blocked_inheritance",
            new[] { "domain_name", "only_blocked", "max_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_ou_link_summary",
            new[] { "domain_name", "link_count_at_least", "broken_only", "max_gpos", "max_ous", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_integrity",
            new[] { "domain_name", "sysvol_missing_only", "ad_missing_only", "errors_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_read",
            new[] { "domain_name", "include_compliant", "deny_only", "max_gpos", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_administrative",
            new[] { "domain_name", "include_compliant", "errors_only", "max_gpos", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_consistency",
            new[] { "domain_name", "verify_inheritance", "include_consistent", "top_level_inconsistent_only", "inside_inconsistent_only", "max_gpos", "sysvol_scan_cap", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_unknown",
            new[] { "domain_name", "resolution_error_contains", "inherited_only", "max_gpos", "max_findings", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_root",
            new[] { "domain_name", "permission", "deny_only", "inherited_only", "max_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_permission_report",
            new[] { "domain_name", "gpo_id", "gpo_name", "principal_contains", "permission_type", "max_gpos", "max_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_gpo_redirect",
            new[] { "domain_name", "gpo_ids", "gpo_names", "actual_path_contains", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_fsmo_roles",
            new[] { "domain_name", "include_best_practices", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_group_members",
            new[] { "identity", "search_base_dn", "domain_controller", "max_members" },
            new[] { "identity" }
        };

        yield return new object[] {
            "ad_group_members_resolved",
            new[] { "identity", "search_base_dn", "domain_controller", "include_nested", "max_results", "attributes", "max_values_per_attribute", "columns", "sort_by", "sort_direction", "top" },
            new[] { "identity" }
        };

        yield return new object[] {
            "ad_groups_list",
            new[] { "name_contains", "name_prefix", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "max_results", "page_size", "offset", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_ldap_diagnostics",
            new[] { "servers", "domain_controller", "max_servers", "include_global_catalog", "verify_certificate", "identity", "certificate_include_dns_names", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_ldap_certificates",
            new[] { "servers", "domain_controller", "max_servers", "include_global_catalog", "verify_certificate", "identity", "certificate_include_dns_names", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_ldaps_certificates",
            new[] { "servers", "domain_controller", "max_servers", "include_global_catalog", "verify_certificate", "identity", "certificate_include_dns_names", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_directory_discovery_diagnostics",
            new[] { "forest_name", "domains", "max_issues", "dns_resolve_timeout_ms", "ldap_timeout_ms", "include_dns_srv_comparison", "include_host_resolution", "include_directory_topology", "as_issue", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_dangerous_extended_rights",
            new[] { "domain_name", "forest_name", "include_findings", "max_findings_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_dc_shadow_indicators",
            new[] { "domain_name", "forest_name", "include_findings", "max_findings_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_dns_scavenging",
            new[] { "dns_server", "mismatched_only", "stale_only", "scavenging_enabled_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "dns_server" }
        };

        yield return new object[] {
            "ad_dns_server_config",
            new[] { "dns_servers", "domain_name", "forest_name", "recursion_disabled_only", "missing_forwarders_only", "max_servers", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_dns_zone_config",
            new[] { "dns_server", "zone_name_contains", "dynamic_updates_only", "insecure_updates_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "dns_server" }
        };

        yield return new object[] {
            "ad_dns_zone_security",
            new[] { "domain_name", "forest_name", "exposed_only", "broad_write_min", "include_offending_principals", "max_offending_rows", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_dns_delegation",
            new[] { "domain_name", "forest_name", "zone_name_contains", "identity_contains", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_monitoring_probe_catalog",
            Array.Empty<string>(),
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_monitoring_probe_run",
            new[] { "bind_identity", "bind_secret", "columns", "degraded_above_ms", "directory_allow_authenticated_fallback", "directory_allowed_shares", "directory_attribute", "directory_dns_servers", "directory_exclude_sites", "directory_filter", "directory_ignore_drive_shares", "directory_include_forest_roles", "directory_optional_shares", "directory_probe_kind", "directory_query_name", "directory_query_timeout_ms", "directory_require_global_catalog_ready", "directory_require_synchronized", "directory_required_shares", "directory_search_base", "directory_share_name", "directory_sites", "directory_use_all_dns_servers", "directory_use_anonymous_bind", "directory_use_ldaps", "directory_use_start_tls", "directory_zones", "discovery_fallback", "dns_queries", "dns_service_query_name", "dns_service_record_type", "dns_service_require_answers", "domain_controller", "domain_name", "exclude_domain_controllers", "exclude_domains", "forest_name", "identity", "include_children", "include_domain_controllers", "include_domains", "include_facts", "include_global_catalog", "include_sysvol", "include_trusts", "include_udp", "latency_threshold_ms", "loss_threshold_percent", "max_concurrency", "name", "p95_latency_threshold_ms", "path", "port", "probe_kind", "protocol", "query_mode", "request_timeout_ms", "require_wsus", "retries", "retry_delay_ms", "skip_rodc", "sort_by", "sort_direction", "split_protocol_results", "stale_threshold_hours", "targets", "tcp_ports", "test_ping", "test_ports", "test_sysvol_shares", "timeout_ms", "top", "total_budget_ms", "udp_ports", "url", "use_ad_core_profile", "verify_certificate" },
            new[] { "probe_kind" }
        };

        yield return new object[] {
            "ad_krbtgt_health",
            new[] { "domain_name", "forest_name", "age_threshold_days", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_kerberos_crypto_posture",
            new[] { "domain_name", "forest_name", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_machine_account_quota",
            new[] { "domain_name", "forest_name", "threshold", "risky_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_lan_manager_settings",
            new[] { "domain_name", "forest_name", "domain_controllers", "allow_lm_hash_only", "legacy_ntlm_only", "max_domain_controllers", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_legacy_cve_exposure",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_firewall_profiles",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_time_service_configuration",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_llmnr_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_wdigest_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_winrm_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_proxy_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_schannel_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_terminal_services_redirection_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_terminal_services_timeout_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_name_resolution_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_lsa_protection_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_net_session_hardening_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_limit_blank_password_use_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_pku2u_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_hardened_paths_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_kdc_proxy_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_kerberos_pac_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_powershell_logging_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_no_lm_hash_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_ntlm_restrictions_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_restrict_ntlm_configuration",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_logon_ux_uac_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_deny_logon_rights_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_defender_asr_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_everyone_includes_anonymous_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_enable_delegation_privilege_policy",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_client_server_auth_posture",
            new[] { "domain_name", "include_attribution", "configured_attribution_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_ldap_query",
            new[] { "ldap_filter", "scope", "search_base_dn", "domain_controller", "attributes", "allow_sensitive_attributes", "max_attributes", "max_values_per_attribute", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "ldap_filter" }
        };

        yield return new object[] {
            "ad_duplicate_accounts",
            new[] { "domain_name", "forest_name", "include_conflict_dns", "include_duplicate_details", "conflicts_only", "duplicates_only", "max_detail_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_ldap_query_paged",
            new[] { "ldap_filter", "scope", "search_base_dn", "domain_controller", "attributes", "allow_sensitive_attributes", "max_attributes", "max_values_per_attribute", "page_size", "max_pages", "max_results", "cursor", "timeout_ms", "columns", "sort_by", "sort_direction", "top" },
            new[] { "ldap_filter" }
        };

        yield return new object[] {
            "ad_object_resolve",
            new[] { "identities", "identity_kind", "kind", "search_base_dn", "domain_controller", "attributes", "max_inputs", "max_values_per_attribute", "columns", "sort_by", "sort_direction", "top" },
            new[] { "identities" }
        };

        yield return new object[] {
            "ad_handoff_prepare",
            new[] { "entity_handoff", "include_computers", "max_identities", "min_candidate_count" },
            new[] { "entity_handoff" }
        };

        yield return new object[] {
            "ad_object_get",
            new[] { "identity", "kind", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute" },
            new[] { "identity" }
        };

        yield return new object[] {
            "ad_ou_protection",
            new[] { "domain_name", "forest_name", "unprotected_only", "include_unprotected_ous", "max_ou_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_privileged_groups_summary",
            new[] { "domain_name", "domain_controller", "search_base_dn", "include_member_count", "include_member_sample", "member_sample_size" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_replication_summary",
            new[] { "domain_controller", "domain_name", "forest_name", "outbound", "by_source", "stale_threshold_hours", "bucket_hours", "include_details", "max_details", "max_domain_controllers", "max_errors", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_replication_connections",
            new[] { "server", "server_match", "site", "site_match", "source_server", "source_server_match", "transport", "state", "origin", "summary", "summary_by", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_replication_status",
            new[] { "computer_names", "health_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_recycle_bin_lifetime",
            new[] { "forest_name" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_null_session_posture",
            new[] { "domain_name", "forest_name", "domain_controllers", "anonymous_sam_only", "null_session_only", "max_domain_controllers", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_shadow_credentials_risk",
            new[] { "domain_name", "forest_name", "include_findings", "max_findings_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_password_policy",
            new[] { "forest_name", "domain_name", "include_fine_grained", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_password_policy_rollup",
            new[] { "domain_name", "forest_name", "pso_min_length", "pso_history_min", "include_pso_details", "max_pso_rows_per_domain", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_password_policy_length",
            new[] { "domain_name", "forest_name", "recommended_minimum_length", "below_recommended_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_pki_templates",
            new[] { "forest_name", "weak_key_only", "takeover_risk_only", "code_signing_risk_only", "client_auth_risk_only", "include_takeover_rows", "max_results", "max_takeover_rows", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_pki_posture",
            new[] { "forest_name", "include_details", "insecure_endpoints_only", "max_details_per_category", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_schema_version",
            new[] { "mismatched_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_sites",
            new[] { "forest_name", "include_subnets", "include_options", "no_dc_only", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_subnets",
            new[] { "forest_name", "summary", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_site_links",
            new[] { "forest_name", "summary", "has_schedule", "options_all", "expand_schedule", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_site_coverage",
            new[] { "forest_name", "include_registry", "raw", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_trust",
            new[] { "forest_name", "recursive", "skip_validation", "status", "inactive_days", "old_protocol", "impermeability", "trust_type", "direction", "summary", "summary_by", "summary_matrix", "include_communication_issues", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_search",
            new[] { "query", "kind", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "query" }
        };

        yield return new object[] {
            "ad_search_facets",
            new[] { "ldap_filter", "kind", "search_text", "scope", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "page_size", "max_pages", "max_results", "max_facet_values", "facet_by_container", "container_facet_mode", "container_ou_depth", "facet_by_enabled", "facet_uac_flags", "facet_pwd_age_buckets_days", "include_samples", "sample_size", "timeout_ms" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_spn_search",
            new[] { "spn_contains", "spn_exact", "kind", "enabled_only", "search_base_dn", "domain_controller", "attributes", "max_values_per_attribute", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_stale_accounts",
            new[] { "kind", "enabled_only", "exclude_critical", "days_since_logon", "days_since_password_set", "match", "search_base_dn", "domain_controller", "max_results" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_never_logged_in_accounts",
            new[] { "domain_name", "grace_period_days", "reference_time_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_service_account_usage",
            new[] { "domain_name", "account_type", "used_only", "some_computers_stale_only", "all_computers_stale_only", "include_principals", "include_principal_infos", "max_results", "columns", "sort_by", "sort_direction", "top" },
            new[] { "domain_name" }
        };

        yield return new object[] {
            "ad_kds_root_keys",
            new[] { "effective_only", "not_effective_only", "reference_time_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        yield return new object[] {
            "ad_admin_count_report",
            new[] { "forest_name_contains", "domain_name_contains", "sam_account_name_contains", "stale_days", "reference_time_utc", "max_results", "columns", "sort_by", "sort_direction", "top" },
            Array.Empty<string>()
        };

        foreach (var snapshot in ActiveDirectorySchemaSnapshotsTail()) {
            yield return snapshot;
        }
    }
}
