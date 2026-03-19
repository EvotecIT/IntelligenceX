using System.Collections.Generic;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// A tool pack groups multiple tools and provides self-registration plus metadata for hosts/UIs.
/// </summary>
public interface IToolPack {
    /// <summary>
    /// Pack metadata used for policy/UI.
    /// </summary>
    ToolPackDescriptor Descriptor { get; }

    /// <summary>
    /// Registers all tools from this pack into the provided registry.
    /// </summary>
    void Register(ToolRegistry registry);
}

/// <summary>
/// Optional pack-owned tool catalog projection used by hosts to avoid duplicating pack metadata inference.
/// </summary>
public interface IToolPackCatalogProvider {
    /// <summary>
    /// Returns the normalized tool catalog published by this pack.
    /// </summary>
    IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog();
}

/// <summary>
/// Optional pack-owned planner guidance projection used by hosts to bootstrap planner-safe
/// pack hints without hardcoding pack-specific recipes or probe choices in Chat.
/// </summary>
public interface IToolPackGuidanceProvider {
    /// <summary>
    /// Returns the normalized pack guidance published by this pack's <c>*_pack_info</c> tool.
    /// </summary>
    ToolPackInfoModel GetPackGuidance();
}

/// <summary>
/// Optional runtime option key provider used to map host bootstrap option bags onto pack option instances.
/// </summary>
public interface IToolPackRuntimeOptionTarget {
    /// <summary>
    /// Returns normalized runtime option keys owned by this options type.
    /// </summary>
    IReadOnlyList<string> RuntimeOptionKeys { get; }
}
