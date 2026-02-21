namespace IntelligenceX.Tools;

/// <summary>
/// Stable error codes for write-governance authorization and enforcement.
/// </summary>
public static class ToolWriteGovernanceErrorCodes {
    /// <summary>
    /// Write operation id (idempotency key) is missing.
    /// </summary>
    public const string WriteOperationIdRequired = "write_operation_id_required";

    /// <summary>
    /// Explicit write confirmation argument is missing.
    /// </summary>
    public const string WriteConfirmationRequired = "write_confirmation_required";

    /// <summary>
    /// Write audit sink is required but not configured.
    /// </summary>
    public const string WriteAuditSinkRequired = "write_audit_sink_required";

    /// <summary>
    /// Write governance runtime is required but not configured.
    /// </summary>
    public const string WriteGovernanceRuntimeRequired = "write_governance_runtime_required";

    /// <summary>
    /// Write governance requirements are not met.
    /// </summary>
    public const string WriteGovernanceRequirementsNotMet = "write_governance_requirements_not_met";

    /// <summary>
    /// Write authorization denied by governance runtime.
    /// </summary>
    public const string WriteGovernanceDenied = "write_governance_denied";

    /// <summary>
    /// Audit sink append failed while processing write governance.
    /// </summary>
    public const string WriteAuditAppendFailed = "write_audit_append_failed";
}
