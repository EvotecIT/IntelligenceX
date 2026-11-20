using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Rules;

public sealed partial class LedgerRules {
    internal const string UsageLimitClearedCondition = "usage-limit-cleared";
    internal const string UsageLimitClearedAtCondition = "usage-limit-clearance-observed";
    internal const string UsageLimitResetCondition = "usage-limit-confirmed-reset";

    private (UsageLimitView? Limit, bool ConfirmedClear) UsageLimit(LedgerItem item, DateTimeOffset now) {
        if (item.Session is not { UsageLimit: true } session) return (null, false);
        var recorded = session.TurnEndedAtUtc ??
            item.ConditionFirstObservedAtUtc.GetValueOrDefault("usage-limit", session.LastActivityAtUtc ?? item.FirstObservedAtUtc);
        if (item.ConditionFirstObservedAtUtc.TryGetValue(UsageLimitClearedCondition, out var clearedStop) &&
            clearedStop == recorded && item.ConditionFirstObservedAtUtc.TryGetValue(UsageLimitClearedAtCondition, out var clearedAt)) {
            var knownReset = item.ConditionFirstObservedAtUtc.TryGetValue(UsageLimitResetCondition, out var resetAt)
                ? resetAt : (DateTimeOffset?)null;
            if ((knownReset ?? clearedAt) <= now) return (new(recorded, knownReset, false), true);
        }
        // The prior matches retain independent limit evidence when another hold supplies the primary verdict.
        var prior = item.Current;
        var cleared = false;
        if (prior is not null && prior.EvaluatedAtUtc <= now &&
            prior.Matches.Any(match => match is { Verdict: "stuck", ReasonCode: "usage-limit" } && match.SinceUtc == recorded)) {
            var episode = Match(item, "stuck", "usage-limit", recorded, "source-event", null, null).EpisodeId;
            cleared = prior.Matches.Any(match => match is { Verdict: "stuck", ReasonCode: "usage-limit" } &&
                match.SinceUtc == recorded && match.EpisodeId == episode);
        }
        var fallback = new UsageLimitView(recorded, null, !cleared && now - recorded < settings.UsageLimitWindow);
        var account = _accountLimit?.Invoke(item);
        if (account is null || account.Machine != item.MachineId || account.Provider != item.Provider ||
            account.AccountId is null || !string.Equals(account.AccountId, session.AccountId, StringComparison.OrdinalIgnoreCase) ||
            account.ObservedAtUtc <= recorded || account.ObservedAtUtc > now) return (fallback, false);
        var available = account.AvailableAtUtc ?? AccountAvailableAt(account);
        if (available > recorded && available <= now)
            return (fallback with { InForce = false }, true);

        // A reset from a later window cannot extend the older stop. An exceeded window without a reset
        // still uses the fallback, even when another exceeded window has a known reset.
        var windows = new (double? Used, DateTimeOffset? Reset, int? Minutes, DateTimeOffset At)[] {
            (account.UsedPercent, account.ResetsAtUtc, account.WindowMinutes, account.WindowObservedAtUtc ?? account.ObservedAtUtc),
            (account.ShortUsedPercent, account.ShortResetsAtUtc, 300, account.ShortWindowObservedAtUtc ?? account.ObservedAtUtc)
        }.Where(window => window.At > recorded && window.At <= now).ToArray();
        var reached = windows.Where(window => window.Used >= 100).ToArray();
        if (reached.Length == 0 && account.LimitReached == true) {
            var reported = windows.Where(window => window.Reset.HasValue).ToArray();
            if (reported is [{ Used: null }]) reached = reported;
        }
        if (reached.Length == 0) return (fallback, false);
        var reset = reached.Where(window => window.Reset > recorded &&
                (window.Minutes is not > 0 || window.Reset!.Value - recorded <= TimeSpan.FromMinutes(window.Minutes.Value)))
            .Select(window => window.Reset).Max();
        return reset is { } at && (!reached.Any(window => window.Reset is null) || at - recorded >= settings.UsageLimitWindow)
            ? (new(recorded, at, now < at), now >= at)
            : (fallback, false);
    }

    /// <summary>The latest time both windows were available, or when the provider explicitly allowed usage.</summary>
    internal static DateTimeOffset? AccountAvailableAt(AccountLimit account) {
        if (account.LimitReached == false) return account.ObservedAtUtc;
        if (account.Provider != "claude" || account.LimitReached == true ||
            account.UsedPercent is not < 100 || account.ShortUsedPercent is not < 100) return null;
        var main = account.WindowObservedAtUtc ?? account.ObservedAtUtc;
        var shortWindow = account.ShortWindowObservedAtUtc ?? account.ObservedAtUtc;
        return main == shortWindow ? main : null;
    }

    private static RuleMatch MatchUsageLimit(LedgerItem item, UsageLimitView limit) =>
        Match(item, limit.InForce ? "needs-you" : "stuck", "usage-limit", limit.RecordedAtUtc,
            item.Session?.TurnEndedAtUtc is null ? "first-observation" : "source-event", null, null);
}
