using System;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.OpenAI.Native;

public sealed class OpenAINativeOptions {
    public OAuthConfig OAuth { get; } = OAuthConfig.FromEnvironment();
    public IAuthBundleStore AuthStore { get; set; } = new FileAuthBundleStore();

    public string ResponsesUrl { get; set; } = "https://chatgpt.com/backend-api/codex/responses";
    public string[] ModelUrls { get; set; } = new[] {
        "https://chatgpt.com/backend-api/codex/models",
        "https://chatgpt.com/backend-api/models"
    };
    public string ClientVersion { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_CLIENT_VERSION") ?? "0.0.0";
    public string Instructions { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_INSTRUCTIONS") ?? "You are a helpful assistant.";
    public string? ReasoningEffort { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_REASONING_EFFORT");
    public string? ReasoningSummary { get; set; } =
        Environment.GetEnvironmentVariable("INTELLIGENCEX_REASONING_SUMMARY");
    public string Originator { get; set; } = "pi";
    public string TextVerbosity { get; set; } = "medium";
    public bool IncludeReasoningEncryptedContent { get; set; } = true;

    public TimeSpan OAuthTimeout { get; set; } = TimeSpan.FromMinutes(3);
    public bool UseLocalListener { get; set; } = true;

    public bool PersistCodexAuthJson { get; set; } = true;
    public string? CodexHome { get; set; }

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
