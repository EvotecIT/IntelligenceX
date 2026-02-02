using System;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI.Native;

/// <summary>
/// Options for the ChatGPT native transport, including auth, endpoints, and defaults.
/// </summary>
/// <example>
/// <code>
/// var options = new OpenAINativeOptions {
///     ChatGptApiBaseUrl = "https://chatgpt.com/backend-api",
///     UserAgent = "IntelligenceX/0.1.0"
/// };
/// </code>
/// </example>
public sealed class OpenAINativeOptions {
    /// <summary>
    /// OAuth configuration used for ChatGPT sign-in.
    /// </summary>
    public OAuthConfig OAuth { get; } = OAuthConfig.FromEnvironment();
    /// <summary>
    /// Store used to load/save the auth bundle.
    /// </summary>
    public IAuthBundleStore AuthStore { get; set; } = new FileAuthBundleStore();

    /// <summary>
    /// Endpoint for responses in the native transport.
    /// </summary>
    public string ResponsesUrl { get; set; } = "https://chatgpt.com/backend-api/codex/responses";
    /// <summary>
    /// Endpoint list used to fetch available models.
    /// </summary>
    public string[] ModelUrls { get; set; } = new[] {
        "https://chatgpt.com/backend-api/codex/models",
        "https://chatgpt.com/backend-api/models"
    };
    /// <summary>
    /// Base URL for ChatGPT API calls (defaults to the public backend-api host).
    /// </summary>
    public string ChatGptApiBaseUrl { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_CHATGPT_API_BASE_URL") ?? "https://chatgpt.com/backend-api";
    public string ClientVersion { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_CLIENT_VERSION") ?? "0.0.0";
    public string Instructions { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_INSTRUCTIONS") ?? "You are a helpful assistant.";
    public ReasoningEffort? ReasoningEffort { get; set; } =
        ChatEnumParser.ParseReasoningEffort(Environment.GetEnvironmentVariable("INTELLIGENCEX_REASONING_EFFORT"));
    public ReasoningSummary? ReasoningSummary { get; set; } =
        ChatEnumParser.ParseReasoningSummary(Environment.GetEnvironmentVariable("INTELLIGENCEX_REASONING_SUMMARY"));
    public string Originator { get; set; } = "pi";
    public TextVerbosity TextVerbosity { get; set; } =
        ChatEnumParser.ParseTextVerbosity(Environment.GetEnvironmentVariable("INTELLIGENCEX_TEXT_VERBOSITY"))
        ?? TextVerbosity.Medium;
    public bool IncludeReasoningEncryptedContent { get; set; } = true;

    /// <summary>
    /// Timeout for OAuth login flows.
    /// </summary>
    public TimeSpan OAuthTimeout { get; set; } = TimeSpan.FromMinutes(3);
    /// <summary>
    /// Whether to use a local listener for OAuth callbacks.
    /// </summary>
    public bool UseLocalListener { get; set; } = true;

    public bool PersistCodexAuthJson { get; set; } = true;
    public string? CodexHome { get; set; }

    /// <summary>
    /// Optional user agent for HTTP requests.
    /// </summary>
    public string? UserAgent { get; set; }

    public void Validate() {
        if (!string.IsNullOrWhiteSpace(Originator)) {
            OAuth.Originator = Originator;
        }
        OAuth.Validate();
        if (AuthStore is null) {
            throw new ArgumentNullException(nameof(AuthStore));
        }
        if (string.IsNullOrWhiteSpace(ResponsesUrl)) {
            throw new ArgumentException("ResponsesUrl cannot be null or whitespace.", nameof(ResponsesUrl));
        }
        if (string.IsNullOrWhiteSpace(ChatGptApiBaseUrl)) {
            throw new ArgumentException("ChatGptApiBaseUrl cannot be null or whitespace.", nameof(ChatGptApiBaseUrl));
        }
        if (ModelUrls is null || ModelUrls.Length == 0) {
            throw new ArgumentException("ModelUrls cannot be null or empty.", nameof(ModelUrls));
        }
        if (string.IsNullOrWhiteSpace(ClientVersion)) {
            throw new ArgumentException("ClientVersion cannot be null or whitespace.", nameof(ClientVersion));
        }
        if (string.IsNullOrWhiteSpace(Instructions)) {
            throw new ArgumentException("Instructions cannot be null or whitespace.", nameof(Instructions));
        }
        if (OAuthTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(OAuthTimeout), "OAuthTimeout must be positive.");
        }
    }
}
