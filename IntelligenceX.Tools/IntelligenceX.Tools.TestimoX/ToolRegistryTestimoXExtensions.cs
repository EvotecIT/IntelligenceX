using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Convenience registration helpers for the IX.TestimoX tool pack.
/// </summary>
public static class ToolRegistryTestimoXExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterTestimoXPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(TestimoXToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterTestimoXPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(TestimoXToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ToolPackGuidance.CatalogFromTools(CreateTools(options));
    }

    /// <summary>
    /// Registers all IX.TestimoX tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterTestimoXPack(this ToolRegistry registry, TestimoXToolOptions options) {
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

    private static IEnumerable<ITool> CreateTools(TestimoXToolOptions options) {
        yield return new TestimoXPackInfoTool(options);
        yield return new TestimoXRulesListTool(options);
        yield return new TestimoXRulesRunTool(options);
    }
}
