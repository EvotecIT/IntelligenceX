using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestProviderLimitAdvisoryRequiresCapacityEvidence() {
        var now = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
        foreach (var used in new double?[] { null, double.NaN, double.PositiveInfinity, -1d }) {
            var account = new ProviderLimitAccountSnapshot("a", "Account A", "Pro",
                new[] { new ProviderLimitWindow("weekly", "Weekly", used, now.AddDays(2)) },
                null, null, now, isSelected: true);
            var snapshot = new ProviderLimitSnapshot("codex", "Codex", "API", null, null,
                account.Windows, null, null, now, new[] { account });
            var result = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now).Single();
            AssertEqual(false, result.IsRecommended, "unknown capacity is not recommended");
            AssertEqual("Unknown", result.StatusLabel, "unknown capacity is not clear");
            AssertEqual(true, ProviderLimitForecasting.BuildForecast(account.Windows[0], now) is null,
                "invalid capacity has no forecast");
        }

        foreach (var windows in new[] {
                     Array.Empty<ProviderLimitWindow>(),
                     new[] { new ProviderLimitWindow("weekly", "Weekly", 0d, now) }
                 }) {
            var snapshot = new ProviderLimitSnapshot("codex", "Codex", "API", null, null,
                windows, null, null, now);
            var result = ProviderLimitForecasting.BuildAccountAdvisories(snapshot, now).Single();
            AssertEqual(false, result.IsRecommended, "missing or reset capacity is not recommended");
            AssertEqual(windows.Length == 0 ? "Unavailable" : "Refresh needed", result.StatusLabel,
                "missing and passed-reset readings have explicit states");
        }
    }
}
