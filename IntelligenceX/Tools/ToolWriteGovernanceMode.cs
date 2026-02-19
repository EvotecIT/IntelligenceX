namespace IntelligenceX.Tools;

/// <summary>
/// Defines runtime enforcement mode for write-governance checks.
/// </summary>
public enum ToolWriteGovernanceMode {
    /// <summary>
    /// Enforce write-governance checks (default).
    /// </summary>
    Enforced = 0,

    /// <summary>
    /// Bypass write-governance checks for write-intent calls.
    /// Intended only for local/lab scenarios.
    /// </summary>
    Yolo = 1
}
