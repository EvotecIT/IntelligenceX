using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured maintenance-window descriptor for background scheduler policy and diagnostics.
/// </summary>
public sealed record SessionCapabilityBackgroundSchedulerMaintenanceWindowDto {
    /// <summary>
    /// Canonical maintenance-window spec string.
    /// </summary>
    public string Spec { get; init; } = string.Empty;

    /// <summary>
    /// Normalized day token (daily, mon, tue, wed, thu, fri, sat, sun).
    /// </summary>
    public string Day { get; init; } = string.Empty;

    /// <summary>
    /// Local start time in HH:mm form.
    /// </summary>
    public string StartTimeLocal { get; init; } = string.Empty;

    /// <summary>
    /// Window duration in minutes.
    /// </summary>
    public int DurationMinutes { get; init; }

    /// <summary>
    /// Optional normalized pack scope.
    /// </summary>
    public string PackId { get; init; } = string.Empty;

    /// <summary>
    /// Optional thread scope.
    /// </summary>
    public string ThreadId { get; init; } = string.Empty;

    /// <summary>
    /// Whether this window is scoped to a pack or thread instead of globally pausing the daemon.
    /// </summary>
    public bool Scoped { get; init; }
}
