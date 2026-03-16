using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Describes an active background-scheduler suppression entry for a pack or thread.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerSuppressionDto {
    /// <summary>
    /// Normalized pack id or thread id covered by this suppression.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Stable source/mode token for the suppression.
    /// Values: persistent_default, persistent_runtime, temporary_runtime.
    /// </summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Whether the suppression is temporary and expected to expire automatically.
    /// </summary>
    public bool Temporary { get; init; }

    /// <summary>
    /// Best-effort UTC ticks when the temporary suppression expires. Zero for persistent suppressions.
    /// </summary>
    public long ExpiresUtcTicks { get; init; }
}
