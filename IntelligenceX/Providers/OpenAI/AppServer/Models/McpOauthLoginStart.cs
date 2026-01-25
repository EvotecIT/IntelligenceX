using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class McpOauthLoginStart {
    public McpOauthLoginStart(string? loginId, string? authUrl, JsonObject raw, JsonObject? additional) {
        LoginId = loginId;
        AuthUrl = authUrl;
        Raw = raw;
        Additional = additional;
    }

    public string? LoginId { get; }
    public string? AuthUrl { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static McpOauthLoginStart FromJson(JsonObject obj) {
        var loginId = obj.GetString("loginId");
        var authUrl = obj.GetString("authUrl") ?? obj.GetString("authorization_url");
        var additional = obj.ExtractAdditional("loginId", "authUrl", "authorization_url");
        return new McpOauthLoginStart(loginId, authUrl, obj, additional);
    }
}
