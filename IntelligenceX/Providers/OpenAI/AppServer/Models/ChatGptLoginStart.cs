using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ChatGptLoginStart {
    public ChatGptLoginStart(string loginId, string authUrl, JsonObject raw, JsonObject? additional) {
        LoginId = loginId;
        AuthUrl = authUrl;
        Raw = raw;
        Additional = additional;
    }

    public string LoginId { get; }
    public string AuthUrl { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ChatGptLoginStart FromJson(JsonObject obj) {
        var loginId = obj.GetString("loginId") ?? string.Empty;
        var authUrl = obj.GetString("authUrl") ?? obj.GetString("authorization_url") ?? string.Empty;
        var additional = obj.ExtractAdditional("loginId", "authUrl", "authorization_url");
        return new ChatGptLoginStart(loginId, authUrl, obj, additional);
    }
}
