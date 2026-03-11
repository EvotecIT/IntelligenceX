using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// OfficeIMO tool pack (self-describing + self-registering).
/// </summary>
public sealed class OfficeImoToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly OfficeImoToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="OfficeImoToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public OfficeImoToolPack(OfficeImoToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "officeimo",
        Name = "Office Documents (OfficeIMO)",
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "Read-only Office document ingestion (Word/Excel/PowerPoint/Markdown/PDF) backed by OfficeIMO.Reader.",
        SourceKind = "open_source"
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterOfficeImoPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryOfficeImoExtensions.GetRegisteredToolCatalog(_options);
    }
}
