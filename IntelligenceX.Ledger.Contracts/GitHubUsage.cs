namespace IntelligenceX.Ledger.Contracts;

/// <summary>
/// One minute of one component's GitHub REST use for one rate-limit resource. <see cref="Charged"/> counts every
/// answer GitHub counts against the limit (2xx, 4xx, 5xx); <see cref="NotModified"/> counts the free 304 answers.
/// </summary>
public sealed record GitHubUsageMinute(DateTimeOffset MinuteUtc, string Resource, int Charged, int NotModified = 0);

/// <summary>The rate-limit headers of the last answer a component received for one resource.</summary>
public sealed record GitHubRateLimitReading(string Resource, int Limit, int Used, int Remaining, DateTimeOffset ResetAtUtc,
    DateTimeOffset ObservedAtUtc);

/// <summary>
/// One running component's tally. Each minute holds the running total for that process, so a repeated report
/// replaces the earlier one instead of adding to it; <see cref="Instance"/> separates a restarted process.
/// </summary>
public sealed record GitHubUsageReport(string Component, Guid Instance, GitHubUsageMinute[] Minutes,
    GitHubRateLimitReading[]? RateLimits = null);

/// <summary>Charged requests of one component on one machine.</summary>
public sealed record GitHubBudgetSpender(string Machine, string Component, int ChargedLastHour, int ChargedLast5Minutes,
    int NotModifiedLastHour, DateTimeOffset LastMinuteUtc) {
    /// <summary>The rate-limit window this component last saw, identified by its reset time.</summary>
    public DateTimeOffset? WindowResetAtUtc { get; init; }
}

/// <summary>
/// One GitHub rate-limit window as the components see it. Separate tokens or identities have separate windows, which
/// show up here with different reset times. <see cref="Unattributed"/> is what the window used that no reporting
/// component accounts for: ad-hoc <c>gh</c>, scripts and other tools on the same identity.
/// </summary>
public sealed record GitHubBudgetWindow(string Resource, int Limit, int Used, int Remaining, DateTimeOffset ResetAtUtc,
    DateTimeOffset ObservedAtUtc, string ObservedBy, int Reported, int Unattributed, string[] Components);

/// <summary>Who is spending the GitHub REST budget, now (last 5 minutes) and over the last hour.</summary>
public sealed record GitHubBudgetView(DateTimeOffset GeneratedAtUtc, GitHubBudgetWindow[] Windows, GitHubBudgetSpender[] Spenders,
    string[] Coverage) {
    /// <summary>Every reported core request in the last hour, including spenders beyond the ones listed.</summary>
    public int ChargedLastHour { get; init; }
}
