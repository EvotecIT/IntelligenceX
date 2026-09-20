using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestBankedResetPlannerPreservesEvidenceAndAccountScope() {
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var account = new ProviderLimitAccountSnapshot("a", "Account A", "Pro",
            new[] { new ProviderLimitWindow("weekly", "Weekly", 95d, now.AddDays(2)) },
            null, null, now);
        BankedResetCredit Credit(string id, string accountId, DateTimeOffset? expiry,
            DateTimeOffset? used = null, ResetCreditEvidence source = ResetCreditEvidence.Manual) =>
            new(id, "codex", accountId, ResetCreditScope.Full, source, now, expiry, used);
        var manual = Credit("manual", "a", now.AddDays(3));
        var credits = new[] {
            Credit("other-account", "b", now.AddMinutes(1)),
            Credit("used", "a", now.AddMinutes(1), now),
            Credit("expired", "a", now),
            Credit("unknown-expiry", "a", null),
            manual,
            Credit("later", "a", now.AddDays(5), source: ResetCreditEvidence.ProviderReported)
        };
        var advice = BankedResetPlanner.Build("codex", account, credits, now, TimeSpan.FromMinutes(10));
        AssertEqual("manual", advice.CreditToReview?.Id, "earliest unexpired matching account credit");
        AssertContainsText(advice.Summary, "Manual entry", "manual provenance is explicit");
        AssertContainsText(advice.Summary, "verify", "manual credit is not redemption authority");
        AssertContainsText(advice.Summary, "Capacity is low", "weekly-only account can receive guidance");
        AssertEqual(1, account.Windows.Count, "planner preserves optional short window");
        var noMatch = BankedResetPlanner.Build("another-provider", account, credits, now, TimeSpan.FromMinutes(10));
        AssertEqual(true, noMatch.CreditToReview is null, "never pool between providers");
        AssertContainsText(noMatch.Summary, "may be incomplete", "empty inventory is not proof of zero balance");
    }

    private static void TestBankedResetPlannerDoesNotInferWindowActivation() {
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        var credit = new BankedResetCredit("reset", "codex", "a", ResetCreditScope.Full,
            ResetCreditEvidence.ProviderReported, now, now.AddDays(1));
        BankedResetAdvice Advice(double? used, DateTimeOffset reset, DateTimeOffset observed) =>
            BankedResetPlanner.Build("codex",
                new ProviderLimitAccountSnapshot("a", null, null,
                    new[] { new ProviderLimitWindow("weekly", "Weekly", used, reset) }, null, null, observed),
                new[] { credit }, now, TimeSpan.FromMinutes(10));
        AssertContainsText(Advice(95d, now, now).Summary, "activation is not established", "past reset is not activation");
        AssertContainsText(Advice(95d, now.AddDays(2), now.AddHours(-1)).Summary, "Refresh", "stale readings cannot drive timing");
        AssertContainsText(Advice(null, now.AddDays(2), now).Summary, "unknown", "unknown usage is not empty capacity");
        AssertContainsText(Advice(95d, now.AddMinutes(30), now).Summary, "Consider waiting", "near normal reset preserves credit");
        AssertContainsText(Advice(40d, now.AddDays(2), now).Summary, "reserve", "remaining capacity is not discarded automatically");
    }
}
