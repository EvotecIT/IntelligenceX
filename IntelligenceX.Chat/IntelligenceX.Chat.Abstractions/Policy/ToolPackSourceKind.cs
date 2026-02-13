using System.Text.Json.Serialization;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Classifies where a tool pack comes from for UI and policy display.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ToolPackSourceKind>))]
public enum ToolPackSourceKind {
    /// <summary>
    /// Pack ships as part of core IntelligenceX distribution.
    /// </summary>
    Builtin,
    /// <summary>
    /// Pack comes from an open-source plugin/package.
    /// </summary>
    OpenSource,
    /// <summary>
    /// Pack comes from a private/closed-source plugin/package.
    /// </summary>
    ClosedSource
}
