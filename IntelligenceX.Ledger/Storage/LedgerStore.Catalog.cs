using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    /// <summary>Checkouts across every repository: open sessions plus any active since <paramref name="cutoff"/>.</summary>
    internal async Task<(string Repo, RepositorySession Session)[]> CatalogWorktreesAsync(DateTimeOffset cutoff, CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var rows = await db.QueryAsListAsync("""
            SELECT repo, json FROM repository_sessions
            WHERE (open=1 AND archived=0) OR activity>=@cutoff
            ORDER BY repo, item
            """, row => (row.GetString(0), Read<RepositorySession>(row.GetString(1))),
            new Dictionary<string, object?> { ["cutoff"] = cutoff.ToUniversalTime().ToString("O") }, cancellationToken: token);
        return rows.ToArray();
    }
}
