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
        Description = "ComputerX host inventory, diagnostics, and governed service plus scheduled-task lifecycle operations.",
        SourceKind = "closed_source",
        EngineId = "computerx",
        Category = "system",
        CapabilityTags = new[] {
            "cpu",
            ToolPackCapabilityTags.GovernedWrite,
            "disk_space",
            "host_inventory",
            ToolPackCapabilityTags.LocalAnalysis,
            ToolPackCapabilityTags.LocalExecution,
            "memory",
            "patching",
            "performance",
            ToolPackCapabilityTags.RemoteAnalysis,
            ToolPackCapabilityTags.RemoteExecution,
            "service_lifecycle",
            "scheduled_task_lifecycle",
            "storage",
            ToolPackCapabilityTags.WriteCapable
        },
        SearchTokens = new[] {
            "computerx",
            "cpu",
            "disk",
            "disk_space",
            "governed_write",
            "host",
            "memory",
            "patching",
            "remote_analysis",
            "service_control",
            "service_lifecycle",
            "server",
            "scheduled_task_control",
            "scheduled_task_lifecycle",
            "start_service",
            "storage",
            "stop_service",
            "system",
            "task_scheduler",
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
