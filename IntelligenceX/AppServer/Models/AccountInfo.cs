namespace IntelligenceX.AppServer.Models;

public sealed class AccountInfo {
    public AccountInfo(string? email, string? planType, string? accountId) {
        Email = email;
        PlanType = planType;
        AccountId = accountId;
    }

    public string? Email { get; }
    public string? PlanType { get; }
    public string? AccountId { get; }
}
