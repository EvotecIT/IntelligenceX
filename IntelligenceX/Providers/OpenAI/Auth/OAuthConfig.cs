using System;

namespace IntelligenceX.OpenAI.Auth;

/// <summary>
/// Configuration for the OAuth login flow.
/// </summary>
public sealed class OAuthConfig {
    /// <summary>
    /// Authorization endpoint URL.
    /// </summary>
    public string AuthorizeUrl { get; set; } = OpenAICodexDefaults.AuthorizeUrl;
    /// <summary>
    /// Token endpoint URL.
    /// </summary>
    public string TokenUrl { get; set; } = OpenAICodexDefaults.TokenUrl;
    /// <summary>
    /// OAuth client id.
    /// </summary>
    public string ClientId { get; set; } = OpenAICodexDefaults.ClientId;
    /// <summary>
    /// OAuth scopes string.
    /// </summary>
    public string Scopes { get; set; } = OpenAICodexDefaults.Scope;
    /// <summary>
    /// Redirect URI.
    /// </summary>
    public string RedirectUri { get; set; } = "";
    /// <summary>
    /// Redirect port when using a local listener.
    /// </summary>
    public int RedirectPort { get; set; } = 1455;
    /// <summary>
    /// Redirect path when using a local listener.
    /// </summary>
    public string RedirectPath { get; set; } = "/auth/callback";

    /// <summary>
    /// Whether to add organizations during login.
    /// </summary>
    public bool AddOrganizations { get; set; } = true;
    /// <summary>
    /// Whether to use the simplified Codex CLI flow.
    /// </summary>
    public bool CodexCliSimplifiedFlow { get; set; } = true;
    /// <summary>
    /// Originator identifier.
    /// </summary>
    public string Originator { get; set; } = "pi";

    /// <summary>
    /// Builds an OAuth configuration from environment variables.
    /// </summary>
    public static OAuthConfig FromEnvironment() {
        var cfg = new OAuthConfig {
            AuthorizeUrl = Environment.GetEnvironmentVariable("OPENAI_AUTH_AUTHORIZE_URL") ?? OpenAICodexDefaults.AuthorizeUrl,
            TokenUrl = Environment.GetEnvironmentVariable("OPENAI_AUTH_TOKEN_URL") ?? OpenAICodexDefaults.TokenUrl,
            ClientId = Environment.GetEnvironmentVariable("OPENAI_AUTH_CLIENT_ID") ?? OpenAICodexDefaults.ClientId,
            Scopes = Environment.GetEnvironmentVariable("OPENAI_AUTH_SCOPES") ?? OpenAICodexDefaults.Scope,
            RedirectUri = Environment.GetEnvironmentVariable("OPENAI_AUTH_REDIRECT_URL") ?? string.Empty
        };
        return cfg;
    }

    /// <summary>
    /// Validates required configuration values.
    /// </summary>
    public void Validate() {
        if (string.IsNullOrWhiteSpace(AuthorizeUrl)) {
            throw new InvalidOperationException("Missing OPENAI_AUTH_AUTHORIZE_URL.");
        }
        if (string.IsNullOrWhiteSpace(TokenUrl)) {
            throw new InvalidOperationException("Missing OPENAI_AUTH_TOKEN_URL.");
        }
        if (string.IsNullOrWhiteSpace(ClientId)) {
            throw new InvalidOperationException("Missing OPENAI_AUTH_CLIENT_ID.");
        }
    }
}
