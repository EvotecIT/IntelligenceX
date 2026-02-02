using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Account details returned by the app-server.
/// </summary>
/// <example>
/// <code>
/// var account = await client.ReadAccountAsync();
/// Console.WriteLine(account.Email);
/// </code>
/// </example>
public sealed class AccountInfo {
    public AccountInfo(string? email, string? planType, string? accountId, JsonObject raw, JsonObject? additional) {
        Email = email;
        PlanType = planType;
        AccountId = accountId;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Account email (if available).</summary>
    public string? Email { get; }
    /// <summary>Plan type name (if available).</summary>
    public string? PlanType { get; }
    /// <summary>Account identifier (if available).</summary>
    public string? AccountId { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses account info from JSON.</summary>
    public static AccountInfo FromJson(JsonObject obj) {
        var email = obj.GetString("email");
        var planType = obj.GetString("planType") ?? obj.GetString("plan_type");
        var accountId = obj.GetString("id");
        var additional = obj.ExtractAdditional("email", "planType", "plan_type", "id");
        return new AccountInfo(email, planType, accountId, obj, additional);
    }
}
