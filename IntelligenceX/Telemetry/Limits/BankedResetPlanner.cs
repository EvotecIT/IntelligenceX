using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>Read-only guidance; no result implies provider permission to redeem or activate a window.</summary>
public sealed class BankedResetAdvice {
    internal BankedResetAdvice(string summary, BankedResetCredit? credit) {
        Summary = summary;
        CreditToReview = credit;
    }
    /// <summary>Human-readable reason and next verification step.</summary>
    public string Summary { get; }
    /// <summary>Earliest known expiry to review, not a selected redemption action.</summary>
    public BankedResetCredit? CreditToReview { get; }
}

/// <summary>Conservative reset planning shared by desktop surfaces.</summary>
public static class BankedResetPlanner {
    /// <summary>
    /// Reviews account-scoped inventory and fresh provider readings. Missing windows are never fabricated;
    /// a passed reset timestamp does not establish activation, and manual inventory always requires verification.
    /// </summary>
    public static BankedResetAdvice Build(string providerId, ProviderLimitAccountSnapshot account,
        IEnumerable<BankedResetCredit> credits, DateTimeOffset nowUtc, TimeSpan maximumReadingAge) {
        if (account is null) throw new ArgumentNullException(nameof(account));
        if (credits is null) throw new ArgumentNullException(nameof(credits));
        if (string.IsNullOrWhiteSpace(providerId)) throw new ArgumentException("Provider is required.", nameof(providerId));
        if (maximumReadingAge <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumReadingAge));
        var candidate = credits
            .Where(c => string.Equals(c.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(c.AccountId, account.AccountId, StringComparison.Ordinal)
                        && c.RecordedUsedAtUtc is null && c.ObservedAtUtc <= nowUtc
                        && (!c.ExpiresAtUtc.HasValue || c.ExpiresAtUtc > nowUtc))
            .OrderBy(c => c.ExpiresAtUtc ?? DateTimeOffset.MaxValue)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        if (candidate is null) return new BankedResetAdvice("No unexpired reset recorded for this account. Inventory may be incomplete.", null);
        var provenance = candidate.Evidence == ResetCreditEvidence.Manual
            ? "Manual entry; verify availability and expiry with the provider. "
            : "Provider-reported inventory; recheck redemption eligibility before use. ";
        if (account.RetrievedAtUtc > nowUtc || nowUtc - account.RetrievedAtUtc > maximumReadingAge
            || account.Windows.Count == 0) {
            return new BankedResetAdvice(provenance + "Refresh account limits before planning a reset.", candidate);
        }
        if (account.Windows.Any(w => w.ResetsAt <= nowUtc)) {
            return new BankedResetAdvice(provenance + "A reset time has passed. Refresh to confirm the next window; activation is not established.", candidate);
        }
        if (account.Windows.Any(w => !w.UsedPercent.HasValue || double.IsNaN(w.UsedPercent.Value)
                                    || double.IsInfinity(w.UsedPercent.Value) || w.UsedPercent < 0d)) {
            return new BankedResetAdvice(provenance + "Some capacity is unknown. No reset timing recommendation is available.", candidate);
        }
        if (candidate.Scope != ResetCreditScope.Full) {
            return new BankedResetAdvice(provenance + "Confirm which reported windows this credit restores before planning its use.", candidate);
        }
        var constrained = account.Windows.Where(w => w.UsedPercent >= 90d).ToArray();
        if (constrained.Length == 0) {
            return new BankedResetAdvice(provenance + "Capacity remains. Keep the reset in reserve and review its expiry before the next planned work session.", candidate);
        }
        if (constrained.All(w => w.ResetsAt.HasValue && w.ResetsAt.Value - nowUtc <= TimeSpan.FromHours(1))) {
            return new BankedResetAdvice(provenance + "Reported constrained windows reset within an hour. Consider waiting if the work can wait.", candidate);
        }
        return new BankedResetAdvice(provenance + "Capacity is low. Review the earliest-expiring reset if work cannot wait; a reset may discard remaining capacity. Start timing depends on confirmed provider behavior.", candidate);
    }
}
