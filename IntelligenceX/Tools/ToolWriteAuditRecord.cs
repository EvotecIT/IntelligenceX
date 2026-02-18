namespace IntelligenceX.Tools;

/// <summary>
/// Immutable write-governance audit record captured at authorization time.
/// </summary>
public sealed class ToolWriteAuditRecord {
    /// <summary>
    /// UTC timestamp when record was emitted.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Tool name invoked by host.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Canonical tool name.
    /// </summary>
    public string CanonicalToolName { get; set; } = string.Empty;

    /// <summary>
    /// Governance contract id used by tool.
    /// </summary>
    public string GovernanceContractId { get; set; } = string.Empty;

    /// <summary>
    /// Indicates authorization result.
    /// </summary>
    public bool IsAuthorized { get; set; }

    /// <summary>
    /// Stable denial code when not authorized.
    /// </summary>
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable denial reason.
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Correlation identifier for this write execution attempt.
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// Correlation identifier for immutable audit streams.
    /// </summary>
    public string AuditCorrelationId { get; set; } = string.Empty;

    /// <summary>
    /// Actor identifier.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// Change reason provided by caller.
    /// </summary>
    public string ChangeReason { get; set; } = string.Empty;

    /// <summary>
    /// Rollback plan identifier provided by caller.
    /// </summary>
    public string RollbackPlanId { get; set; } = string.Empty;

    /// <summary>
    /// Immutable audit provider selected by runtime.
    /// </summary>
    public string ImmutableAuditProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Rollback provider selected by runtime.
    /// </summary>
    public string RollbackProviderId { get; set; } = string.Empty;
}
