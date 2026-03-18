using System;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Aggregate autonomy surface available in the active chat session.
/// </summary>
public sealed record SessionCapabilityAutonomySummaryDto {
    /// <summary>
    /// Number of remote-capable tools in the enabled pack set.
    /// </summary>
    public int RemoteCapableToolCount { get; init; }

    /// <summary>
    /// Number of setup-aware tools in the enabled pack set.
    /// </summary>
    public int SetupAwareToolCount { get; init; }
    /// <summary>
    /// Number of target-scoped tools in the enabled pack set.
    /// </summary>
    public int TargetScopedToolCount { get; init; }
    /// <summary>
    /// Number of remote-host-targeting tools in the enabled pack set.
    /// </summary>
    public int RemoteHostTargetingToolCount { get; init; }
    /// <summary>
    /// Number of environment-discovery tools in the enabled pack set.
    /// </summary>
    public int EnvironmentDiscoverToolCount { get; init; }

    /// <summary>
    /// Number of handoff-aware tools in the enabled pack set.
    /// </summary>
    public int HandoffAwareToolCount { get; init; }

    /// <summary>
    /// Number of recovery-aware tools in the enabled pack set.
    /// </summary>
    public int RecoveryAwareToolCount { get; init; }
    /// <summary>
    /// Number of write-capable tools in the enabled pack set.
    /// </summary>
    public int WriteCapableToolCount { get; init; }
    /// <summary>
    /// Number of authentication-required tools in the enabled pack set.
    /// </summary>
    public int AuthenticationRequiredToolCount { get; init; }
    /// <summary>
    /// Number of probe-capable tools in the enabled pack set.
    /// </summary>
    public int ProbeCapableToolCount { get; init; }

    /// <summary>
    /// Number of tools that expose cross-pack handoffs in the enabled pack set.
    /// </summary>
    public int CrossPackHandoffToolCount { get; init; }

    /// <summary>
    /// Enabled pack ids that currently expose at least one remote-capable tool.
    /// </summary>
    public string[] RemoteCapablePackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled pack ids that currently expose at least one target-scoped tool.
    /// </summary>
    public string[] TargetScopedPackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled pack ids that currently expose at least one remote-host-targeting tool.
    /// </summary>
    public string[] RemoteHostTargetingPackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled pack ids that currently expose at least one environment-discovery tool.
    /// </summary>
    public string[] EnvironmentDiscoverPackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled pack ids that currently expose at least one write-capable tool.
    /// </summary>
    public string[] WriteCapablePackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled pack ids that currently expose at least one authentication-required tool.
    /// </summary>
    public string[] AuthenticationRequiredPackIds { get; init; } = Array.Empty<string>();
    /// <summary>
    /// Enabled pack ids that currently expose at least one probe-capable tool.
    /// </summary>
    public string[] ProbeCapablePackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Enabled pack ids that currently expose at least one cross-pack handoff.
    /// </summary>
    public string[] CrossPackReadyPackIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Distinct pack ids currently targeted by enabled cross-pack handoff routes.
    /// </summary>
    public string[] CrossPackTargetPackIds { get; init; } = Array.Empty<string>();
}
