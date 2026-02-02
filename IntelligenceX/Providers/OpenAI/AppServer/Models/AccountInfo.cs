using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents account metadata returned by the app-server.
/// </summary>
public sealed class AccountInfo {
    /// <summary>
    /// Initializes a new account info model.
    /// </summary>
    public AccountInfo(string? email, string? planType, string? accountId, JsonObject raw, JsonObject? additional) {
        Email = email;
        PlanType = planType;
        AccountId = accountId;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the account email.
    /// </summary>
    public string? Email { get; }
    /// <summary>
    /// Gets the plan type identifier.
    /// </summary>
    public string? PlanType { get; }
    /// <summary>
    /// Gets the account id.
    /// </summary>
    public string? AccountId { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses account info from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed account info.</returns>
    public static AccountInfo FromJson(JsonObject obj) {
        var email = obj.GetString("email");
        var planType = obj.GetString("planType") ?? obj.GetString("plan_type");
        var accountId = obj.GetString("id");
        var additional = obj.ExtractAdditional("email", "planType", "plan_type", "id");
        return new AccountInfo(email, planType, accountId, obj, additional);
    }
}
