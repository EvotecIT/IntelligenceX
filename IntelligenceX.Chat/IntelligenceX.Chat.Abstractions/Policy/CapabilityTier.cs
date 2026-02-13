namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Defines the "risk tier" of a capability (tool pack / tool surface).
/// </summary>
public enum CapabilityTier {
    /// <summary>
    /// Read-only, low-risk operations (queries, listing, analysis).
    /// </summary>
    ReadOnly = 0,
    /// <summary>
    /// Read-only, but potentially sensitive (may expose secrets or sensitive identifiers).
    /// </summary>
    SensitiveRead = 1,
    /// <summary>
    /// Write or otherwise dangerous operations (state changes).
    /// </summary>
    DangerousWrite = 2
}

