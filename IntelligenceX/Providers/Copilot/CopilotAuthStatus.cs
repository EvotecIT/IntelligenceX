using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

/// <summary>
/// Represents the Copilot authentication status.
/// </summary>
public sealed class CopilotAuthStatus {
    /// <summary>
    /// Initializes a new auth status instance.
    /// </summary>
    public CopilotAuthStatus(bool isAuthenticated, string? authType, string? host, string? login, string? statusMessage,
        JsonObject raw, JsonObject? additional) {
        IsAuthenticated = isAuthenticated;
        AuthType = authType;
        Host = host;
        Login = login;
        StatusMessage = statusMessage;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets a value indicating whether the user is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; }
    /// <summary>
    /// Gets the auth type.
    /// </summary>
    public string? AuthType { get; }
    /// <summary>
    /// Gets the host name.
    /// </summary>
    public string? Host { get; }
    /// <summary>
    /// Gets the login name.
    /// </summary>
    public string? Login { get; }
    /// <summary>
    /// Gets the status message.
    /// </summary>
    public string? StatusMessage { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a Copilot auth status from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed status.</returns>
    public static CopilotAuthStatus FromJson(JsonObject obj) {
        var isAuthenticated = obj.GetBoolean("isAuthenticated");
        var authType = obj.GetString("authType");
        var host = obj.GetString("host");
        var login = obj.GetString("login");
        var statusMessage = obj.GetString("statusMessage");
        var additional = obj.ExtractAdditional(
            "isAuthenticated", "authType", "host", "login", "statusMessage");
        return new CopilotAuthStatus(isAuthenticated, authType, host, login, statusMessage, obj, additional);
    }
}
