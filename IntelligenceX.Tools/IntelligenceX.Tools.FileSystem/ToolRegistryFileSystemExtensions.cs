using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.FileSystem;

/// <summary>
/// Convenience registration helpers for the FileSystem tool pack.
/// </summary>
public static class ToolRegistryFileSystemExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterFileSystemPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(FileSystemToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterFileSystemPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(FileSystemToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ToolPackGuidance.CatalogFromTools(CreateTools(options));
    }

    /// <summary>
    /// Registers all FileSystem tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterFileSystemPack(this ToolRegistry registry, FileSystemToolOptions options) {
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

    private static IEnumerable<ITool> CreateTools(FileSystemToolOptions options) {
        yield return new FileSystemPackInfoTool(options);
        yield return new FsListTool(options);
        yield return new FsReadTool(options);
        yield return new FsSearchTool(options);
    }
}
