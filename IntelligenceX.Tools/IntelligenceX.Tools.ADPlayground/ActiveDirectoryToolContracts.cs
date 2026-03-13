using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName = BuildDeclaredRolesByToolName();
    private static readonly IReadOnlySet<string> KnownToolNames = BuildKnownToolNames();

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

    private static IReadOnlySet<string> BuildKnownToolNames() {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "ad_pack_info",
            "ad_environment_discover",
            "ad_scope_discovery",
            "ad_forest_discover",
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
            "ad_spn_search",
            "ad_spn_stats",
            "ad_spn_hygiene",
            "ad_groups_list",
            "ad_whoami",
            "ad_recycle_bin_lifetime",
            "ad_object_get",
            "ad_object_resolve",
            "ad_handoff_prepare",
            "ad_delegation_audit",
            "ad_privileged_groups_summary",
            "ad_domain_admins_summary",
            "ad_stale_accounts",
            "ad_never_logged_in_accounts",
            "ad_service_account_usage",
            "ad_kds_root_keys",
            "ad_admin_count_report",
            "ad_krbtgt_health",
            "ad_ldap_query",
            "ad_ldap_query_paged",
            "ad_ldap_diagnostics",
            "ad_directory_discovery_diagnostics",
            "ad_dns_server_config",
            "ad_dns_zone_config",
            "ad_dns_zone_security",
            "ad_dns_delegation",
            "ad_dns_scavenging",
            "ad_monitoring_probe_catalog",
            "ad_monitoring_probe_run",
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
            "ad_search",
            "ad_group_members",
            "ad_group_members_resolved",
            "ad_users_expired"
        };
    }

    private static void AddRoleGroup(
        IDictionary<string, string> declared,
        string role,
        params string[] toolNames) {
        foreach (var toolName in toolNames) {
            declared[toolName] = role;
        }
    }

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var execution = BuildExecution(definition, routing);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            execution: execution,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
        return ToolDefinitionOverlay.WithDefinition(tool, updatedDefinition);
    }

    private static ToolExecutionContract? BuildExecution(ToolDefinition definition, ToolRoutingContract? routing) {
        if (definition.Execution is { IsExecutionAware: true }) {
            return definition.Execution;
        }

        if (routing is not null
            && string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Execution;
        }

        var traits = ToolExecutionTraitProjection.Project(definition);
        return new ToolExecutionContract {
            IsExecutionAware = true,
            ExecutionScope = traits.ExecutionScope,
            TargetScopeArguments = traits.TargetScopeArguments,
            RemoteHostArguments = traits.RemoteHostArguments
        };
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
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Setup;
        }

        if (definition.Setup is { IsSetupAware: true }) {
            return definition.Setup;
        }

        return new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = "ad_environment_discover",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "ad_directory_context",
                    Kind = ToolSetupRequirementKinds.Configuration,
                    IsRequired = true,
                    HintKeys = SetupHintKeys
                },
                new ToolSetupRequirement {
                    RequirementId = "ad_ldap_connectivity",
                    Kind = ToolSetupRequirementKinds.Connectivity,
                    IsRequired = true,
                    HintKeys = new[] { "domain_controller", "domain_name", "forest_name" }
                }
            },
            SetupHintKeys = SetupHintKeys
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (definition.Handoff is { IsHandoffAware: true }) {
            return definition.Handoff;
        }

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

        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                new ToolHandoffRoute {
                    TargetPackId = "active_directory",
                    TargetToolName = "ad_object_resolve",
                    Reason = "Use normalized identities from handoff payload for batched AD object resolution.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = "target_arguments/ad_object_resolve/identities",
                            TargetArgument = "identities",
                            IsRequired = true
                        }
                    }
                },
                new ToolHandoffRoute {
                    TargetPackId = "active_directory",
                    TargetToolName = "ad_scope_discovery",
                    Reason = "Use discovered domain hints to bootstrap AD scope before resolution calls.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = "target_arguments/ad_scope_discovery/domain_name",
                            TargetArgument = "domain_name",
                            IsRequired = false
                        },
                        new ToolHandoffBinding {
                            SourceField = "target_arguments/ad_scope_discovery/include_domain_controllers",
                            TargetArgument = "include_domain_controllers",
                            IsRequired = false
                        }
                    }
                }
            }
        };
    }

    private static ToolHandoffContract CreateSystemHostPivotHandoff(
        string primarySourceField,
        string fallbackSourceField,
        string reason) {
        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                CreateSystemHandoffRoute("system_info", reason, primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_time_sync", "Pivot into remote time-sync posture for discovered AD hosts when probe output points to NTP/time skew follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_metrics_summary", "Pivot into remote memory/runtime telemetry for the discovered AD host.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_hardware_summary", "Pivot into remote hardware inventory for the discovered AD host.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_logical_disks_list", "Pivot into remote logical-disk inspection for the discovered AD host.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_windows_update_client_status", "Pivot into remote low-privilege Windows Update/WSUS client status for the discovered AD host.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_windows_update_telemetry", "Pivot into remote Windows Update freshness and reboot telemetry for the discovered AD host.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_backup_posture", "Pivot into remote backup/recovery posture when the discovered AD host needs shadow-copy or restore coverage checks.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_office_posture", "Pivot into remote Office macro/Protected View posture when the discovered AD host needs application hardening follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_browser_posture", "Pivot into remote browser policy posture when the discovered AD host needs endpoint hardening follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_tls_posture", "Pivot into remote TLS/SChannel posture when the discovered AD host needs protocol or cipher-hardening follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_winrm_posture", "Pivot into remote WinRM posture when the discovered AD host needs remote-management hardening follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_powershell_logging_posture", "Pivot into remote PowerShell logging posture when the discovered AD host needs script auditing or logging-policy follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_uac_posture", "Pivot into remote UAC posture when the discovered AD host needs elevation-hardening follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_ldap_policy_posture", "Pivot into remote LDAP signing/channel-binding posture when the discovered AD host needs host policy follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_network_client_posture", "Pivot into remote network-client hardening posture when the discovered AD host needs name-resolution or redirect-policy follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_account_policy_posture", "Pivot into remote account password/lockout posture when the discovered AD host needs effective host account-policy follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_interactive_logon_posture", "Pivot into remote interactive logon posture when the discovered AD host needs console-logon policy follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_device_guard_posture", "Pivot into remote Device Guard posture when the discovered AD host needs virtualization-security follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_defender_asr_posture", "Pivot into remote Defender ASR posture when the discovered AD host needs host attack-surface reduction follow-up.", primarySourceField, fallbackSourceField),
                CreateSystemHandoffRoute("system_certificate_posture", "Pivot into remote certificate-store posture only when the follow-up is about machine certificate stores or trust-store posture on the discovered AD host.", primarySourceField, fallbackSourceField),
                CreateEventLogHandoffRoute("eventlog_channels_list", "Pivot into remote Event Log channel discovery for the discovered AD host before live log triage.", primarySourceField, fallbackSourceField)
            }
        };
    }

    private static ToolHandoffRoute CreateSystemHandoffRoute(
        string targetToolName,
        string reason,
        string primarySourceField,
        string fallbackSourceField) {
        return new ToolHandoffRoute {
            TargetPackId = "system",
            TargetToolName = targetToolName,
            Reason = reason,
            Bindings = new[] {
                new ToolHandoffBinding {
                    SourceField = primarySourceField,
                    TargetArgument = "computer_name",
                    IsRequired = false
                },
                new ToolHandoffBinding {
                    SourceField = fallbackSourceField,
                    TargetArgument = "computer_name",
                    IsRequired = false
                }
            }
        };
    }

    private static ToolHandoffRoute CreateEventLogHandoffRoute(
        string targetToolName,
        string reason,
        string primarySourceField,
        string fallbackSourceField) {
        return new ToolHandoffRoute {
            TargetPackId = "eventlog",
            TargetToolName = targetToolName,
            Reason = reason,
            Bindings = new[] {
                new ToolHandoffBinding {
                    SourceField = primarySourceField,
                    TargetArgument = "machine_name",
                    IsRequired = false
                },
                new ToolHandoffBinding {
                    SourceField = fallbackSourceField,
                    TargetArgument = "machine_name",
                    IsRequired = false
                }
            }
        };
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 1,
            RetryableErrorCodes = new[] { "timeout", "query_failed", "probe_failed", "discovery_failed", "transport_unavailable" },
            RecoveryToolNames = new[] { "ad_environment_discover" }
        };
    }

    private static string ResolveRole(string toolName, string? explicitRole) {
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
}
