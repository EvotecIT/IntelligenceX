using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the start of a ChatGPT login flow.
/// </summary>
/// <example>
/// <code>
/// var login = await client.StartChatGptLoginAsync();
/// Console.WriteLine(login.AuthUrl);
/// </code>
/// </example>
public sealed class ChatGptLoginStart {
    public ChatGptLoginStart(string loginId, string authUrl, JsonObject raw, JsonObject? additional) {
        LoginId = loginId;
        AuthUrl = authUrl;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Login identifier.</summary>
    public string LoginId { get; }
    /// <summary>Authorization URL for completing login.</summary>
    public string AuthUrl { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a login response from JSON.</summary>
    public static ChatGptLoginStart FromJson(JsonObject obj) {
        var loginId = obj.GetString("loginId") ?? string.Empty;
        var authUrl = obj.GetString("authUrl") ?? obj.GetString("authorization_url") ?? string.Empty;
        var additional = obj.ExtractAdditional("loginId", "authUrl", "authorization_url");
        return new ChatGptLoginStart(loginId, authUrl, obj, additional);
    }
}
