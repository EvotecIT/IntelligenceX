using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.Common.CrossPack;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Pack-owned EventLog contract shapes used by the EventViewerX pack.
/// </summary>
public static class EventLogContractCatalog {
    private const string PackInfoToolName = "eventlog_pack_info";

    /// <summary>
    /// Stable setup hint keys for remote Event Log access.
    /// </summary>
    public static readonly string[] SetupHintKeys = {
        "machine_name",
        "machine_names",
        "channel",
        "channels",
        "evtx_path",
        "path"
    };

    /// <summary>
    /// Stable setup hint keys for collector-subscription administration.
    /// </summary>
    public static readonly string[] CollectorSubscriptionSetupHintKeys = {
        "machine_name",
        "subscription_name"
    };

    /// <summary>
    /// Stable setup hint keys for classic Event Log provisioning.
    /// </summary>
    public static readonly string[] ClassicLogSetupHintKeys = {
        "machine_name",
        "log_name",
        "source_name"
    };

    /// <summary>
    /// Stable setup hint keys for named-event discovery and query.
    /// </summary>
    public static readonly string[] NamedEventCatalogSetupHintKeys = {
        "named_events",
        "categories",
        "machine_name",
        "machine_names"
    };

    private static readonly string[] RetryableErrorCodes = {
        "timeout",
        "query_failed",
        "probe_failed",
        "transport_unavailable"
    };

