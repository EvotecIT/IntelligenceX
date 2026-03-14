using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared catalog for cross-pack remote host follow-up routes spanning System and EventLog packs.
/// </summary>
public static class RemoteHostFollowUpCatalog {
    /// <summary>
    /// Builds the standard System + EventLog remote-host follow-up routes from a single source field.
    /// </summary>
    public static ToolHandoffRoute[] CreateSystemAndEventLogTargetRoutes(
        string sourceField,
        string systemReason,
        string eventLogReason,
        bool isRequired = false) {
        return CreateSystemAndEventLogTargetRoutes(
            sourceFields: new[] { sourceField },
            systemReason: systemReason,
            eventLogReason: eventLogReason,
            isRequired: isRequired);
    }

    /// <summary>
    /// Builds the standard System + EventLog remote-host follow-up routes from caller-provided source fields.
    /// </summary>
    public static ToolHandoffRoute[] CreateSystemAndEventLogTargetRoutes(
        IReadOnlyList<string> sourceFields,
        string systemReason,
        string eventLogReason,
        bool isRequired = false) {
        var systemRoutes = SystemRemoteHostFollowUpCatalog.CreateComputerTargetRoutes(
            sourceFields: sourceFields,
            primaryReasonOverride: systemReason,
            isRequired: isRequired);
        var eventLogRoutes = EventLogRemoteHostFollowUpCatalog.CreateMachineTargetRoutes(
            sourceFields: sourceFields,
            primaryReasonOverride: eventLogReason,
            isRequired: isRequired);
        return CrossPackRouteComposer.Combine(systemRoutes, eventLogRoutes);
    }

    /// <summary>
    /// Builds the standard System + EventLog channel-discovery follow-up routes from caller-provided source fields.
    /// </summary>
    public static ToolHandoffRoute[] CreateSystemAndEventLogChannelDiscoveryRoutes(
        IReadOnlyList<string> sourceFields,
        string systemReason,
        string eventLogReason,
        bool isRequired = false) {
        var systemRoutes = SystemRemoteHostFollowUpCatalog.CreateComputerTargetRoutes(
            sourceFields: sourceFields,
            primaryReasonOverride: systemReason,
            isRequired: isRequired);
        var eventLogRoutes = EventLogRemoteHostFollowUpCatalog.CreateChannelDiscoveryRoutes(
            sourceFields: sourceFields,
            primaryReasonOverride: eventLogReason,
            isRequired: isRequired);
        return CrossPackRouteComposer.Combine(systemRoutes, eventLogRoutes);
    }
}
