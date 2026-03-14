using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryRoutingCatalog {
    public static readonly IReadOnlyList<string> SignalTokens = new[] {
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
    };

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName = BuildDeclaredRolesByToolName();
    private static readonly IReadOnlyDictionary<string, FallbackDescriptor> FallbackDescriptorsByToolName =
        new Dictionary<string, FallbackDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["ad_monitoring_dashboard_state_get"] = new(
                SelectionKeys: new[] { "monitoring_directory" },
                HintKeys: new[] { "monitoring_directory" }),
            ["ad_monitoring_metrics_get"] = new(
                SelectionKeys: new[] { "monitoring_directory" },
                HintKeys: new[] { "monitoring_directory" }),
            ["ad_monitoring_diagnostics_get"] = new(
                SelectionKeys: new[] { "monitoring_directory" },
                HintKeys: new[] { "monitoring_directory", "include_slow_probes", "max_slow_probes" }),
            ["ad_monitoring_service_heartbeat_get"] = new(
                SelectionKeys: new[] { "monitoring_directory" },
                HintKeys: new[] { "monitoring_directory" })
        };

    private static readonly IReadOnlyDictionary<string, SelectionDescriptor> ExplicitSelectionDescriptors =
        new Dictionary<string, SelectionDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["ad_search"] = new(
                Scope: "domain",
                Operation: "search",
                Entity: "directory_object",
                Risk: ToolRoutingTaxonomy.RiskMedium,
                AdditionalTags: new[] { "identity", "handoff_consumer" }),
            ["ad_object_resolve"] = new(
                Scope: "domain",
                Operation: "resolve",
                Entity: "directory_object",
                Risk: ToolRoutingTaxonomy.RiskMedium,
                AdditionalTags: new[] { "identity", "handoff_consumer" }),
            ["ad_handoff_prepare"] = new(
                Scope: "domain",
                Operation: "transform",
                Entity: "identity",
                Risk: ToolRoutingTaxonomy.RiskLow,
                AdditionalTags: new[] { "handoff", "normalization" })
        };

    private static readonly IReadOnlySet<string> OperationalToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "ad_monitoring_probe_run",
        "ad_whoami",
        "ad_object_get",
        "ad_handoff_prepare",
        "ad_group_members",
        "ad_group_members_resolved"
    };

    private static readonly IReadOnlySet<string> KnownToolNames = BuildKnownToolNames();

    public static ToolDefinition ApplySelectionMetadata(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        return ExplicitSelectionDescriptors.TryGetValue((definition.Name ?? string.Empty).Trim(), out var descriptor)
            ? ToolExplicitSelectionMetadata.Apply(
                definition,
                scope: descriptor.Scope,
                operation: descriptor.Operation,
                entity: descriptor.Entity,
                risk: descriptor.Risk,
                additionalTags: descriptor.AdditionalTags)
            : definition;
    }

    public static string ResolveRole(string toolName, string? explicitRole) {
        if (!string.IsNullOrWhiteSpace(explicitRole)) {
            return ToolRoutingRoleResolver.ResolveExplicitOrFallback(
                explicitRole: explicitRole,
                fallbackRole: ToolRoutingTaxonomy.RoleOperational,
                packDisplayName: "ActiveDirectory");
        }

        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (DeclaredRolesByToolName.TryGetValue(normalizedToolName, out var declaredRole)) {
            return declaredRole;
        }

        if (KnownToolNames.Contains(normalizedToolName)) {
            return ToolRoutingTaxonomy.RoleOperational;
        }

        throw new InvalidOperationException(
            $"ActiveDirectory tool '{normalizedToolName}' must declare an explicit routing role or be added to the known-tool role catalog.");
    }

    public static IReadOnlyList<string> ResolveFallbackSelectionKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return TryGetFallbackDescriptor(toolName, out var descriptor)
            ? descriptor.SelectionKeys
            : Array.Empty<string>();
    }

    public static IReadOnlyList<string> ResolveFallbackHintKeys(string toolName, IReadOnlyList<string>? explicitKeys) {
        if (explicitKeys is { Count: > 0 }) {
            return explicitKeys;
        }

        return TryGetFallbackDescriptor(toolName, out var descriptor)
            ? descriptor.HintKeys
            : Array.Empty<string>();
    }

    public static bool RequiresSelectionForFallback(bool explicitRequiresSelection, IReadOnlyList<string> fallbackSelectionKeys) {
        return explicitRequiresSelection || fallbackSelectionKeys.Count > 0;
    }

    private static IReadOnlySet<string> BuildKnownToolNames() {
        var known = new HashSet<string>(DeclaredRolesByToolName.Keys, StringComparer.OrdinalIgnoreCase);
        known.UnionWith(OperationalToolNames);
        return known;
    }

    private static IReadOnlyDictionary<string, string> BuildDeclaredRolesByToolName() {
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddRoleGroup(declared, ToolRoutingTaxonomy.RolePackInfo,
            "ad_pack_info");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            "ad_environment_discover",
            "ad_scope_discovery",
            "ad_forest_discover");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleResolver,
            "ad_search",
            "ad_object_resolve",
            "ad_ldap_query",
            "ad_ldap_query_paged",
            "ad_spn_search",
            "ad_dns_server_config",
            "ad_dns_zone_config",
            "ad_dns_zone_security",
            "ad_dns_delegation",
            "ad_dns_scavenging");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleDiagnostic,
            "ad_gpo_list",
            "ad_gpo_changes",
            "ad_gpo_health",
            "ad_gpo_permission_read",
            "ad_gpo_permission_administrative",
            "ad_gpo_permission_consistency",
            "ad_gpo_permission_unknown",
            "ad_gpo_permission_root",
            "ad_gpo_permission_report",
            "ad_gpo_inventory_health",
            "ad_gpo_duplicates",
            "ad_gpo_blocked_inheritance",
            "ad_gpo_ou_link_summary",
            "ad_gpo_redirect",
            "ad_gpo_integrity",
            "ad_wmi_filters",
            "ad_wsus_configuration",
            "ad_domain_info",
            "ad_forest_functional",
            "ad_ds_heuristics",
            "ad_laps_schema_posture",
            "ad_azuread_sso",
            "ad_domain_statistics",
            "ad_domain_container_defaults",
            "ad_domain_controller_facts",
            "ad_domain_controller_security",
            "ad_dc_fleet_posture",
            "ad_registration_posture",
            "ad_domain_controllers",
            "ad_fsmo_roles",
            "ad_client_server_auth_posture",
            "ad_legacy_cve_exposure",
            "ad_firewall_profiles",
            "ad_time_service_configuration",
            "ad_llmnr_policy",
            "ad_wdigest_policy",
            "ad_winrm_policy",
            "ad_proxy_policy",
            "ad_schannel_policy",
            "ad_terminal_services_redirection_policy",
            "ad_terminal_services_timeout_policy",
            "ad_name_resolution_policy",
            "ad_lsa_protection_policy",
            "ad_net_session_hardening_policy",
            "ad_limit_blank_password_use_policy",
            "ad_pku2u_policy",
            "ad_hardened_paths_policy",
            "ad_kdc_proxy_policy",
            "ad_kerberos_pac_policy",
            "ad_powershell_logging_policy",
            "ad_no_lm_hash_policy",
            "ad_ntlm_restrictions_policy",
            "ad_restrict_ntlm_configuration",
            "ad_logon_ux_uac_policy",
            "ad_deny_logon_rights_policy",
            "ad_defender_asr_policy",
            "ad_everyone_includes_anonymous_policy",
            "ad_enable_delegation_privilege_policy",
            "ad_lan_manager_settings",
            "ad_machine_account_quota",
            "ad_duplicate_accounts",
            "ad_ou_protection",
            "ad_laps_coverage",
            "ad_kerberos_crypto_posture",
            "ad_spn_stats",
            "ad_spn_hygiene",
            "ad_groups_list",
            "ad_recycle_bin_lifetime",
            "ad_delegation_audit",
            "ad_privileged_groups_summary",
            "ad_domain_admins_summary",
            "ad_stale_accounts",
            "ad_never_logged_in_accounts",
            "ad_service_account_usage",
            "ad_kds_root_keys",
            "ad_admin_count_report",
            "ad_krbtgt_health",
            "ad_ldap_diagnostics",
            "ad_directory_discovery_diagnostics",
            "ad_monitoring_probe_catalog",
            "ad_monitoring_service_heartbeat_get",
            "ad_monitoring_diagnostics_get",
            "ad_monitoring_metrics_get",
            "ad_monitoring_dashboard_state_get",
            "ad_replication_summary",
            "ad_replication_connections",
            "ad_replication_status",
            "ad_password_policy",
            "ad_password_policy_rollup",
            "ad_password_policy_length",
            "ad_schema_version",
            "ad_null_session_posture",
            "ad_shadow_credentials_risk",
            "ad_dc_shadow_indicators",
            "ad_dangerous_extended_rights",
            "ad_smartcard_posture",
            "ad_pki_templates",
            "ad_pki_posture",
            "ad_sites",
            "ad_subnets",
            "ad_site_links",
            "ad_site_coverage",
            "ad_trust",
            "ad_system_state_backup",
            "ad_search_facets",
            "ad_users_expired");

        return declared;
    }

    private static void AddRoleGroup(
        IDictionary<string, string> declared,
        string role,
        params string[] toolNames) {
        foreach (var toolName in toolNames) {
            declared[toolName] = role;
        }
    }

    private static bool TryGetFallbackDescriptor(string toolName, out FallbackDescriptor descriptor) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (FallbackDescriptorsByToolName.TryGetValue(normalizedToolName, out var resolvedDescriptor)) {
            descriptor = resolvedDescriptor;
            return true;
        }

        descriptor = null!;
        return false;
    }

    private sealed record SelectionDescriptor(
        string Scope,
        string Operation,
        string Entity,
        string Risk,
        string[] AdditionalTags);

    private sealed record FallbackDescriptor(
        string[] SelectionKeys,
        string[] HintKeys);
}
