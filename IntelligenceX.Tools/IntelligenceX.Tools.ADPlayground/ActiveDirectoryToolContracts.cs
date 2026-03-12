using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolContracts {
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

    private static readonly string[] DiagnosticRoleKeywords = {
        "domain",
        "forest",
        "gpo",
        "dns",
        "site",
        "subnet",
        "group",
        "account",
        "policy",
        "trust",
        "replication",
        "spn",
        "kerberos",
        "ldap",
        "pki",
        "laps",
        "firewall",
        "delegation",
        "permission",
        "monitoring",
        "schema",
        "certificate",
        "credentials",
        "ntlm",
        "logon",
        "uac",
        "wsus",
        "wmi",
        "winrm",
        "service",
        "netlogon",
        "sysvol",
        "shadow",
        "kdc",
        "proxy",
        "whoami",
        "ou",
        "admin",
        "users"
    };

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

    private static string ResolveRole(string toolName, string? existingRole) {
        var inferredRole = TryResolveDeclaredRole(toolName);
        if (inferredRole.Length == 0) {
            return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
                explicitRole: existingRole,
                toolName: toolName,
                declaredRolesByToolName: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                packDisplayName: "AD Playground");
        }

        return ToolRoutingRoleResolver.ResolveExplicitOrFallback(
            explicitRole: existingRole,
            fallbackRole: inferredRole,
            packDisplayName: "AD Playground");
    }

    private static string TryResolveDeclaredRole(string toolName) {
        if (string.Equals(toolName, "ad_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (string.Equals(toolName, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleEnvironmentDiscover;
        }

        if (string.Equals(toolName, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_probe_catalog", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_service_heartbeat_get", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_diagnostics_get", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_metrics_get", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_dashboard_state_get", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (string.Equals(toolName, "ad_dns_server_config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_zone_config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_zone_security", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_delegation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_scavenging", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleResolver;
        }

        if (string.Equals(toolName, "ad_search_facets", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (string.Equals(toolName, "ad_search", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_object_resolve", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_ldap_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_ldap_query_paged", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_spn_search", StringComparison.OrdinalIgnoreCase)
            || toolName.IndexOf("_search", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_resolve", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_query", StringComparison.OrdinalIgnoreCase) >= 0) {
            return ToolRoutingTaxonomy.RoleResolver;
        }

        if (string.Equals(toolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_object_get", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleOperational;
        }

        if (string.Equals(toolName, "ad_domain_info", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_trust", StringComparison.OrdinalIgnoreCase)
            || toolName.IndexOf("_list", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_summary", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_statistics", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_stats", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_policy", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_posture", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_security", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_health", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_facts", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_roles", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_coverage", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_integrity", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_inventory", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_status", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_version", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_templates", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_keys", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_usage", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_accounts", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_audit", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_defaults", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_configuration", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_filters", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_functional", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_changes", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_duplicates", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_site", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_read", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_report", StringComparison.OrdinalIgnoreCase) >= 0
            || ContainsAnyKeyword(toolName, DiagnosticRoleKeywords)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (toolName.StartsWith("ad_", StringComparison.OrdinalIgnoreCase)
            && toolName.IndexOf("unclassified", StringComparison.OrdinalIgnoreCase) < 0) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        return string.Empty;
    }

    private static bool ContainsAnyKeyword(string toolName, IReadOnlyList<string> keywords) {
        for (var i = 0; i < keywords.Count; i++) {
            if (toolName.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }
}
