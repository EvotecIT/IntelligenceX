using IntelligenceX.Telemetry.Limits;

namespace IntelligenceX.Tray.ViewModels;

public sealed partial class ProviderViewModel {
    private ProviderLimitSnapshot? _latestLimitSnapshot;

    /// <summary>Re-evaluates displayed advice as time passes, without polling or authenticating any provider.</summary>
    public void RefreshLimitReadingAge() {
        if (_latestLimitSnapshot is null) return;
        var current = ProviderLimitForecasting.BuildAccountAdvisories(_latestLimitSnapshot);
        // Rebuild only at a meaningful status/recommendation transition, preserving quiet UI between ticks.
        if (current.Count == LimitAccounts.Count && current.Select((advice, index) =>
                string.Equals(advice.DisplayLabel, LimitAccounts[index].Label, StringComparison.Ordinal)
                && string.Equals(advice.StatusLabel, LimitAccounts[index].StatusLabel, StringComparison.Ordinal)
                && advice.IsRecommended == LimitAccounts[index].IsRecommended).All(equal => equal)) return;
        ApplyLimitSnapshot(_latestLimitSnapshot);
    }
}
