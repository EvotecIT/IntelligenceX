using IntelligenceX.Json;

namespace IntelligenceX.Tools;

/// <summary>
/// Write authorization request passed to governance runtime.
/// </summary>
public sealed class ToolWriteGovernanceRequest {
    /// <summary>
    /// Tool name.
    /// </summary>
    public string ToolName { get; set; } = string.Empty;

    /// <summary>
    /// Canonical tool name.
    /// </summary>
    public string CanonicalToolName { get; set; } = string.Empty;

    /// <summary>
    /// Governance contract id.
    /// </summary>
    public string GovernanceContractId { get; set; } = string.Empty;

    /// <summary>
    /// Parsed arguments passed to the tool.
    /// </summary>
    public JsonObject? Arguments { get; set; }

    /// <summary>
    /// Name of confirmation argument when applicable.
    /// </summary>
    public string ConfirmationArgumentName { get; set; } = string.Empty;

    /// <summary>
    /// Write execution identifier.
    /// </summary>
    public string ExecutionId { get; set; } = string.Empty;

    /// <summary>
    /// Actor identifier.
    /// </summary>
    public string ActorId { get; set; } = string.Empty;

    /// <summary>
    /// Change reason/ticket metadata.
    /// </summary>
    public string ChangeReason { get; set; } = string.Empty;

    /// <summary>
    /// Rollback plan identifier.
    /// </summary>
    public string RollbackPlanId { get; set; } = string.Empty;

    /// <summary>
    /// Optional rollback provider identifier.
    /// </summary>
    public string RollbackProviderId { get; set; } = string.Empty;

    /// <summary>
    /// Optional audit correlation identifier.
    /// </summary>
    public string AuditCorrelationId { get; set; } = string.Empty;
}
