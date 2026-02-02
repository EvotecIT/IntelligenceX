namespace IntelligenceX.OpenAI;

/// <summary>
/// Identifies the OpenAI transport implementation.
/// </summary>
public enum OpenAITransportKind {
    /// <summary>
    /// Native HTTP transport.
    /// </summary>
    Native,
    /// <summary>
    /// App-server JSON-RPC transport.
    /// </summary>
    AppServer
}

