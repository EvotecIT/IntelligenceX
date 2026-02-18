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
}
