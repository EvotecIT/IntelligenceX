using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared catalog of EventLog follow-up routes that accept a remote machine target.
/// </summary>
public static class EventLogRemoteHostFollowUpCatalog {
    /// <summary>
    /// Stable route descriptors for EventLog pack tools that accept a <c>machine_name</c> pivot.
    /// </summary>
    public static readonly (string TargetToolName, string Reason)[] MachineTargetRouteDescriptors = {
        ("eventlog_live_stats", "Promote discovered host context into remote EventViewerX live statistics and availability follow-up.")
    };

    /// <summary>
    /// Stable route descriptors for EventLog pack tools that accept a <c>machine_name</c> pivot
    /// for channel-discovery style follow-up.
    /// </summary>
    public static readonly (string TargetToolName, string Reason)[] ChannelDiscoveryRouteDescriptors = {
        ("eventlog_channels_list", "Pivot into remote Event Log channel discovery for the discovered host before live log triage.")
    };

    /// <summary>
    /// Builds the standard EventLog pack remote-host follow-up routes with caller-provided source fields.
    /// </summary>
    public static ToolHandoffRoute[] CreateMachineTargetRoutes(
        IReadOnlyList<string> sourceFields,
        string? primaryReasonOverride = null,
        bool isRequired = false) {
        var descriptors = new (string TargetToolName, string Reason)[MachineTargetRouteDescriptors.Length];
        descriptors[0] = (
            MachineTargetRouteDescriptors[0].TargetToolName,
            string.IsNullOrWhiteSpace(primaryReasonOverride)
                ? MachineTargetRouteDescriptors[0].Reason
                : primaryReasonOverride!);

        return ToolContractDefaults.CreateSharedTargetRoutes(
            targetPackId: "eventlog",
            targetArgument: "machine_name",
            sourceFields: sourceFields,
            routeDescriptors: descriptors,
            isRequired: isRequired);
    }

    /// <summary>
    /// Builds the standard EventLog channel-discovery follow-up routes with caller-provided source fields.
    /// </summary>
    public static ToolHandoffRoute[] CreateChannelDiscoveryRoutes(
        IReadOnlyList<string> sourceFields,
        string? primaryReasonOverride = null,
        bool isRequired = false) {
        var descriptors = new (string TargetToolName, string Reason)[ChannelDiscoveryRouteDescriptors.Length];
        descriptors[0] = (
            ChannelDiscoveryRouteDescriptors[0].TargetToolName,
            string.IsNullOrWhiteSpace(primaryReasonOverride)
                ? ChannelDiscoveryRouteDescriptors[0].Reason
                : primaryReasonOverride!);

        return ToolContractDefaults.CreateSharedTargetRoutes(
            targetPackId: "eventlog",
            targetArgument: "machine_name",
            sourceFields: sourceFields,
            routeDescriptors: descriptors,
            isRequired: isRequired);
    }
}
