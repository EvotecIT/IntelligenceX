using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Registration-first pack/plugin tooling snapshot embedded in the runtime capability summary.
/// </summary>
public sealed record SessionCapabilityToolingSnapshotDto {
    /// <summary>
    /// Best-effort source label describing how this tooling snapshot was resolved.
    /// </summary>
    public string? Source { get; init; }

    /// <summary>
    /// Exported pack summaries visible to the current runtime snapshot.
    /// </summary>
    public ToolPackInfoDto[] Packs { get; init; } = Array.Empty<ToolPackInfoDto>();

    /// <summary>
    /// Exported plugin/source summaries visible to the current runtime snapshot.
    /// </summary>
    public PluginInfoDto[] Plugins { get; init; } = Array.Empty<PluginInfoDto>();
}
