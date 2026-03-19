using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Pack-owned Active Directory contract shapes used by the ADPlayground pack.
/// </summary>
public static class ActiveDirectoryContractCatalog {
    private const string PackInfoToolName = "ad_pack_info";

    /// <summary>
    /// Stable setup hint keys for Active Directory scope and connectivity discovery.
    /// </summary>
    public static readonly string[] SetupHintKeys = {
        "domain_controller",
        "search_base_dn",
        "domain_name",
        "forest_name"
    };

    private static readonly string[] LdapConnectivityHintKeys = {
        "domain_controller",
        "domain_name",
        "forest_name"
    };

    private static readonly string[] RetryableErrorCodes = {
        "timeout",
        "query_failed",
        "probe_failed",
        "discovery_failed",
        "transport_unavailable"
    };

    /// <summary>
    /// Builds the standard Active Directory environment setup contract.
    /// </summary>
    public static ToolSetupContract CreateDirectoryContextSetup() {
        return ToolContractDefaults.CreateSetup(
            setupToolName: "ad_environment_discover",
            requirements: new[] {
                ToolContractDefaults.CreateRequirement(
                    requirementId: "ad_directory_context",
                    requirementKind: ToolSetupRequirementKinds.Configuration,
                    hintKeys: SetupHintKeys,
                    isRequired: true),
                ToolContractDefaults.CreateRequirement(
                    requirementId: "ad_ldap_connectivity",
                    requirementKind: ToolSetupRequirementKinds.Connectivity,
                    hintKeys: LdapConnectivityHintKeys,
                    isRequired: true)
            },
            setupHintKeys: SetupHintKeys);
    }

