using System;

namespace IntelligenceX.Telemetry.Limits;

/// <summary>Evidence behind a reset inventory entry, independent of current redemption eligibility.</summary>
public enum ResetCreditEvidence {
    /// <summary>Entered by the user; must be verified with the provider before use.</summary>
    Manual,
    /// <summary>Reported by a provider at the recorded observation time.</summary>
    ProviderReported
}

/// <summary>Windows a reset credit is reported to restore. No short window is assumed to exist.</summary>
public enum ResetCreditScope {
    /// <summary>The provider or user has not specified the affected windows.</summary>
    Unknown,
    /// <summary>All applicable windows.</summary>
    Full,
    /// <summary>The weekly window only.</summary>
    Weekly,
    /// <summary>The short window only, if the account has one.</summary>
    ShortWindow
}

/// <summary>
/// An account-scoped inventory record, not authority to redeem a reset.
/// Unknown expiry is preserved, and recording use does not call the provider.
/// </summary>
public sealed class BankedResetCredit {
    /// <summary>Creates a reset record with explicit provenance and observation time.</summary>
    public BankedResetCredit(string id, string providerId, string accountId, ResetCreditScope scope,
        ResetCreditEvidence evidence, DateTimeOffset observedAtUtc, DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? recordedUsedAtUtc = null) {
        Id = Required(id, nameof(id));
        ProviderId = Required(providerId, nameof(providerId));
        AccountId = Required(accountId, nameof(accountId));
        if (!Enum.IsDefined(typeof(ResetCreditScope), scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(typeof(ResetCreditEvidence), evidence)) throw new ArgumentOutOfRangeException(nameof(evidence));
        Scope = scope;
        Evidence = evidence;
        ObservedAtUtc = observedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        RecordedUsedAtUtc = recordedUsedAtUtc;
    }

    /// <summary>Stable inventory ID, distinct from the account ID.</summary>
    public string Id { get; }
    /// <summary>Provider owning the credit.</summary>
    public string ProviderId { get; }
    /// <summary>Exact account identity; credits are never pooled between accounts.</summary>
    public string AccountId { get; }
    /// <summary>Reported affected windows.</summary>
    public ResetCreditScope Scope { get; }
    /// <summary>Origin of the inventory entry.</summary>
    public ResetCreditEvidence Evidence { get; }
    /// <summary>When this entry was last verified or entered.</summary>
    public DateTimeOffset ObservedAtUtc { get; }
    /// <summary>Known expiry instant, or null when not reported.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; }
    /// <summary>Locally recorded use, not a redemption receipt.</summary>
    public DateTimeOffset? RecordedUsedAtUtc { get; }

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException("A stable identity is required.", name) : value.Trim();
}
