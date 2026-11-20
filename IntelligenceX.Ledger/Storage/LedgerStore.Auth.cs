using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    /// <summary>How long changes are kept, and the most kept at once, so a flapping check can't grow the table.</summary>
    internal static readonly TimeSpan AuthChangeRetention = TimeSpan.FromDays(90);
    internal const int AuthChangeLimit = 5000;
    private static readonly string[] AgentProviders = ["codex", "claude"];

    private static async Task InitializeAuthAsync(SQLiteAsyncSession db, CancellationToken token) =>
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS auth_state(machine TEXT NOT NULL,provider TEXT NOT NULL,checked TEXT NOT NULL,json TEXT NOT NULL,
              raw TEXT NULL,PRIMARY KEY(machine,provider));
            CREATE TABLE IF NOT EXISTS auth_changes(id INTEGER PRIMARY KEY AUTOINCREMENT,machine TEXT NOT NULL,provider TEXT NOT NULL,
              at TEXT NOT NULL,field TEXT NOT NULL,before TEXT NULL,after TEXT NULL);
            CREATE INDEX IF NOT EXISTS auth_changes_machine ON auth_changes(machine,id);
            """, cancellationToken: token);

    /// <summary>
    /// Stores a machine's latest facts and records each difference from the previous ones as a change. Codex and Claude
    /// take their state from the agent service's sign-in item on that machine when there is one, since the agent runs
    /// the real sign-in check; the reporter only knows whether credentials and binaries are there. The first facts from
    /// a machine are its baseline and record no change.
    /// </summary>
    private static async Task RecordAuthAsync(SQLiteAsyncSession db, string machine, AuthFact[] facts, DateTimeOffset now, CancellationToken token) {
        var signIn = await SignInAsync(db, machine, token);
        foreach (var fact in facts) {
            var merged = Merge(fact, signIn.FirstOrDefault(s => s.Provider == fact.Provider));
            var prior = await db.ExecuteScalarAsync("SELECT json FROM auth_state WHERE machine=@machine AND provider=@provider",
                new Dictionary<string, object?> { ["machine"] = machine, ["provider"] = fact.Provider }, token) is string json ? Read<AuthFact>(json) : null;
            // A delayed report with an older reading would restore an obsolete state and record a false reversal.
            // The same reading sent again still counts, since the agent's sign-in state may have changed meanwhile.
            if (prior is not null && fact.CheckedAtUtc < prior.CheckedAtUtc) continue;
            if (prior is not null)
                foreach (var change in AuthDiff(machine, prior, merged, now))
                    await db.ExecuteNonQueryAsync("INSERT INTO auth_changes(machine,provider,at,field,before,after) VALUES(@machine,@provider,@at,@field,@before,@after)",
                        new Dictionary<string, object?> { ["machine"] = machine, ["provider"] = change.Provider, ["at"] = change.AtUtc.ToString("O"),
                            ["field"] = change.Field, ["before"] = change.Before, ["after"] = change.After }, token);
            await db.ExecuteNonQueryAsync("""
                INSERT INTO auth_state(machine,provider,checked,json,raw) VALUES(@machine,@provider,@checked,@json,@raw)
                ON CONFLICT(machine,provider) DO UPDATE SET checked=excluded.checked,json=excluded.json,raw=excluded.raw
                """, new Dictionary<string, object?> { ["machine"] = machine, ["provider"] = fact.Provider,
                    ["checked"] = merged.CheckedAtUtc.ToString("O"), ["json"] = Json(merged), ["raw"] = Json(fact) }, token);
        }
        await db.ExecuteNonQueryAsync("""
            DELETE FROM auth_changes WHERE at<@cutoff;
            DELETE FROM auth_changes WHERE id<=(SELECT id FROM auth_changes ORDER BY id DESC LIMIT 1 OFFSET @limit);
            """, new Dictionary<string, object?> { ["cutoff"] = (now - AuthChangeRetention).ToString("O"), ["limit"] = AuthChangeLimit }, token);
    }

    /// <summary>
    /// Folds a newly published agent sign-in state into the machine's stored facts when it arrives, so each change is
    /// recorded at the time the agent saw it, including a sign-out and sign-in that both happen between two reporter
    /// readings. A machine whose reporter hasn't sent facts yet has nothing to fold into; its first reading does it.
    /// The event is folded into the reporter's own last reading, not into a state an earlier agent event produced, so
    /// only a sign-out the reporter itself saw can outweigh it.
    /// </summary>
    private static async Task RecordSignInAsync(SQLiteAsyncSession db, string machine, SignInFact signIn, CancellationToken token) {
        var row = (await db.QueryAsListAsync("SELECT json,raw FROM auth_state WHERE machine=@machine AND provider=@provider",
            r => (Json: r.GetString(0), Raw: r.IsDBNull(1) ? null : r.GetString(1)),
            new Dictionary<string, object?> { ["machine"] = machine, ["provider"] = signIn.Provider }, cancellationToken: token)).FirstOrDefault();
        if (row.Json is null) return;
        var prior = Read<AuthFact>(row.Json);
        var merged = Merge(row.Raw is { } raw ? Read<AuthFact>(raw) : prior, signIn);
        foreach (var change in AuthDiff(machine, prior, merged, signIn.AtUtc))
            await db.ExecuteNonQueryAsync("INSERT INTO auth_changes(machine,provider,at,field,before,after) VALUES(@machine,@provider,@at,@field,@before,@after)",
                new Dictionary<string, object?> { ["machine"] = machine, ["provider"] = change.Provider, ["at"] = change.AtUtc.ToString("O"),
                    ["field"] = change.Field, ["before"] = change.Before, ["after"] = change.After }, token);
        await db.ExecuteNonQueryAsync("UPDATE auth_state SET json=@json WHERE machine=@machine AND provider=@provider",
            new Dictionary<string, object?> { ["machine"] = machine, ["provider"] = signIn.Provider, ["json"] = Json(merged) }, token);
    }

    private static async Task<SignInFact[]> SignInAsync(SQLiteAsyncSession db, string machine, CancellationToken token) {
        var found = new List<SignInFact>();
        foreach (var provider in AgentProviders)
            if (await db.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
                    new Dictionary<string, object?> { ["id"] = machine + ":sign-in:" + provider }, token) is string json &&
                Read<LedgerItem>(json).SignIn is { } fact)
                found.Add(fact);
        return found.ToArray();
    }

    /// <summary>The reporter's facts with the agent service's sign-in state folded in.</summary>
    internal static AuthFact Merge(AuthFact fact, SignInFact? signIn) {
        if (signIn is null) return fact;
        // A check that timed out or failed reads "unknown" without requiring a sign-in.
        var agent = signIn.Reason == "unknown" ? "unknown" : signIn.Required ? signIn.Reason : "ready";
        var facts = new Dictionary<string, string>(fact.Facts ?? [], StringComparer.Ordinal) { ["agent-sign-in"] = agent };
        var state = agent switch {
            "ready" => "signed-in",
            "signed-out" or "expired" => "signed-out",
            "usage-limit" => "signed-in", // Signed in, but out of usage; the agent-sign-in fact says so.
            _ => "unknown"
        };
        // The agent publishes only when its state changes, so its item can be older than the reporter's reading. When
        // the reporter sees the credentials gone (signed-out), an earlier "ready" can't stand; a later one wins.
        if (fact.State == "not-installed" || fact.State == "signed-out" && signIn.AtUtc <= fact.CheckedAtUtc) state = fact.State;
        return fact with { State = state, Facts = facts };
    }

    /// <summary>What changed between two readings of one provider: its state and every fact added, removed or changed.</summary>
    internal static AuthChange[] AuthDiff(string machine, AuthFact before, AuthFact after, DateTimeOffset now) {
        var changes = new List<AuthChange>();
        if (before.State != after.State) changes.Add(new(machine, after.Provider, now, "state", before.State, after.State));
        var old = before.Facts ?? [];
        var current = after.Facts ?? [];
        foreach (var key in old.Keys.Union(current.Keys).Order(StringComparer.Ordinal)) {
            var was = old.GetValueOrDefault(key);
            var next = current.GetValueOrDefault(key);
            if (!string.Equals(was, next, StringComparison.Ordinal)) changes.Add(new(machine, after.Provider, now, key, was, next));
        }
        return changes.ToArray();
    }

    /// <summary>Every enrolled machine's latest facts, the agent service's sign-in states and the latest changes.</summary>
    public async Task<AuthStateView> AuthStateAsync(DateTimeOffset now, Dictionary<string, string[]> expected, TimeSpan reporterTimeout,
        string? machine, int changeLimit, CancellationToken token = default) {
        var heartbeats = await HeartbeatsAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        var machines = expected.Where(pair => machine is null || pair.Key == machine).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var (rows, changes, health) = await db.RunInTransactionAsync(SQLiteTransactionMode.Deferred, async (tx, ct) => {
            var states = await tx.QueryAsListAsync("SELECT machine,json FROM auth_state", r => (Machine: r.GetString(0), Fact: Read<AuthFact>(r.GetString(1))),
                cancellationToken: ct);
            var recent = await tx.QueryAsListAsync("""
                SELECT machine,provider,at,field,before,after FROM auth_changes WHERE (@machine IS NULL OR machine=@machine) AND at>=@cutoff ORDER BY id DESC LIMIT @limit
                """, r => new AuthChange(r.GetString(0), r.GetString(1), DateTimeOffset.Parse(r.GetString(2)), r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)),
                // Expired changes stay out even before the retention pass removes them.
                new Dictionary<string, object?> { ["machine"] = machine, ["limit"] = changeLimit, ["cutoff"] = (now - AuthChangeRetention).ToString("O") },
                cancellationToken: ct);
            var reporters = await tx.QueryAsListAsync("SELECT machine,json FROM reporter_health",
                r => (Machine: r.GetString(0), Health: Read<ReporterHealth>(r.GetString(1))), cancellationToken: ct);
            var signIn = new Dictionary<string, SignInFact[]>(StringComparer.Ordinal);
            foreach (var pair in machines) signIn[pair.Key] = await SignInAsync(tx, pair.Key, ct);
            return ((states, signIn), recent, reporters);
        }, token);
        var coverage = new List<string>();
        var result = machines.Select(pair => {
            var times = pair.Value.Select(r => heartbeats.TryGetValue(r, out var at) ? (DateTimeOffset?)at : null).ToArray();
            var reporter = times.All(t => t is null) ? "not-enrolled" : times.Any(t => t is null || now - t > reporterTimeout) ? "reporter-silent" : "healthy";
            var facts = rows.states.Where(row => row.Machine == pair.Key).Select(row => row.Fact).OrderBy(f => f.Provider, StringComparer.Ordinal).ToArray();
            if (facts.Length == 0) coverage.Add("no-auth-facts:" + pair.Key);
            var version = health.Where(h => h.Machine == pair.Key).Select(h => h.Health).OrderByDescending(h => h.ObservedAtUtc)
                .Select(h => h.SourceVersion).FirstOrDefault();
            return new AuthMachine(pair.Key, reporter, times.Where(t => t is not null).DefaultIfEmpty().Max(), version, facts, rows.signIn[pair.Key]);
        }).ToArray();
        if (changes.Count == changeLimit) coverage.Add("changes-limited:" + changeLimit);
        return new(now, result, changes.ToArray(), coverage.ToArray());
    }
}
