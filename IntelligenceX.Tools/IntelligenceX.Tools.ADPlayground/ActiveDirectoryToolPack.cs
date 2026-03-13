using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Active Directory tool pack (self-describing + self-registering).
/// </summary>
public sealed class ActiveDirectoryToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly ActiveDirectoryToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="ActiveDirectoryToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public ActiveDirectoryToolPack(ActiveDirectoryToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "active_directory",
        Name = "ADPlayground",
        Aliases = new[] { "ad", "adplayground" },
        Tier = ToolCapabilityTier.SensitiveRead,
        IsDangerous = false,
        Description = "ADPlayground-backed Active Directory analysis tools (read-oriented).",
        SourceKind = "closed_source",
        EngineId = "adplayground",
        CapabilityTags = new[] { "directory", "domain_scope", "gpo", "identity", "remote_analysis" }
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterActiveDirectoryPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryActiveDirectoryExtensions.GetRegisteredToolCatalog(_options);
    }
}
