using System;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Safety and execution limits for DomainDetective tools.
/// </summary>
public sealed class DomainDetectiveToolOptions {
    /// <summary>
    /// Default timeout for a single domain verification call.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 60000;

    /// <summary>
    /// Maximum timeout allowed for domain verification calls.
    /// </summary>
    public int MaxTimeoutMs { get; set; } = 240000;

    /// <summary>
    /// Maximum number of health checks allowed in a single call.
    /// </summary>
    public int MaxChecks { get; set; } = 16;

    /// <summary>
    /// Maximum number of remediation hints returned in summary outputs.
    /// </summary>
    public int MaxHints { get; set; } = 20;

    /// <summary>
    /// Validates option values.
    /// </summary>
    public void Validate() {
        if (DefaultTimeoutMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(DefaultTimeoutMs), "DefaultTimeoutMs must be positive.");
        }

        if (MaxTimeoutMs < DefaultTimeoutMs) {
            throw new ArgumentOutOfRangeException(nameof(MaxTimeoutMs), "MaxTimeoutMs must be greater than or equal to DefaultTimeoutMs.");
        }

        if (MaxChecks <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxChecks), "MaxChecks must be positive.");
        }

        if (MaxHints <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxHints), "MaxHints must be positive.");
        }
    }
}

