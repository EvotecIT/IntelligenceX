using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// IX.TestimoX tool pack (self-describing + self-registering).
/// </summary>
public sealed class TestimoXToolPack : IToolPack {
    private readonly TestimoXToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="TestimoXToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public TestimoXToolPack(TestimoXToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "testimox",
        Name = "IX.TestimoX",
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "TestimoX rule discovery and targeted rule execution (read-oriented diagnostics)."
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterTestimoXPack(_options);
    }
}
