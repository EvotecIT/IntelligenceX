using System;

namespace IntelligenceX.Auth;

public sealed class OAuthConfig {
    public string AuthorizeUrl { get; set; } = OpenAICodexDefaults.AuthorizeUrl;
    public string TokenUrl { get; set; } = OpenAICodexDefaults.TokenUrl;
    public string ClientId { get; set; } = OpenAICodexDefaults.ClientId;
    public string Scopes { get; set; } = OpenAICodexDefaults.Scope;
    public string RedirectUri { get; set; } = "";
    public int RedirectPort { get; set; } = 1455;
    public string RedirectPath { get; set; } = "/auth/callback";

    public bool AddOrganizations { get; set; } = true;
    public bool CodexCliSimplifiedFlow { get; set; } = true;
    public string Originator { get; set; } = "pi";

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
