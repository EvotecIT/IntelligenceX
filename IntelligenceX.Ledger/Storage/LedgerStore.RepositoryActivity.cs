using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    /// <summary>Creates the repository index and imports observations retained before this index existed.</summary>
    internal static async Task InitializeRepositoryActivityAsync(SQLiteAsyncSession db, CancellationToken token = default) {
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS repository_sessions (
              item TEXT PRIMARY KEY, repo TEXT NOT NULL, activity TEXT,
              open INTEGER NOT NULL, archived INTEGER NOT NULL, json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS repository_sessions_live ON repository_sessions(repo,open,archived);
            CREATE INDEX IF NOT EXISTS repository_sessions_activity ON repository_sessions(repo,activity);
            """, cancellationToken: token);
        var items = await db.QueryAsListAsync("""
            SELECT json FROM items WHERE json_extract(json,'$.kind')='session'
              AND json_type(json,'$.session.repository')='object'
            """, row => Read<LedgerItem>(row.GetString(0)), cancellationToken: token);
        foreach (var item in items) await UpsertRepositorySessionAsync(db, item, token);
    }

    /// <summary>Refreshes the compact index inside the current ingest or health-activity transaction.</summary>
    internal static async Task UpsertRepositorySessionAsync(SQLiteAsyncSession db, LedgerItem item, CancellationToken token = default) {
        var args = new Dictionary<string, object?> { ["item"] = item.Id };
        if (item.Kind != "session" || item.Session?.Repository is not { } repository || string.IsNullOrWhiteSpace(repository.Repo)) {
            await db.ExecuteNonQueryAsync("DELETE FROM repository_sessions WHERE item=@item", args, token);
            return;
        }

        var fact = item.Session;
        var files = repository.Files?.Distinct(StringComparer.Ordinal).Take(20).ToArray();
        var session = new RepositorySession(fact.SourceSessionId?.ToString() ?? item.Id, item.MachineId, item.Provider,
            repository.Branch, repository.Worktree, repository.Title, repository.PullRequests,
            fact.LastActivityAtUtc?.ToUniversalTime(), files, repository.FilesCapped || repository.Files?.Length > 20,
            repository.BaseRef, repository.GitState, fact.TurnOpen == true && fact.Archived != true, repository.PullRequestsKnown);
        args["repo"] = RepositoryOverlapEvaluator.NormalizeRepo(repository.Repo);
        args["activity"] = session.LastActivityAtUtc?.ToString("O");
        args["open"] = fact.TurnOpen == true ? 1 : 0;
        args["archived"] = fact.Archived == true ? 1 : 0;
        args["json"] = Json(session);
        await db.ExecuteNonQueryAsync("""
            INSERT INTO repository_sessions(item,repo,activity,open,archived,json)
            VALUES(@item,@repo,@activity,@open,@archived,@json)
            ON CONFLICT(item) DO UPDATE SET repo=excluded.repo,activity=excluded.activity,
              open=excluded.open,archived=excluded.archived,json=excluded.json
            """, args, token);
    }

    /// <summary>Returns every live or last-24-hours session and related live leases without the work-projection gate.</summary>
    public async Task<RepositoryActivity> RepositoryActivityAsync(string repo, DateTimeOffset now, CancellationToken token = default) {
        repo = RepositoryOverlapEvaluator.NormalizeRepo(repo);
        await using var db = await _database.OpenSessionAsync(path, token);
        var args = new Dictionary<string, object?> {
            ["repo"] = repo, ["cutoff"] = now.AddHours(-24).ToUniversalTime().ToString("O"),
            ["now"] = now.ToUniversalTime().ToString("O"), ["build"] = "build:" + repo,
            ["repoKey"] = "repo:" + repo, ["branchPrefix"] = "branch:" + repo + ":"
        };
        // UNION lets SQLite use the live and activity indexes independently and deduplicates their intersection.
        var sessions = await db.QueryAsListAsync("""
            SELECT json FROM repository_sessions WHERE item IN (
              SELECT item FROM repository_sessions WHERE repo=@repo AND open=1 AND archived=0
              UNION SELECT item FROM repository_sessions WHERE repo=@repo AND activity>=@cutoff)
            ORDER BY item
            """, row => Read<RepositorySession>(row.GetString(0)), args, cancellationToken: token);
        var leases = await db.QueryAsListAsync("""
            SELECT l.json FROM leases l WHERE l.expires>@now AND (
              l.key=@build COLLATE NOCASE OR l.key=@repoKey COLLATE NOCASE
              OR substr(l.key,1,length(@branchPrefix))=@branchPrefix COLLATE NOCASE
              OR EXISTS (SELECT 1 FROM repository_sessions r WHERE r.repo=@repo
                AND (json_extract(l.json,'$.holder')=r.item
                  OR json_extract(l.json,'$.holder')=json_extract(r.json,'$.agentId'))))
            ORDER BY l.key
            """, row => Read<Lease>(row.GetString(0)), args, cancellationToken: token);
        var coverage = new List<string>();
        if (sessions.Count == 0) coverage.Add("repository-sessions-unobserved");
        var missing = await db.ExecuteScalarAsync("""
            SELECT EXISTS(SELECT 1 FROM session_status s JOIN items i ON i.id=s.item
              LEFT JOIN repository_sessions r ON r.item=s.item WHERE r.item IS NULL
              AND ((s.open=1 AND s.archived=0)
                OR julianday(json_extract(i.json,'$.session.lastActivityAtUtc'))>=julianday(@cutoff)))
            """, args, token);
        if (Convert.ToInt32(missing) != 0) coverage.Add("repository-metadata-missing");
        if (sessions.Any(session => session.Files is null)) coverage.Add("changed-files-unknown");
        if (sessions.Any(session => session.GitState != "observed")) coverage.Add("git-unavailable-or-partial");
        if (sessions.Any(session => session.Branch is null)) coverage.Add("branch-unknown");
        if (sessions.Any(session => session.FilesCapped)) coverage.Add("changed-files-capped");
        if (sessions.Any(session => session.PullRequestsKnown != true)) coverage.Add("pull-requests-unobserved");
        return new(repo, sessions.ToArray(), leases.ToArray(), coverage.ToArray());
    }

    /// <summary>Evaluates overlap using the same complete repository activity query as repo_activity.</summary>
    public async Task<RepositoryOverlap> RepositoryOverlapAsync(string repo, string? branch, string[]? files, string? text,
        DateTimeOffset now, CancellationToken token = default) =>
        RepositoryOverlapEvaluator.Evaluate(await RepositoryActivityAsync(repo, now, token), branch, files, text);

    /// <summary>Machines with persisted repository observations, for expected-reporter coverage enrichment.</summary>
    public async Task<string[]> ReportedRepositoryMachinesAsync(CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var machines = await db.QueryAsListAsync("""
            SELECT DISTINCT json_extract(json,'$.machine') FROM repository_sessions
            ORDER BY json_extract(json,'$.machine')
            """, row => row.GetString(0), cancellationToken: token);
        return machines.ToArray();
    }
}
