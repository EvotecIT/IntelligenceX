using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Rules;

public sealed partial class LedgerRules {
    private UsageLimitView? UsageLimit(LedgerItem item, DateTimeOffset now) {
        if (item.Session is not { UsageLimit: true } session) return null;
        var recorded = session.TurnEndedAtUtc ??
            item.ConditionFirstObservedAtUtc.GetValueOrDefault("usage-limit", session.LastActivityAtUtc ?? item.FirstObservedAtUtc);
        // A previously cleared stop stays cleared while account observations are unavailable, including after restart.
        var cleared = item.Current is { Value: "stuck", ReasonCode: "usage-limit" } prior &&
            prior.SinceUtc == recorded && prior.EvaluatedAtUtc <= now &&
            prior.EpisodeId == Match(item, "stuck", "usage-limit", recorded, "source-event", null, null).EpisodeId;
        var fallback = new UsageLimitView(recorded, null, !cleared && now - recorded < settings.UsageLimitWindow);
        var account = _accountLimit?.Invoke(item);
        if (account is null || account.Machine != item.MachineId || account.Provider != item.Provider ||
            account.AccountId is null || !string.Equals(account.AccountId, session.AccountId, StringComparison.OrdinalIgnoreCase) ||
            account.ObservedAtUtc <= recorded || account.ObservedAtUtc > now) return fallback;
        if (account.LimitReached == false)
            return fallback with { InForce = false };

        // A reset from a later window cannot extend the older stop. An exceeded window without a reset
        // still uses the fallback, even when another exceeded window has a known reset.
        var windows = new (double? Used, DateTimeOffset? Reset, int? Minutes)[] {
            (account.UsedPercent, account.ResetsAtUtc, account.WindowMinutes),
            (account.ShortUsedPercent, account.ShortResetsAtUtc, 300)
        };
        var reached = windows.Where(window => window.Used >= 100).ToArray();
        if (reached.Length == 0 && account.LimitReached == true) {
            var reported = windows.Where(window => window.Reset.HasValue).ToArray();
            if (reported.Length == 1) reached = reported;
        }
        if (reached.Length == 0)
            return account.Provider == "claude" && account.UsedPercent is < 100 && account.ShortUsedPercent is < 100
                ? fallback with { InForce = false }
                : fallback;
        var reset = reached.Where(window => window.Reset > recorded &&
                (window.Minutes is not > 0 || window.Reset!.Value - recorded <= TimeSpan.FromMinutes(window.Minutes.Value)))
            .Select(window => window.Reset).Max();
        return reset is { } at && (!reached.Any(window => window.Reset is null) || at - recorded >= settings.UsageLimitWindow)
            ? new(recorded, at, now < at)
            : fallback;
    }
}
