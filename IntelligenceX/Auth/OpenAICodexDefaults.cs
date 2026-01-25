namespace IntelligenceX.Auth;

internal static class OpenAICodexDefaults {
    public const string Provider = "openai-codex";
    public const string AuthorizeUrl = "https://auth.openai.com/oauth/authorize";
    public const string TokenUrl = "https://auth.openai.com/oauth/token";
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";
    public const string Scope = "openid profile email offline_access";
    public const string AuthClaim = "https://api.openai.com/auth";
    public const string AccountIdClaim = "chatgpt_account_id";
}
