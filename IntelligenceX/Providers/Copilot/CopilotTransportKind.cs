namespace IntelligenceX.Copilot;

/// <summary>
/// Describes the transport used to talk to Copilot.
/// </summary>
public enum CopilotTransportKind {
    /// <summary>
    /// Use the official Copilot CLI JSON-RPC transport.
    /// </summary>
    Cli,
    /// <summary>
    /// Use the experimental direct HTTP transport.
    /// </summary>
    /// <remarks>
    /// This transport is unsupported and may be removed without notice.
    /// </remarks>
    Direct
}
