namespace IntelligenceX.Tools.Common;

/// <summary>
/// Coarse capability tier for a tool pack.
/// </summary>
public enum ToolCapabilityTier {
    /// <summary>
    /// Read-only operations on non-sensitive data.
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// Read-only operations that may access sensitive data.
    /// </summary>
    SensitiveRead = 1,

    /// <summary>
    /// Write/modify operations that are dangerous by nature.
    /// </summary>
    DangerousWrite = 2
}

