using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ReviewerSetup;

/// <summary>
/// Convenience registration helpers for the reviewer setup tool pack.
/// </summary>
public static class ToolRegistryReviewerSetupExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterReviewerSetupPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(ReviewerSetupToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterReviewerSetupPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(ReviewerSetupToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ToolPackGuidance.CatalogFromTools(CreateTools(options));
    }

    /// <summary>
    /// Registers all reviewer setup tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterReviewerSetupPack(this ToolRegistry registry, ReviewerSetupToolOptions options) {
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

    private static IEnumerable<ITool> CreateTools(ReviewerSetupToolOptions options) {
        yield return new ReviewerSetupPackInfoTool(options);
        yield return new ReviewerSetupContractVerifyTool(options);
    }
}
