namespace IntelligenceX.Tools;

/// <summary>
/// Canonical argument names used by IX write-governance metadata contracts.
/// </summary>
public static class ToolWriteGovernanceArgumentNames {
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
}
