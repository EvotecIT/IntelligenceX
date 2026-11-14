namespace IntelligenceX.Ledger.Contracts;

/// <summary>Provider cleanup must recheck local ownership and storage before applying this plan.</summary>
public sealed record SessionCleanupPlan(long PolicyRevision, DateTimeOffset AtUtc, double IdleArchiveHours,
    SessionCleanupEntry[] Archivable, SessionCleanupEntry[] Held, SessionCleanupEntry[] YoursIdle);
public sealed record SessionCleanupEntry(string Id, string Machine, string Provider, string? Title, string Origin,
    double IdleDays, string? Worktree, string? Branch, string Reason);
