using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// DnsClientX tool pack (self-describing + self-registering).
/// </summary>
public sealed class DnsClientXToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly DnsClientXToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="DnsClientXToolPack"/>.
    /// </summary>
    public DnsClientXToolPack(DnsClientXToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "dnsclientx",
        Name = "DnsClientX",
        Aliases = new[] { "dns_client_x" },
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "Open-source DNS query and connectivity diagnostics.",
        SourceKind = "open_source",
        EngineId = "dnsclientx",
        Category = "dns",
        CapabilityTags = new[] { "dns", "network", ToolPackCapabilityTags.RemoteAnalysis }
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterDnsClientXPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryDnsClientXExtensions.GetRegisteredToolCatalog(_options);
    }
}
