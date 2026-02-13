using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Event log tool pack (self-describing + self-registering).
/// </summary>
public sealed class EventLogToolPack : IToolPack {
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
        Name = "Event Log",
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "Windows Event Log and EVTX analysis (restricted to AllowedRoots for EVTX file access)."
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterEventLogPack(_options);
    }
}
