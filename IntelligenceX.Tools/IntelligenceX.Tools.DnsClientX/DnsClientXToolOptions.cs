using System;
using System.Collections.Generic;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// Safety and execution limits for DnsClientX tools.
/// </summary>
public sealed class DnsClientXToolOptions : IToolPackRuntimeOptionTarget {
    private static readonly IReadOnlyList<string> RuntimeOptionKeyValues = new[] {
        "dnsclientx"
    };

    /// <summary>
    /// Maximum number of records returned for each DNS section (answers/authority/additional).
    /// </summary>
    public int MaxAnswersPerSection { get; set; } = 200;

    /// <summary>
    /// Default DNS query timeout in milliseconds.
    /// </summary>
    public int DefaultTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Maximum allowed DNS query timeout in milliseconds.
    /// </summary>
    public int MaxTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Maximum retries allowed for DNS queries.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Maximum number of ping targets per call.
    /// </summary>
    public int MaxPingTargets { get; set; } = 16;

    /// <summary>
    /// Default ping timeout in milliseconds.
    /// </summary>
    public int DefaultPingTimeoutMs { get; set; } = 2500;

    /// <summary>
    /// Maximum ping timeout in milliseconds.
    /// </summary>
    public int MaxPingTimeoutMs { get; set; } = 10000;

    /// <inheritdoc />
    public IReadOnlyList<string> RuntimeOptionKeys => RuntimeOptionKeyValues;

    /// <summary>
    /// Validates option values.
    /// </summary>
    public void Validate() {
        if (MaxAnswersPerSection <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxAnswersPerSection), "MaxAnswersPerSection must be positive.");
        }

        if (DefaultTimeoutMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(DefaultTimeoutMs), "DefaultTimeoutMs must be positive.");
        }

        if (MaxTimeoutMs < DefaultTimeoutMs) {
            throw new ArgumentOutOfRangeException(nameof(MaxTimeoutMs), "MaxTimeoutMs must be greater than or equal to DefaultTimeoutMs.");
        }

        if (MaxRetries < 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), "MaxRetries cannot be negative.");
        }

        if (MaxPingTargets <= 0) {
            throw new ArgumentOutOfRangeException(nameof(MaxPingTargets), "MaxPingTargets must be positive.");
        }

        if (DefaultPingTimeoutMs <= 0) {
            throw new ArgumentOutOfRangeException(nameof(DefaultPingTimeoutMs), "DefaultPingTimeoutMs must be positive.");
        }

        if (MaxPingTimeoutMs < DefaultPingTimeoutMs) {
            throw new ArgumentOutOfRangeException(nameof(MaxPingTimeoutMs), "MaxPingTimeoutMs must be greater than or equal to DefaultPingTimeoutMs.");
        }
    }
}