    /// <summary>
    /// Builds the standard EventLog channel access setup contract.
    /// </summary>
    public static ToolSetupContract CreateChannelAccessSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "eventlog_connectivity_probe",
            requirementId: "eventlog_channel_access",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: SetupHintKeys);
    }

    /// <summary>
    /// Builds the named-event query setup contract.
    /// </summary>
    public static ToolSetupContract CreateNamedEventQuerySetup() {
        return ToolContractDefaults.CreateSetup(
            setupToolName: "eventlog_named_events_catalog",
            requirements: new[] {
                ToolContractDefaults.CreateRequirement(
                    requirementId: "eventlog_named_event_catalog",
                    requirementKind: ToolSetupRequirementKinds.Capability,
                    hintKeys: NamedEventCatalogSetupHintKeys,
                    isRequired: true),
                ToolContractDefaults.CreateRequirement(
                    requirementId: "eventlog_channel_access",
                    requirementKind: ToolSetupRequirementKinds.Connectivity,
                    hintKeys: SetupHintKeys,
                    isRequired: false)
            },
            setupHintKeys: ToolContractDefaults.MergeDistinctStrings(NamedEventCatalogSetupHintKeys, SetupHintKeys));
    }

    /// <summary>
    /// Builds the governed collector-subscription setup contract.
    /// </summary>
    public static ToolSetupContract CreateCollectorSubscriptionSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "eventlog_connectivity_probe",
            requirementId: "eventlog_collector_subscription_access",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: CollectorSubscriptionSetupHintKeys);
    }

    /// <summary>
    /// Builds the governed classic-log administration setup contract.
    /// </summary>
    public static ToolSetupContract CreateClassicLogSetup() {
        return ToolContractDefaults.CreateRequiredSetup(
            setupToolName: "eventlog_connectivity_probe",
            requirementId: "eventlog_classic_log_access",
            requirementKind: ToolSetupRequirementKinds.Connectivity,
            setupHintKeys: ClassicLogSetupHintKeys);
    }

    /// <summary>
    /// Resolves the default EventLog setup contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolSetupContract? CreateSetup(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (string.Equals(normalizedToolName, PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return string.Equals(normalizedToolName, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)
            ? CreateNamedEventQuerySetup()
            : string.Equals(normalizedToolName, "eventlog_classic_log_ensure", StringComparison.OrdinalIgnoreCase)
                ? CreateClassicLogSetup()
            : string.Equals(normalizedToolName, "eventlog_classic_log_remove", StringComparison.OrdinalIgnoreCase)
                ? CreateClassicLogSetup()
            : string.Equals(normalizedToolName, "eventlog_collector_subscriptions_list", StringComparison.OrdinalIgnoreCase)
                ? CreateCollectorSubscriptionSetup()
            : string.Equals(normalizedToolName, "eventlog_collector_subscription_set", StringComparison.OrdinalIgnoreCase)
                ? CreateCollectorSubscriptionSetup()
            : CreateChannelAccessSetup();
    }

    /// <summary>
    /// Builds the EVTX path follow-up handoff contract.
    /// </summary>
    public static ToolHandoffContract CreateEvtxPathFollowUpHandoff() {
        return ToolContractDefaults.CreateHandoff(EventLogArtifactFollowUpCatalog.CreateEvtxPathRoutes());
    }

    /// <summary>
    /// Builds the standard EventLog query handoff contract into AD and selected System follow-up tools.
    /// </summary>
    public static ToolHandoffContract CreateQueryHandoffContract() {
        return ToolContractDefaults.CreateHandoff(
            ActiveDirectoryEntityHandoffCatalog.CreateEntityAndSelectedSystemRoutes(
                entityHandoffSourceField: "meta/entity_handoff",
                entityHandoffReason: "Promote EventLog entity handoff payload into AD identity normalization before lookups.",
                scopeDiscoverySourceField: "meta/entity_handoff/computer_candidates/0/value",
                scopeDiscoveryReason: "Seed AD scope discovery with host evidence from EventLog query context.",
                systemSourceFields: new[] { "meta/entity_handoff/computer_candidates/0/value" },
                systemRouteSelections: new (string TargetToolName, string? ReasonOverride)[] {
                    ("system_info", "Pivot correlated Event Log host evidence into ComputerX-backed system context collection for the same remote machine."),
                    ("system_metrics_summary", "Pivot correlated Event Log host evidence into remote CPU and memory checks for the same machine.")
                },
                scopeDiscoveryIsRequired: false,
                systemRoutesAreRequired: false));
    }

    /// <summary>
    /// Builds the standard EventLog probe handoff contract into safe live triage.
    /// </summary>
    public static ToolHandoffContract CreateConnectivityProbeHandoffContract() {
        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_top_events",
                reason: "Promote a validated EventLog connectivity probe into a capped live triage follow-up using the same machine and requested log.",
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Investigation,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: true),
                    ToolContractDefaults.CreateBinding("requested_log_name", "log_name", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_live_query",
                reason: "Promote a validated EventLog connectivity probe into a live log query using the same machine and requested log.",
                targetRole: ToolRoutingTaxonomy.RoleOperational,
                followUpKind: ToolHandoffFollowUpKinds.Investigation,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: true),
                    ToolContractDefaults.CreateBinding("requested_log_name", "log_name", isRequired: true)
                })
        });
    }

    /// <summary>
    /// Builds the governed Event Log channel-policy verification handoff contract.
    /// </summary>
    public static ToolHandoffContract CreateChannelPolicyWriteHandoffContract() {
        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_channels_list",
                reason: "Verify the affected Event Log channel remains visible on the same host after the governed channel policy write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("log_name", "name_contains", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_connectivity_probe",
                reason: "Reconfirm Event Log reachability and same-host channel access after the governed channel policy write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("log_name", "log_name", isRequired: true)
                })
        });
    }

    /// <summary>
    /// Builds the governed collector-subscription verification handoff contract.
    /// </summary>
    public static ToolHandoffContract CreateCollectorSubscriptionWriteHandoffContract() {
        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_collector_subscriptions_list",
                reason: "Verify the affected collector subscription remains visible with updated state after the governed write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("subscription_name", "name_contains", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_connectivity_probe",
                reason: "Reconfirm collector-host reachability after the governed collector subscription write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false)
                })
        });
    }

    /// <summary>
    /// Builds the governed classic-log ensure verification handoff contract.
    /// </summary>
    public static ToolHandoffContract CreateClassicLogEnsureHandoffContract() {
        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_channels_list",
                reason: "Verify the affected classic Event Log is visible after the governed ensure write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("log_name", "name_contains", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_providers_list",
                reason: "Verify the requested event source/provider name is visible after the governed ensure write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("source_name", "name_contains", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_connectivity_probe",
                reason: "Reconfirm Event Log reachability after the governed classic-log ensure write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("log_name", "log_name", isRequired: true)
                })
        });
    }

    /// <summary>
    /// Builds the governed classic-log cleanup verification handoff contract.
    /// </summary>
    public static ToolHandoffContract CreateClassicLogRemoveHandoffContract() {
        return ToolContractDefaults.CreateHandoff(new[] {
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_channels_list",
                reason: "Verify the affected classic Event Log no longer appears after the governed cleanup write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.High,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("log_name", "name_contains", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_providers_list",
                reason: "Verify the requested event source/provider name no longer appears after the governed cleanup write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("source_name", "name_contains", isRequired: true)
                }),
            ToolContractDefaults.CreateRoute(
                targetPackId: "eventlog",
                targetToolName: "eventlog_connectivity_probe",
                reason: "Reconfirm Event Log reachability after the governed classic-log cleanup write.",
                targetRole: ToolRoutingTaxonomy.RoleDiagnostic,
                followUpKind: ToolHandoffFollowUpKinds.Verification,
                followUpPriority: ToolHandoffFollowUpPriorities.Normal,
                bindings: new[] {
                    ToolContractDefaults.CreateBinding("machine_name", "machine_name", isRequired: false),
                    ToolContractDefaults.CreateBinding("log_name", "log_name", isRequired: true)
                })
        });
    }

    /// <summary>
    /// Resolves the default EventLog handoff contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolHandoffContract? CreateHandoff(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();

        if (string.Equals(normalizedToolName, PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        if (string.Equals(normalizedToolName, "eventlog_evtx_find", StringComparison.OrdinalIgnoreCase)) {
            return CreateEvtxPathFollowUpHandoff();
        }

        if (string.Equals(normalizedToolName, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedToolName, "eventlog_evtx_security_summary", StringComparison.OrdinalIgnoreCase)) {
            return CreateQueryHandoffContract();
        }

        if (string.Equals(normalizedToolName, "eventlog_connectivity_probe", StringComparison.OrdinalIgnoreCase)) {
            return CreateConnectivityProbeHandoffContract();
        }

        if (string.Equals(normalizedToolName, "eventlog_channel_policy_set", StringComparison.OrdinalIgnoreCase)) {
            return CreateChannelPolicyWriteHandoffContract();
        }

        if (string.Equals(normalizedToolName, "eventlog_classic_log_ensure", StringComparison.OrdinalIgnoreCase)) {
            return CreateClassicLogEnsureHandoffContract();
        }

        if (string.Equals(normalizedToolName, "eventlog_classic_log_remove", StringComparison.OrdinalIgnoreCase)) {
            return CreateClassicLogRemoveHandoffContract();
        }

        if (string.Equals(normalizedToolName, "eventlog_collector_subscription_set", StringComparison.OrdinalIgnoreCase)) {
            return CreateCollectorSubscriptionWriteHandoffContract();
        }

        return null;
    }

    /// <summary>
    /// Builds the standard EventLog retry and recovery contract for a tool name.
    /// </summary>
    public static ToolRecoveryContract? CreateRecovery(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (string.Equals(normalizedToolName, PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var supportsRetry = normalizedToolName.IndexOf("_query", StringComparison.OrdinalIgnoreCase) >= 0
                            || normalizedToolName.IndexOf("_find", StringComparison.OrdinalIgnoreCase) >= 0
                            || normalizedToolName.IndexOf("_top_events", StringComparison.OrdinalIgnoreCase) >= 0;
        if (string.Equals(normalizedToolName, "eventlog_channel_policy_set", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateNoRetryRecovery(
                recoveryToolNames: new[] { "eventlog_connectivity_probe", "eventlog_channels_list" });
        }

        if (string.Equals(normalizedToolName, "eventlog_classic_log_ensure", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateNoRetryRecovery(
                recoveryToolNames: new[] { "eventlog_channels_list", "eventlog_providers_list", "eventlog_connectivity_probe" });
        }

        if (string.Equals(normalizedToolName, "eventlog_classic_log_remove", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateNoRetryRecovery(
                recoveryToolNames: new[] { "eventlog_classic_log_ensure", "eventlog_channels_list", "eventlog_providers_list", "eventlog_connectivity_probe" });
        }

        if (string.Equals(normalizedToolName, "eventlog_collector_subscription_set", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateNoRetryRecovery(
                recoveryToolNames: new[] { "eventlog_collector_subscriptions_list", "eventlog_connectivity_probe" });
        }

        var recoveryToolNames = string.Equals(normalizedToolName, "eventlog_evtx_find", StringComparison.OrdinalIgnoreCase)
            ? new[] { "eventlog_evtx_find" }
            : new[] { "eventlog_connectivity_probe", "eventlog_channels_list" };

        return ToolContractDefaults.CreateRecovery(
            supportsTransientRetry: supportsRetry,
            maxRetryAttempts: supportsRetry ? 1 : 0,
            retryableErrorCodes: supportsRetry ? RetryableErrorCodes : Array.Empty<string>(),
            recoveryToolNames: recoveryToolNames);
    }
}
