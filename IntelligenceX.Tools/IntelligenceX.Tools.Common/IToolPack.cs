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

