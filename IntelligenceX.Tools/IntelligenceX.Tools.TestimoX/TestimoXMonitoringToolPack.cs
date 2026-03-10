using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// TestimoX monitoring/history artifact pack (self-describing + self-registering).
/// </summary>
public sealed class TestimoXMonitoringToolPack : IToolPack {
    private readonly TestimoXToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="TestimoXMonitoringToolPack"/>.
    /// </summary>
    public TestimoXMonitoringToolPack(TestimoXToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "testimox_monitoring",
        Name = "TestimoX Monitoring",
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "Persisted TestimoX monitoring, report, and history artifact inspection.",
        SourceKind = "closed_source"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterTestimoXMonitoringPack(_options);
    }
}

