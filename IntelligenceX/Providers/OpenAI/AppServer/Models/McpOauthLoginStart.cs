using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the start of an MCP OAuth login flow.
/// </summary>
/// <example>
/// <code>
/// var login = await client.StartMcpOAuthLoginAsync("server-name");
/// Console.WriteLine(login.AuthUrl);
/// </code>
/// </example>
public sealed class McpOauthLoginStart {
    public McpOauthLoginStart(string? loginId, string? authUrl, JsonObject raw, JsonObject? additional) {
        LoginId = loginId;
        AuthUrl = authUrl;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Login identifier (if provided).</summary>
    public string? LoginId { get; }
    /// <summary>Authorization URL for completing login (if provided).</summary>
    public string? AuthUrl { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a login response from JSON.</summary>
    public static McpOauthLoginStart FromJson(JsonObject obj) {
        var loginId = obj.GetString("loginId");
        var authUrl = obj.GetString("authUrl") ?? obj.GetString("authorization_url");
        var additional = obj.ExtractAdditional("loginId", "authUrl", "authorization_url");
        return new McpOauthLoginStart(loginId, authUrl, obj, additional);
    }
}
