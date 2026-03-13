namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Pack-level autonomy readiness derived from registered tool contracts.
/// </summary>
public sealed record ToolPackAutonomySummaryDto {
    /// <summary>
    /// Total number of tools registered for the pack.
    /// </summary>
    public int TotalTools { get; init; }
    /// <summary>
    /// Number of tools that can operate locally or remotely.
    /// </summary>
    public int RemoteCapableTools { get; init; }
    /// <summary>
    /// Remote-capable tool names.
    /// </summary>
    public string[] RemoteCapableToolNames { get; init; } = [];
    /// <summary>
    /// Number of tools that declare setup metadata.
    /// </summary>
    public int SetupAwareTools { get; init; }
    /// <summary>
    /// Number of tools that explicitly perform environment discovery/bootstrap.
    /// </summary>
    public int EnvironmentDiscoverTools { get; init; }
    /// <summary>
    /// Setup-aware tool names.
    /// </summary>
    public string[] SetupAwareToolNames { get; init; } = [];
    /// <summary>
    /// Environment-discovery tool names.
    /// </summary>
    public string[] EnvironmentDiscoverToolNames { get; init; } = [];
    /// <summary>
    /// Number of tools that declare outbound handoff metadata.
    /// </summary>
    public int HandoffAwareTools { get; init; }
    /// <summary>
    /// Handoff-aware tool names.
    /// </summary>
    public string[] HandoffAwareToolNames { get; init; } = [];
    /// <summary>
    /// Number of tools that declare recovery metadata.
    /// </summary>
    public int RecoveryAwareTools { get; init; }
    /// <summary>
    /// Recovery-aware tool names.
    /// </summary>
    public string[] RecoveryAwareToolNames { get; init; } = [];
    /// <summary>
    /// Number of tools that declare cross-pack handoffs.
    /// </summary>
    public int CrossPackHandoffTools { get; init; }
    /// <summary>
    /// Tool names that can hand off into other packs.
    /// </summary>
    public string[] CrossPackHandoffToolNames { get; init; } = [];
    /// <summary>
    /// Distinct target pack ids referenced by cross-pack handoffs.
    /// </summary>
    public string[] CrossPackTargetPacks { get; init; } = [];
}
