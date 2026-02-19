using System.Collections.Generic;
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
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterReviewerSetupPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(ReviewerSetupToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all reviewer setup tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterReviewerSetupPack(this ToolRegistry registry, ReviewerSetupToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(ReviewerSetupToolOptions options) {
        yield return new ReviewerSetupPackInfoTool(options);
        yield return new ReviewerSetupContractVerifyTool(options);
    }
}
