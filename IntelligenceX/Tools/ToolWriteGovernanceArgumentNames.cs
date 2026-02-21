using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Canonical argument names used by IX write-governance metadata contracts.
/// </summary>
public static class ToolWriteGovernanceArgumentNames {
    /// <summary>
    /// Idempotency key for write operation replay safety.
    /// </summary>
    public const string OperationId = "write_operation_id";

    /// <summary>
    /// Write execution identifier argument.
    /// </summary>
    public const string ExecutionId = "write_execution_id";

    /// <summary>
    /// Write actor identifier argument.
    /// </summary>
    public const string ActorId = "write_actor_id";

    /// <summary>
    /// Write change reason argument.
    /// </summary>
    public const string ChangeReason = "write_change_reason";

    /// <summary>
    /// Write rollback plan identifier argument.
    /// </summary>
    public const string RollbackPlanId = "write_rollback_plan_id";

    /// <summary>
    /// Optional write rollback provider identifier argument.
    /// </summary>
    public const string RollbackProviderId = "write_rollback_provider_id";

    /// <summary>
    /// Optional write audit correlation identifier argument.
    /// </summary>
    public const string AuditCorrelationId = "write_audit_correlation_id";

    /// <summary>
    /// Canonical governance metadata argument names expected on write-capable tool schemas.
    /// </summary>
    public static IReadOnlyList<string> CanonicalSchemaMetadataArguments { get; } = new[] {
        OperationId,
        ExecutionId,
        ActorId,
        ChangeReason,
        RollbackPlanId,
        RollbackProviderId,
        AuditCorrelationId
    };
}
