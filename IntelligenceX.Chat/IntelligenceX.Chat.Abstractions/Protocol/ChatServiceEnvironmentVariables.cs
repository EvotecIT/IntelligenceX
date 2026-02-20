namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Canonical environment variable names shared across chat app/service launch paths.
/// </summary>
public static class ChatServiceEnvironmentVariables {
    /// <summary>
    /// Optional compatible-http Basic auth password used by service startup.
    /// </summary>
    public const string OpenAIBasicPassword = "INTELLIGENCEX_OPENAI_BASIC_PASSWORD";
}
