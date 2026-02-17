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
    AppServer,
    /// <summary>
    /// OpenAI-compatible HTTP transport (for example local providers such as Ollama/LM Studio).
    /// </summary>
    CompatibleHttp,
    /// <summary>
    /// GitHub Copilot CLI transport (subscription-backed).
    /// </summary>
    CopilotCli
}
