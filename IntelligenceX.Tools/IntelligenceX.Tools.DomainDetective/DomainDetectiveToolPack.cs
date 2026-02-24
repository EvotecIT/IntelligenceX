using System;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// DomainDetective tool pack (self-describing + self-registering).
/// </summary>
public sealed class DomainDetectiveToolPack : IToolPack {
    private readonly DomainDetectiveToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="DomainDetectiveToolPack"/>.
    /// </summary>
    public DomainDetectiveToolPack(DomainDetectiveToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "domaindetective",
        Name = "DomainDetective",
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "Open-source domain, DNS, and network-path diagnostics.",
        SourceKind = "open_source"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterDomainDetectivePack(_options);
    }
}
