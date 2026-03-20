using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Event log tool pack (self-describing + self-registering).
/// </summary>
public sealed class EventLogToolPack : IToolPack, IToolPackCatalogProvider, IToolPackGuidanceProvider {
    private readonly EventLogToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="EventLogToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public EventLogToolPack(EventLogToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "eventlog",
        Name = "Event Log (EventViewerX)",
        Aliases = new[] { "eventviewerx", "event_log" },
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "Windows Event Log and EVTX analysis plus governed channel policy, classic log administration, and collector subscription writes (restricted to AllowedRoots for EVTX file access).",
        SourceKind = "builtin",
        EngineId = "eventviewerx",
        Category = "eventlog",
        CapabilityTags = new[] {
            "auth",
            "channel_policy",
            "classic_log",
            "classic_log_cleanup",
            "collector_subscription",
            "event_logs",
            "evtx",
            "forensics",
            ToolPackCapabilityTags.GovernedWrite,
            "kerberos",
            ToolPackCapabilityTags.LocalAnalysis,
            ToolPackCapabilityTags.LocalExecution,
            "log_provisioning",
            ToolPackCapabilityTags.RemoteAnalysis,
            ToolPackCapabilityTags.RemoteExecution,
            "retention",
            "subscription",
            "wec",
            ToolPackCapabilityTags.WriteCapable,
            "security_events"
        },
        SearchTokens = new[] {
            "auth",
            "channel_policy",
            "classic_log",
            "collector_subscription",
            "custom_log",
            "custom_log_cleanup",
            "event",
            "eventlog",
            "eventviewerx",
            "evtx",
            "forensics",
            "governed_write",
            "kerberos",
            "log_provisioning",
            "log_cleanup",
            "remote_analysis",
            "retention",
            "security_events",
            "ensure_log_source",
            "remove_log_source",
            "set_collector_subscription",
            "set_channel_policy",
            "subscription",
            "wec",
            "windows_event_channel",
            "windows_events",
            "windows_logs"
        },
        CapabilityParity = EventLogToolPackParity.Slices
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterEventLogPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryEventLogExtensions.GetRegisteredToolCatalog(_options);
    }

    /// <inheritdoc />
    public ToolPackInfoModel GetPackGuidance() {
        return EventLogPackInfoTool.BuildGuidance(_options);
    }
}
