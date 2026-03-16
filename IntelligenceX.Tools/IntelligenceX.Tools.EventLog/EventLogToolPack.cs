using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Event log tool pack (self-describing + self-registering).
/// </summary>
public sealed class EventLogToolPack : IToolPack, IToolPackCatalogProvider {
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
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "Windows Event Log and EVTX analysis (restricted to AllowedRoots for EVTX file access).",
        SourceKind = "builtin",
        EngineId = "eventviewerx",
        Category = "eventlog",
        CapabilityTags = new[] {
            "auth",
            "event_logs",
            "evtx",
            "forensics",
            "kerberos",
            "local_analysis",
            "remote_analysis",
            "security_events"
        },
        SearchTokens = new[] {
            "auth",
            "event",
            "eventlog",
            "eventviewerx",
            "evtx",
            "forensics",
            "kerberos",
            "remote_analysis",
            "security_events",
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
}
