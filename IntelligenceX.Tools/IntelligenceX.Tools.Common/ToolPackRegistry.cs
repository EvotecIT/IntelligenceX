using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared helpers for pack-level tool registration and catalog projection.
/// </summary>
public static class ToolPackRegistry {
    /// <summary>
    /// Returns tool names for tools created by the provided factory.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames<TOptions>(
        TOptions options,
        Func<TOptions, IEnumerable<ITool>> createTools,
        string optionsParamName = "options")
        where TOptions : class {
        ArgumentNullException.ThrowIfNull(options, optionsParamName);
        ArgumentNullException.ThrowIfNull(createTools);

        return createTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool-catalog entries for tools created by the provided factory.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog<TOptions>(
        TOptions options,
        Func<TOptions, IEnumerable<ITool>> createTools,
        string optionsParamName = "options")
        where TOptions : class {
        ArgumentNullException.ThrowIfNull(options, optionsParamName);
        ArgumentNullException.ThrowIfNull(createTools);

        return ToolPackGuidance.CatalogFromTools(createTools(options));
    }

    /// <summary>
    /// Registers all tools produced by the provided factory into the target registry.
    /// </summary>
    public static ToolRegistry RegisterPack<TOptions>(
        ToolRegistry registry,
        TOptions options,
        Func<TOptions, IEnumerable<ITool>> createTools,
        string optionsParamName = "options")
        where TOptions : class {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options, optionsParamName);
        ArgumentNullException.ThrowIfNull(createTools);

        foreach (var tool in createTools(options)) {
            registry.Register(tool);
        }

        return registry;
    }
}
