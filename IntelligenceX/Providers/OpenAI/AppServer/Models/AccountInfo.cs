using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class AccountInfo {
    public AccountInfo(string? email, string? planType, string? accountId, JsonObject raw, JsonObject? additional) {
        Email = email;
        PlanType = planType;
        AccountId = accountId;
        Raw = raw;
        Additional = additional;
    }

    public string? Email { get; }
    public string? PlanType { get; }
    public string? AccountId { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static AccountInfo FromJson(JsonObject obj) {
        var email = obj.GetString("email");
        var planType = obj.GetString("planType") ?? obj.GetString("plan_type");
        var accountId = obj.GetString("id");
        var additional = obj.ExtractAdditional("email", "planType", "plan_type", "id");
        return new AccountInfo(email, planType, accountId, obj, additional);
    }
}
