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
    /// Resolves the default EventLog setup contract for a tool name when the tool does not declare one explicitly.
    /// </summary>
    public static ToolSetupContract? CreateSetup(string toolName) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (string.Equals(normalizedToolName, PackInfoToolName, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return string.Equals(normalizedToolName, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)
            ? CreateNamedEventQuerySetup()
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
