using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Describes a plugin-style tool source exposed by the service/host.
/// </summary>
public sealed record PluginInfoDto {
    /// <summary>
    /// Stable plugin identifier.
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Human-friendly plugin name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Optional plugin version.
    /// </summary>
    public string? Version { get; init; }
    /// <summary>
    /// Plugin origin classification.
    /// </summary>
    public required string Origin { get; init; }
    /// <summary>
    /// Source kind classification for the plugin.
    /// </summary>
    public ToolPackSourceKind SourceKind { get; init; } = ToolPackSourceKind.OpenSource;
    /// <summary>
    /// Whether the plugin is enabled by default before runtime overrides.
    /// </summary>
    public bool DefaultEnabled { get; init; }
    /// <summary>
    /// Whether the plugin is enabled in the current session.
    /// </summary>
    public required bool Enabled { get; init; }
    /// <summary>
    /// Optional reason when the plugin is unavailable for this session.
    /// </summary>
    public string? DisabledReason { get; init; }
    /// <summary>
    /// Whether the plugin exposes dangerous/write capability.
    /// </summary>
    public required bool IsDangerous { get; init; }
    /// <summary>
    /// Normalized pack ids contributed by this plugin.
    /// </summary>
    public string[] PackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Optional root path for folder-based plugins.
    /// </summary>
    public string? RootPath { get; init; }
    /// <summary>
    /// Optional skill directories exposed by the plugin.
    /// </summary>
    public string[] SkillDirectories { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Optional resolved skill identifiers exposed by the plugin.
    /// </summary>
    public string[] SkillIds { get; init; } = Array.Empty<string>();
}
