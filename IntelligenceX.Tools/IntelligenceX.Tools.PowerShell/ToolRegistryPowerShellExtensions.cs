using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

/// <summary>
/// Convenience registration helpers for the IX.PowerShell tool pack.
/// </summary>
public static class ToolRegistryPowerShellExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterPowerShellPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(PowerShellToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterPowerShellPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(PowerShellToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ToolPackGuidance.CatalogFromTools(CreateTools(options));
    }

    /// <summary>
    /// Registers all IX.PowerShell tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterPowerShellPack(this ToolRegistry registry, PowerShellToolOptions options) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        foreach (var tool in CreateTools(options)) {
            registry.Register(tool);
        }
        return registry;
    }

    private static IEnumerable<ITool> CreateTools(PowerShellToolOptions options) {
        yield return new PowerShellPackInfoTool(options);
        yield return new PowerShellEnvironmentDiscoverTool(options);
        yield return new PowerShellHostsTool(options);
        yield return new PowerShellRunTool(options);
    }
}
