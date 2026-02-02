using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the initial response from an MCP OAuth login start.
/// </summary>
public sealed class McpOauthLoginStart {
    /// <summary>
    /// Initializes a new MCP OAuth login start model.
    /// </summary>
    public McpOauthLoginStart(string? loginId, string? authUrl, JsonObject raw, JsonObject? additional) {
        LoginId = loginId;
        AuthUrl = authUrl;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the login id.
    /// </summary>
    public string? LoginId { get; }
    /// <summary>
    /// Gets the authorization URL.
    /// </summary>
    public string? AuthUrl { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses the MCP OAuth login start payload from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed login start model.</returns>
    public static McpOauthLoginStart FromJson(JsonObject obj) {
        var loginId = obj.GetString("loginId");
        var authUrl = obj.GetString("authUrl") ?? obj.GetString("authorization_url");
        var additional = obj.ExtractAdditional("loginId", "authUrl", "authorization_url");
        return new McpOauthLoginStart(loginId, authUrl, obj, additional);
    }
}
