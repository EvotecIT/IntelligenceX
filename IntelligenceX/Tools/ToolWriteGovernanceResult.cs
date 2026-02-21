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
    /// Missing requirement names when authorization is denied.
    /// </summary>
    public IReadOnlyList<string> MissingRequirements { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional guidance hints for remediation.
    /// </summary>
    public IReadOnlyList<string> Hints { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Indicates whether failure may be transient.
    /// </summary>
    public bool IsTransient { get; set; }

    /// <summary>
    /// Idempotency key for write operation replay safety.
    /// </summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>
    /// Write execution identifier.
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// Immutable audit correlation identifier.
    /// </summary>
    public string AuditCorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Immutable audit provider selected by runtime policy.
    /// </summary>
    public string ImmutableAuditProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Rollback provider selected by runtime policy.
    /// </summary>
    public string RollbackProviderId { get; set; } = string.Empty;
}
