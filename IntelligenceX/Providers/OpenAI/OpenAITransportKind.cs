namespace IntelligenceX.OpenAI;

/// <summary>
/// Available transports for OpenAI requests.
/// </summary>
public enum OpenAITransportKind {
    /// <summary>
    /// Native ChatGPT transport (browser-auth style).
    /// </summary>
    Native,
    /// <summary>
    /// App-server transport (Codex app server).
    /// </summary>
    AppServer
}

