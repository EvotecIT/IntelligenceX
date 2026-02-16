using System;
using IntelligenceX.Chat.Abstractions.Policy;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Per-tool probe result emitted by <see cref="ToolHealthMessage"/>.
/// </summary>
public sealed record ToolHealthProbeDto {
    /// <summary>
    /// Probe tool name (for example: <c>ad_pack_info</c>).
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Optional normalized pack identifier inferred from the tool name.
    /// </summary>
    public string? PackId { get; init; }

    /// <summary>
    /// Optional human-friendly pack display name.
    /// </summary>
    public string? PackName { get; init; }

    /// <summary>
    /// Pack provenance classification.
    /// </summary>
    public ToolPackSourceKind SourceKind { get; init; } = ToolPackSourceKind.OpenSource;

    /// <summary>
    /// Whether the probe returned a healthy result.
    /// </summary>
    public required bool Ok { get; init; }

    /// <summary>
    /// Optional stable error code for failed probes.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Optional error detail for failed probes.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// Probe execution duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// Response message for <see cref="CheckToolHealthRequest"/>.
/// </summary>
public sealed record ToolHealthMessage : ChatServiceMessage {
    /// <summary>
    /// Probe results for discovered pack probes.
    /// </summary>
    public ToolHealthProbeDto[] Probes { get; init; } = Array.Empty<ToolHealthProbeDto>();

    /// <summary>
    /// Number of probes that succeeded.
    /// </summary>
    public int OkCount { get; init; }

    /// <summary>
    /// Number of probes that failed.
    /// </summary>
    public int FailedCount { get; init; }
}
