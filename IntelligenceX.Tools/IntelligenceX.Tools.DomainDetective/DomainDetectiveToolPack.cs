using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// DomainDetective tool pack (self-describing + self-registering).
/// </summary>
public sealed class DomainDetectiveToolPack : IToolPack, IToolPackCatalogProvider {
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
        SourceKind = "open_source",
        EngineId = "domaindetective",
        CapabilityTags = new[] { "dns", "domain_scope", "network_path", "remote_analysis" }
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterDomainDetectivePack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryDomainDetectiveExtensions.GetRegisteredToolCatalog(_options);
    }
}
