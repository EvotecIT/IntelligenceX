using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

public sealed class CopilotAuthStatus {
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

    public bool IsAuthenticated { get; }
    public string? AuthType { get; }
    public string? Host { get; }
    public string? Login { get; }
    public string? StatusMessage { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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
