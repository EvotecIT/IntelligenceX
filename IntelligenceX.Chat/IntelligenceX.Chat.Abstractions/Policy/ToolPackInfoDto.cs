namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Describes a tool pack exposed by the service/host.
/// </summary>
public sealed record ToolPackInfoDto {
    /// <summary>
    /// Stable pack id (machine-friendly).
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// Human-friendly pack name.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Optional pack description shown in host UX.
    /// </summary>
    public string? Description { get; init; }
    /// <summary>
    /// Capability tier for the pack.
    /// </summary>
    public required CapabilityTier Tier { get; init; }
    /// <summary>
    /// Whether the pack is enabled for the current session.
    /// </summary>
    public required bool Enabled { get; init; }
    /// <summary>
    /// Optional reason when the pack is unavailable for this session.
    /// </summary>
    public string? DisabledReason { get; init; }
    /// <summary>
    /// Whether the pack includes potentially dangerous/write operations.
    /// </summary>
    public required bool IsDangerous { get; init; }
    /// <summary>
    /// Pack provenance classification.
    /// </summary>
    public ToolPackSourceKind SourceKind { get; init; } = ToolPackSourceKind.OpenSource;
    /// <summary>
    /// Optional autonomy readiness summary derived from registered tools.
    /// </summary>
    public ToolPackAutonomySummaryDto? AutonomySummary { get; init; }
}
