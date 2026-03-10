using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// TestimoX analytics/history artifact pack (self-describing + self-registering).
/// </summary>
public sealed class TestimoXAnalyticsToolPack : IToolPack {
    private readonly TestimoXToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="TestimoXAnalyticsToolPack"/>.
    /// </summary>
    public TestimoXAnalyticsToolPack(TestimoXToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "testimox_analytics",
        Name = "TestimoX Analytics",
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "Persisted TestimoX analytics, report, and history artifact inspection.",
        SourceKind = "closed_source"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterTestimoXAnalyticsPack(_options);
    }
}
