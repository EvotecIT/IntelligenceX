namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Structured startup/bootstrap telemetry for tooling initialization.
/// </summary>
public sealed record SessionStartupBootstrapTelemetryDto {
    /// <summary>
    /// Total tooling bootstrap duration in milliseconds.
    /// </summary>
    public long TotalMs { get; init; }

    /// <summary>
    /// Runtime policy context construction duration in milliseconds.
    /// </summary>
    public long RuntimePolicyMs { get; init; }

    /// <summary>
    /// Runtime bootstrap option mapping duration in milliseconds.
    /// </summary>
    public long BootstrapOptionsMs { get; init; }

    /// <summary>
    /// Tool pack bootstrap/load duration in milliseconds.
    /// </summary>
    public long PackLoadMs { get; init; }

    /// <summary>
    /// Registry build duration in milliseconds.
    /// </summary>
    public long RegistryMs { get; init; }

    /// <summary>
    /// Total registered tool definitions.
    /// </summary>
    public int Tools { get; init; }

    /// <summary>
    /// Number of packs loaded into the runtime.
    /// </summary>
    public int PacksLoaded { get; init; }

    /// <summary>
    /// Number of packs present in availability metadata but disabled.
    /// </summary>
    public int PacksDisabled { get; init; }

    /// <summary>
    /// Number of effective plugin search roots.
    /// </summary>
    public int PluginRoots { get; init; }

    /// <summary>
    /// Total number of slow pack-load steps detected.
    /// </summary>
    public int SlowPackCount { get; init; }

    /// <summary>
    /// Number of slow pack-load steps included in the summary top list.
    /// </summary>
    public int SlowPackTopCount { get; init; }

    /// <summary>
    /// Number of pack bootstrap steps processed by startup progress tracking.
    /// </summary>
    public int PackProgressProcessed { get; init; }

    /// <summary>
    /// Total number of pack bootstrap steps scheduled by startup progress tracking.
    /// </summary>
    public int PackProgressTotal { get; init; }

    /// <summary>
    /// Total number of slow plugin loads detected.
    /// </summary>
    public int SlowPluginCount { get; init; }

    /// <summary>
    /// Number of slow plugin loads included in the summary top list.
    /// </summary>
    public int SlowPluginTopCount { get; init; }

    /// <summary>
    /// Number of plugin folders processed by startup progress tracking.
    /// </summary>
    public int PluginProgressProcessed { get; init; }

    /// <summary>
    /// Total number of plugin folders scheduled by startup progress tracking.
    /// </summary>
    public int PluginProgressTotal { get; init; }
}
