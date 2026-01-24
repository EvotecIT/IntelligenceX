namespace IntelligenceX.AppServer.Models;

public sealed class McpOauthLoginStart {
    public McpOauthLoginStart(string? loginId, string? authUrl) {
        LoginId = loginId;
        AuthUrl = authUrl;
    }

    public string? LoginId { get; }
    public string? AuthUrl { get; }
}
