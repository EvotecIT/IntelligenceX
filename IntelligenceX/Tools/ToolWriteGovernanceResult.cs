using System;

namespace IntelligenceX.Tools;

/// <summary>
/// Result of governance authorization for write execution.
/// </summary>
public sealed class ToolWriteGovernanceResult {
    /// <summary>
    /// Indicates whether write execution is authorized.
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// Stable error code when authorization fails.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Optional guidance hints for remediation.
    /// </summary>
    public IReadOnlyList<string> Hints { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether failure may be transient.
    /// </summary>
    public bool IsTransient { get; set; }
}
