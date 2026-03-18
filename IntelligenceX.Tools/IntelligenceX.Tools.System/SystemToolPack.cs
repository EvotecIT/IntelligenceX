using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// System tool pack (self-describing + self-registering).
/// </summary>
public sealed class SystemToolPack : IToolPack, IToolPackCatalogProvider, IToolPackGuidanceProvider {
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
        Name = "ComputerX",
        Aliases = new[] { "computerx" },
        Tier = ToolCapabilityTier.ReadOnly,
        IsDangerous = false,
        Description = "ComputerX host inventory and diagnostics (read-only).",
        SourceKind = "closed_source",
        EngineId = "computerx",
        Category = "system",
        CapabilityTags = new[] {
            "cpu",
            "disk_space",
            "host_inventory",
            ToolPackCapabilityTags.LocalAnalysis,
            "memory",
            "patching",
            "performance",
            ToolPackCapabilityTags.RemoteAnalysis,
            "storage"
        },
        SearchTokens = new[] {
            "computerx",
            "cpu",
            "disk",
            "disk_space",
            "host",
            "memory",
            "patching",
            "remote_analysis",
            "server",
            "storage",
            "system",
            "uptime"
        },
        CapabilityParity = SystemToolPackParity.Slices
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterSystemPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistrySystemExtensions.GetRegisteredToolCatalog(_options);
    }

    /// <inheritdoc />
    public ToolPackInfoModel GetPackGuidance() {
        return SystemPackInfoTool.BuildGuidance(_options);
    }
}
