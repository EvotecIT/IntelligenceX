using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// System tool pack (self-describing + self-registering).
/// </summary>
public sealed class SystemToolPack : IToolPack {
    private readonly SystemToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="SystemToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public SystemToolPack(SystemToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "system",
        Name = "System",
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "Local system read-only tools.",
        SourceKind = "closed_source"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterSystemPack(_options);
    }
}
