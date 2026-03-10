using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Declares tool-owned recovery behavior used for resilience inside the tool/engine boundary.
/// </summary>
public sealed class ToolRecoveryContract {
    /// <summary>
    /// Default contract id for IX tool recovery metadata.
    /// </summary>
    public const string DefaultContractId = "ix.tool-recovery.v1";

    /// <summary>
    /// True when the tool exposes recovery behavior metadata.
    /// </summary>
    public bool IsRecoveryAware { get; set; }

    /// <summary>
    /// Stable recovery contract identifier.
    /// </summary>
    public string RecoveryContractId { get; set; } = DefaultContractId;

    /// <summary>
    /// True when the tool supports retry for transient failures.
    /// </summary>
    public bool SupportsTransientRetry { get; set; }

    /// <summary>
    /// Maximum retry attempts for transient failures.
    /// </summary>
    public int MaxRetryAttempts { get; set; }

    /// <summary>
    /// Optional normalized retryable error codes.
    /// </summary>
    public IReadOnlyList<string> RetryableErrorCodes { get; set; } = Array.Empty<string>();

    /// <summary>
    /// True when the tool can switch to alternate internal execution engines.
    /// </summary>
    public bool SupportsAlternateEngines { get; set; }

    /// <summary>
    /// Optional alternate engine identifiers used when primary execution fails.
    /// </summary>
    public IReadOnlyList<string> AlternateEngineIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional helper tool names that can restore context before retrying or resuming execution.
    /// These helpers should be read-only bootstrap/discovery tools when provided.
    /// </summary>
    public IReadOnlyList<string> RecoveryToolNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Validates the contract and throws when invalid.
    /// </summary>
    public void Validate() {
        if (!IsRecoveryAware) {
            return;
        }

        if (string.IsNullOrWhiteSpace(RecoveryContractId)) {
            throw new InvalidOperationException("RecoveryContractId is required when IsRecoveryAware is enabled.");
        }

        if (SupportsTransientRetry) {
            if (MaxRetryAttempts < 1) {
                throw new InvalidOperationException(
                    "MaxRetryAttempts must be greater than zero when SupportsTransientRetry is enabled.");
            }
        } else if (MaxRetryAttempts != 0) {
            throw new InvalidOperationException(
                "MaxRetryAttempts must be zero when SupportsTransientRetry is disabled.");
        }

        if (SupportsAlternateEngines) {
            if (AlternateEngineIds is null || AlternateEngineIds.Count == 0) {
                throw new InvalidOperationException(
                    "AlternateEngineIds must include at least one engine when SupportsAlternateEngines is enabled.");
            }

            if (!HasAnyNonEmptyToken(AlternateEngineIds)) {
                throw new InvalidOperationException(
                    "AlternateEngineIds must include at least one non-empty engine identifier.");
            }
        }

        if (RecoveryToolNames is not null && RecoveryToolNames.Count > 0 && !HasAnyNonEmptyToken(RecoveryToolNames)) {
            throw new InvalidOperationException(
                "RecoveryToolNames must include at least one non-empty tool name when provided.");
        }
    }

    private static bool HasAnyNonEmptyToken(IReadOnlyList<string>? values) {
        if (values is null || values.Count == 0) {
            return false;
        }

        for (var i = 0; i < values.Count; i++) {
            if (!string.IsNullOrWhiteSpace(values[i])) {
                return true;
            }
        }

        return false;
    }
}
