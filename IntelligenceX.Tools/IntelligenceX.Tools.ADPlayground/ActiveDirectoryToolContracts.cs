using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ad_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["ad_environment_discover"] = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            ["ad_scope_discovery"] = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            ["ad_forest_discover"] = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            ["ad_directory_discovery_diagnostics"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_ldap_diagnostics"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_monitoring_probe_catalog"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_monitoring_probe_run"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_monitoring_service_heartbeat_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_monitoring_diagnostics_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_monitoring_metrics_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_monitoring_dashboard_state_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_dns_server_config"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_dns_zone_config"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_dns_zone_security"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_dns_delegation"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_dns_scavenging"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_gpo_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_changes"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_health"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_permission_read"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_permission_administrative"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_permission_consistency"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_permission_unknown"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_permission_root"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_permission_report"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_inventory_health"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_duplicates"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_blocked_inheritance"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_ou_link_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_redirect"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_gpo_integrity"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_wmi_filters"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_wsus_configuration"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_info"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_forest_functional"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_ds_heuristics"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_laps_schema_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_azuread_sso"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_statistics"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_container_defaults"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_controller_facts"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_controller_security"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_dc_fleet_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_registration_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_controllers"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_fsmo_roles"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_client_server_auth_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_legacy_cve_exposure"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_firewall_profiles"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_time_service_configuration"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_llmnr_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_wdigest_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_winrm_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_proxy_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_schannel_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_terminal_services_redirection_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_terminal_services_timeout_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_name_resolution_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_lsa_protection_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_net_session_hardening_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_limit_blank_password_use_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_pku2u_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_hardened_paths_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_kdc_proxy_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_kerberos_pac_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_powershell_logging_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_no_lm_hash_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_ntlm_restrictions_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_restrict_ntlm_configuration"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_logon_ux_uac_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_deny_logon_rights_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_defender_asr_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_everyone_includes_anonymous_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_enable_delegation_privilege_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_lan_manager_settings"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_machine_account_quota"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_duplicate_accounts"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_ou_protection"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_laps_coverage"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_kerberos_crypto_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_spn_search"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_spn_stats"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_spn_hygiene"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_groups_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_whoami"] = ToolRoutingTaxonomy.RoleOperational,
            ["ad_recycle_bin_lifetime"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_object_get"] = ToolRoutingTaxonomy.RoleOperational,
            ["ad_object_resolve"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_handoff_prepare"] = ToolRoutingTaxonomy.RoleOperational,
            ["ad_delegation_audit"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_privileged_groups_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_domain_admins_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_stale_accounts"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_never_logged_in_accounts"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_service_account_usage"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_kds_root_keys"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_admin_count_report"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_krbtgt_health"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_ldap_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_ldap_query_paged"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_replication_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_replication_connections"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_replication_status"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_password_policy"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_password_policy_rollup"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_password_policy_length"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_schema_version"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_null_session_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_shadow_credentials_risk"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_dc_shadow_indicators"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_dangerous_extended_rights"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_smartcard_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_pki_templates"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_pki_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_sites"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_subnets"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_site_links"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_site_coverage"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_trust"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_system_state_backup"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_search_facets"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["ad_search"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_group_members"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_group_members_resolved"] = ToolRoutingTaxonomy.RoleResolver,
            ["ad_users_expired"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

    private static readonly string[] DomainSignalTokens = {
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

    private static readonly string[] SetupHintKeys = {
        "domain_controller",
        "search_base_dn",
        "domain_name",
        "forest_name"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
        return ToolDefinitionOverlay.WithDefinition(tool, updatedDefinition);
    }

    private static ToolRoutingContract BuildRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = string.IsNullOrWhiteSpace(existing?.RoutingContractId)
                ? ToolRoutingContract.DefaultContractId
                : existing!.RoutingContractId,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = "active_directory",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
            DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : DomainSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateSetup(
                setupToolName: "ad_environment_discover",
                requirements: BuildDefaultSetupRequirements(),
                setupHintKeys: SetupHintKeys));
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "context/domain_controller",
                fallbackSourceField: "domain_controllers/0/value",
                reason: "Promote discovered AD domain-controller context into remote ComputerX host diagnostics.");
        }

        if (string.Equals(definition.Name, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Name, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Name, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "domain_controllers/0/value",
                fallbackSourceField: "requested_scope/domain_controller",
                reason: "Promote discovered AD domain-controller inventory into remote ComputerX host diagnostics.");
        }

        if (string.Equals(definition.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "normalized_request/domain_controller",
                fallbackSourceField: "normalized_request/targets/0",
                reason: "Promote AD monitoring probe targets into remote ComputerX follow-up diagnostics.");
        }

        if (!string.Equals(definition.Name, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase)) {
            return definition.Handoff;
        }

        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_object_resolve",
                reason: "Use normalized identities from handoff payload for batched AD object resolution.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("target_arguments/ad_object_resolve/identities", "identities")
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_scope_discovery",
                reason: "Use discovered domain hints to bootstrap AD scope before resolution calls.",
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("target_arguments/ad_scope_discovery/domain_name", "domain_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("target_arguments/ad_scope_discovery/include_domain_controllers", "include_domain_controllers", isRequired: false)
                })
        });
    }

    private static ToolHandoffContract CreateSystemHostPivotHandoff(
        string primarySourceField,
        string fallbackSourceField,
        string reason) {
        return ToolContractDefaults.CreateHandoff(SystemRemoteHostFollowUpCatalog.CreateComputerTargetRoutes(
            sourceFields: new[] { primarySourceField, fallbackSourceField },
            primaryReasonOverride: reason,
            isRequired: false));
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateRecovery(
                supportsTransientRetry: true,
                maxRetryAttempts: 1,
                retryableErrorCodes: new[] { "timeout", "query_failed", "probe_failed", "discovery_failed", "transport_unavailable" },
                recoveryToolNames: new[] { "ad_environment_discover" }));
    }

    private static IReadOnlyList<ToolSetupRequirement> BuildDefaultSetupRequirements() {
        return new[] {
            ToolContractDefaults.CreateRequirement(
                requirementId: "ad_directory_context",
                requirementKind: ToolSetupRequirementKinds.Configuration,
                hintKeys: SetupHintKeys),
            ToolContractDefaults.CreateRequirement(
                requirementId: "ad_ldap_connectivity",
                requirementKind: ToolSetupRequirementKinds.Connectivity,
                hintKeys: new[] { "domain_controller", "domain_name", "forest_name" })
        };
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "Active Directory");
    }
}