    /// <summary>
    /// Resolves the default Active Directory setup contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolSetupContract? CreateSetup(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateDirectoryContextSetup();
    }

    /// <summary>
    /// Builds the standard Active Directory retry and recovery contract.
    /// </summary>
    public static ToolRecoveryContract CreateStandardRecovery() {
        return ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: true,
            maxRetryAttempts: 1,
            retryableErrorCodes: RetryableErrorCodes,
            recoveryToolNames: new[] { "ad_environment_discover" });
    }

    /// <summary>
    /// Resolves the default Active Directory retry and recovery contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        return string.Equals((toolName ?? string.Empty).Trim(), PackInfoToolName, StringComparison.OrdinalIgnoreCase)
            ? null
            : CreateStandardRecovery();
    }

    /// <summary>
    /// Resolves the default Active Directory handoff contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolHandoffContract? CreateHandoff(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();

        if (string.Equals(normalizedToolName, "ad_connectivity_probe", StringComparison.OrdinalIgnoreCase)) {
            return CreateConnectivityProbeHandoff();
        }

        if (string.Equals(normalizedToolName, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "context/domain_controller",
                fallbackSourceField: "domain_controllers/0/value",
                reason: "Promote discovered AD domain-controller context into remote ComputerX host diagnostics.");
        }

        if (string.Equals(normalizedToolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "ad_domain_controllers", StringComparison.OrdinalIgnoreCase)) {
            return CreateSystemHostPivotHandoff(
                primarySourceField: "domain_controllers/0/value",
                fallbackSourceField: "requested_scope/domain_controller",
                reason: "Promote discovered AD domain-controller inventory into remote ComputerX host diagnostics.");
        }

        if (string.Equals(normalizedToolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
            return CreateMonitoringProbeRunHandoff();
        }

        if (string.Equals(normalizedToolName, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase)) {
            return CreatePreparedIdentityHandoff();
        }

        return null;
    }

    /// <summary>
    /// Builds the standard Active Directory probe follow-up contract into richer environment discovery.
    /// </summary>
    public static ToolHandoffContract CreateConnectivityProbeHandoff() {
        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_environment_discover",
                reason: "Promote validated Active Directory probe context into fuller environment discovery using the effective domain controller and search base.",
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("effective_domain_controller", "domain_controller", isRequired: true),
                    ToolContractDefaults.CreateBinding("domain_controller", "domain_controller", isRequired: false),
                    ToolContractDefaults.CreateBinding("effective_search_base_dn", "search_base_dn", isRequired: true),
                    ToolContractDefaults.CreateBinding("search_base_dn", "search_base_dn", isRequired: false)
                })
        });
    }

    /// <summary>
    /// Builds the standard Active Directory handoff-preparation follow-up contract.
    /// </summary>
    public static ToolHandoffContract CreatePreparedIdentityHandoff() {
        return ToolContractDefaults.CreateHandoff(ActiveDirectoryFollowUpCatalog.CreatePreparedIdentityRoutes());
    }

    /// <summary>
    /// Builds the Active Directory monitoring probe follow-up contract with probe-kind-aware continuations.
    /// </summary>
    public static ToolHandoffContract CreateMonitoringProbeRunHandoff() {
        var sourceFields = new[] {
            "normalized_request/domain_controller",
            "normalized_request/targets/0",
            "domain_controller",
            "targets/0"
        };

        return ToolContractDefaults.CreateHandoff(
            CombineRoutes(
                SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
                    sourceFields: sourceFields,
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_info", "Promote AD monitoring probe targets into remote ComputerX host verification before deeper runtime diagnostics.")
                    }),
                EventLogRemoteHostFollowUpCatalog.CreateChannelDiscoveryRoutes(
                    sourceFields: sourceFields,
                    primaryReasonOverride: "Pivot AD monitoring probe targets into remote Event Log channel discovery before deeper live triage."),
                CreateMonitoringLdapFollowUpRoutes(sourceFields),
                CreateMonitoringProfileSystemRoutes(
                    sourceFields,
                    probeKind: "ldap",
                    profileId: "host_policy_focus",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_metrics_summary", "Inspect same-host runtime pressure when LDAP follow-through is about host policy or broader DC state.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "dns",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_network_client_posture", "Inspect DNS client and name-resolution posture on the same monitoring target."),
                        ("system_network_adapters", "Inspect same-host network adapters and addressing after AD DNS probe findings.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "dns_service",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_network_client_posture", "Inspect DNS client and name-resolution posture on the same monitoring target."),
                        ("system_network_adapters", "Inspect same-host network adapters and addressing after AD DNS service findings.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "kerberos",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_time_sync", "Inspect same-host time-sync posture after Kerberos timing or KDC latency findings."),
                        ("system_metrics_summary", "Inspect same-host runtime pressure after Kerberos findings.")
                    }),
                CreateMonitoringProfileSystemRoutes(
                    sourceFields,
                    probeKind: "kerberos",
                    profileId: "transport_split",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_ports_list", "Inspect same-host listener and port state when Kerberos transport split behavior needs follow-through.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "ntp",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_time_sync", "Inspect same-host time-sync posture after NTP skew or packet-loss findings."),
                        ("system_metrics_summary", "Inspect same-host runtime pressure when time service behavior looks unstable.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "replication",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_metrics_summary", "Inspect same-host runtime pressure after replication or topology failures."),
                        ("system_logical_disks_list", "Inspect same-host disks when SYSVOL or storage-backed replication may be involved."),
                        ("system_ports_list", "Inspect same-host listening ports when replication endpoints appear unreachable.")
                    }),
                CreateMonitoringProfileSystemRoutes(
                    sourceFields,
                    probeKind: "replication",
                    profileId: "resource_pressure",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_hardware_summary", "Inspect same-host hardware inventory when replication symptoms suggest broader resource pressure.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "port",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_ports_list", "Inspect same-host listeners and port state for the failing endpoint."),
                        ("system_service_list", "Inspect same-host services behind the failing listener.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "adws",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_ports_list", "Inspect same-host listeners and port state for the failing ADWS endpoint."),
                        ("system_service_list", "Inspect same-host services behind the failing ADWS path.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "https",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_tls_posture", "Inspect same-host TLS/SChannel posture after HTTPS probe findings."),
                        ("system_certificate_posture", "Inspect same-host certificate-store posture when HTTPS trust-store follow-through is needed.")
                    }),
                CreateMonitoringProfileSystemRoutes(
                    sourceFields,
                    probeKind: "https",
                    profileId: "latency_and_runtime_focus",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_metrics_summary", "Inspect same-host runtime pressure when HTTPS is reachable but slow or intermittently degraded.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "ping",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_metrics_summary", "Inspect same-host runtime pressure after latency, jitter, or loss findings."),
                        ("system_network_adapters", "Inspect same-host network adapters after ICMP reachability or latency issues.")
                    }),
                CreateMonitoringKindSystemRoutes(
                    sourceFields,
                    probeKind: "windows_update",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_windows_update_client_status", "Inspect same-host WSUS and Windows Update client state."),
                        ("system_windows_update_telemetry", "Inspect same-host update freshness, reboot pressure, and telemetry."),
                        ("system_patch_compliance", "Correlate same-host installed updates with missing security coverage.")
                    }),
                CreateMonitoringProfileSystemRoutes(
                    sourceFields,
                    probeKind: "windows_update",
                    profileId: "patch_inventory_focus",
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_updates_installed", "Inspect same-host installed update inventory when Windows Update follow-through is patch-inventory focused.")
                    }),
                CreateMonitoringDirectoryKindRoutes(
                    sourceFields,
                    directoryProbeKinds: new[] { "rpc_endpoint" },
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_ports_list", "Inspect same-host listener inventory after directory RPC endpoint failures."),
                        ("system_service_list", "Inspect same-host services after directory RPC endpoint failures.")
                    }),
                CreateMonitoringDirectoryKindRoutes(
                    sourceFields,
                    directoryProbeKinds: new[] { "sysvol_gpt", "netlogon_share", "share_permissions" },
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_logical_disks_list", "Inspect same-host disks and share backing paths after directory share or SYSVOL failures."),
                        ("system_service_list", "Inspect same-host services behind SYSVOL or share availability.")
                    }),
                CreateMonitoringDirectoryKindRoutes(
                    sourceFields,
                    directoryProbeKinds: new[] { "dns_registration", "dns_soa", "srv_coverage" },
                    routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                        ("system_network_client_posture", "Inspect same-host DNS client posture after directory DNS findings."),
                        ("system_network_adapters", "Inspect same-host network adapters after directory DNS findings.")
                    })));
    }

    /// <summary>
    /// Builds the standard Active Directory host pivot contract into System and EventLog packs.
    /// </summary>
    public static ToolHandoffContract CreateSystemHostPivotHandoff(
        string primarySourceField,
        string fallbackSourceField,
        string reason) {
        return ToolContractDefaults.CreateHandoff(
            RemoteHostFollowUpCatalog.CreateSystemAndEventLogChannelDiscoveryRoutes(
                sourceFields: new[] { primarySourceField, fallbackSourceField },
                systemReason: reason,
                eventLogReason: "Pivot into remote Event Log channel discovery for the discovered AD host before live log triage.",
                isRequired: false));
    }

    private static ToolHandoffRoute[] CreateMonitoringLdapFollowUpRoutes(IReadOnlyList<string> sourceFields) {
        var ldapRoutes = new List<ToolHandoffRoute> {
            ToolContractDefaults.CreateSharedTargetRoute(
                targetPackId: "active_directory",
                targetToolName: "ad_ldap_diagnostics",
                reason: "Inspect LDAP/LDAPS endpoint status and certificate details on the same AD monitoring target.",
                targetArgument: "domain_controller",
                sourceFields: sourceFields,
                isRequired: false,
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High)
        };
        ApplyMonitoringConditions(ldapRoutes, "probe_kind", "ldap");

        var directoryLdapRoutes = new List<ToolHandoffRoute>();
        foreach (var directoryProbeKind in new[] { "ldap_search", "gc_readiness", "root_dse", "client_path" }) {
            var routesForKind = new List<ToolHandoffRoute> {
                ToolContractDefaults.CreateSharedTargetRoute(
                    targetPackId: "active_directory",
                    targetToolName: "ad_ldap_diagnostics",
                    reason: "Inspect LDAP/LDAPS endpoint status and certificate details after directory probe findings that depend on LDAP readiness.",
                    targetArgument: "domain_controller",
                    sourceFields: sourceFields,
                    isRequired: false,
                    targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                    followUpKind: ToolHandoffFollowUpKinds.Verification,
                    followUpPriority: ToolHandoffFollowUpPriorities.High)
            };
            ApplyMonitoringConditions(
                routesForKind,
                ("probe_kind", "directory"),
                ("directory_probe_kind", directoryProbeKind));
            directoryLdapRoutes.AddRange(routesForKind);
        }

        var ldapPolicyRoutes = CreateMonitoringKindSystemRoutes(
            sourceFields,
            probeKind: "ldap",
            routeSelections: new (string TargetToolName, string? ReasonOverride)[] {
                ("system_ldap_policy_posture", "Inspect same-host LDAP signing and channel-binding posture on the same monitoring target.")
            });

        return CombineRoutes(ldapRoutes, directoryLdapRoutes, ldapPolicyRoutes);
    }

    private static ToolHandoffRoute[] CreateMonitoringKindSystemRoutes(
        IReadOnlyList<string> sourceFields,
        string probeKind,
        IReadOnlyList<(string TargetToolName, string? ReasonOverride)> routeSelections) {
        var routes = SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
            sourceFields: sourceFields,
            routeSelections: routeSelections,
            isRequired: false);
        ApplyMonitoringConditions(routes, "probe_kind", probeKind);
        return routes;
    }

    private static ToolHandoffRoute[] CreateMonitoringProfileSystemRoutes(
        IReadOnlyList<string> sourceFields,
        string probeKind,
        string profileId,
        IReadOnlyList<(string TargetToolName, string? ReasonOverride)> routeSelections) {
        var routes = SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
            sourceFields: sourceFields,
            routeSelections: routeSelections,
            isRequired: false);
        ApplyMonitoringConditions(
            routes,
            ("probe_kind", probeKind),
            ("active_follow_up_profile_ids", profileId));
        return routes;
    }

    private static ToolHandoffRoute[] CreateMonitoringDirectoryKindRoutes(
        IReadOnlyList<string> sourceFields,
        IReadOnlyList<string> directoryProbeKinds,
        IReadOnlyList<(string TargetToolName, string? ReasonOverride)> routeSelections) {
        if (directoryProbeKinds is not { Count: > 0 }) {
            var routes = SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
                sourceFields: sourceFields,
                routeSelections: routeSelections,
                isRequired: false);
            ApplyMonitoringConditions(routes, "probe_kind", "directory");
            return routes;
        }

        var conditionedRoutes = new List<ToolHandoffRoute>();
        for (var i = 0; i < directoryProbeKinds.Count; i++) {
            var routes = SystemRemoteHostFollowUpCatalog.CreateSelectedComputerTargetRoutes(
                sourceFields: sourceFields,
                routeSelections: routeSelections,
                isRequired: false);
            ApplyMonitoringConditions(
                routes,
                ("probe_kind", "directory"),
                ("directory_probe_kind", directoryProbeKinds[i]));
            conditionedRoutes.AddRange(routes);
        }

        return conditionedRoutes.ToArray();
    }

    private static void ApplyMonitoringConditions(IReadOnlyList<ToolHandoffRoute> routes, string sourceField, string expectedValue) {
        ApplyMonitoringConditions(routes, new[] { ToolContractDefaults.CreateCondition(sourceField, expectedValue) });
    }

    private static void ApplyMonitoringConditions(
        IReadOnlyList<ToolHandoffRoute> routes,
        params (string SourceField, string ExpectedValue)[] conditions) {
        if (conditions is null || conditions.Length == 0) {
            return;
        }

        ApplyMonitoringConditions(
            routes,
            conditions.Select(static pair => ToolContractDefaults.CreateCondition(pair.SourceField, pair.ExpectedValue)).ToArray());
    }

    private static void ApplyMonitoringConditions(IReadOnlyList<ToolHandoffRoute> routes, IReadOnlyList<ToolHandoffCondition> conditions) {
        if (routes is null || routes.Count == 0 || conditions is not { Count: > 0 }) {
            return;
        }

        for (var i = 0; i < routes.Count; i++) {
            routes[i].Conditions = conditions.ToArray();
        }
    }

    private static ToolHandoffRoute[] CombineRoutes(params IReadOnlyList<ToolHandoffRoute>[] routeGroups) {
        var combined = new List<ToolHandoffRoute>();
        if (routeGroups is null || routeGroups.Length == 0) {
            return Array.Empty<ToolHandoffRoute>();
        }

        for (var i = 0; i < routeGroups.Length; i++) {
            var group = routeGroups[i];
            if (group is null || group.Count == 0) {
                continue;
            }

            for (var j = 0; j < group.Count; j++) {
                if (group[j] is not null) {
                    combined.Add(group[j]);
                }
            }
        }

        return combined.Count == 0 ? Array.Empty<ToolHandoffRoute>() : combined.ToArray();
    }
}
