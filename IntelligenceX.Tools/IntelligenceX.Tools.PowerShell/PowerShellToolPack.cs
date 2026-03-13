using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// IX.PowerShell tool pack (self-describing + self-registering).
/// </summary>
public sealed class PowerShellToolPack : IToolPack, IToolPackCatalogProvider {
    private readonly PowerShellToolOptions _options;

    /// <summary>
    /// Creates a new <see cref="PowerShellToolPack"/>.
    /// </summary>
    /// <param name="options">Pack options.</param>
    public PowerShellToolPack(PowerShellToolOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
    }

    /// <inheritdoc />
    public ToolPackDescriptor Descriptor { get; } = new() {
        Id = "powershell",
        Name = "PowerShell Runtime",
        Tier = ToolCapabilityTier.DangerousWrite,
        IsDangerous = true,
        Description = "Opt-in shell runtime execution (windows_powershell / pwsh / cmd).",
        SourceKind = "builtin",
        EngineId = "powershell_runtime",
        CapabilityTags = new[] { "local_execution", "remote_execution", "shell", "write_capable" }
    };

    /// <inheritdoc />
    public void Register(ToolRegistry registry) {
        registry.RegisterPowerShellPack(_options);
    }

    /// <inheritdoc />
    public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
        return ToolRegistryPowerShellExtensions.GetRegisteredToolCatalog(_options);
    }
}
