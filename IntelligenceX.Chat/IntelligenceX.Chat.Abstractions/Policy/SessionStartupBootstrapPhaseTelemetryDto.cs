namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured startup/bootstrap phase timing item.
/// </summary>
public sealed record SessionStartupBootstrapPhaseTelemetryDto {
    /// <summary>
    /// Stable phase identifier (for example runtime_policy, pack_load).
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display label for UI summaries.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Phase duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Stable phase order used for timeline rendering.
    /// </summary>
    public int Order { get; init; }
}
