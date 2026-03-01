using System.Collections.Generic;
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
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterPowerShellPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(PowerShellToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all IX.PowerShell tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterPowerShellPack(this ToolRegistry registry, PowerShellToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(PowerShellToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return PowerShellToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(PowerShellToolOptions options) {
        yield return new PowerShellPackInfoTool(options);
        yield return new PowerShellEnvironmentDiscoverTool(options);
        yield return new PowerShellHostsTool(options);
        yield return new PowerShellRunTool(options);
    }
}
