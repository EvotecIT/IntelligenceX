using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static async Task InitializeSessionLookupAsync(SQLiteAsyncSession db, CancellationToken token) {
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS session_lookup(item TEXT PRIMARY KEY,native TEXT,title TEXT,machine TEXT NOT NULL,provider TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS session_lookup_native ON session_lookup(native);
            CREATE INDEX IF NOT EXISTS items_parent ON items(json_extract(json,'$.parentItemId'));
            INSERT OR IGNORE INTO session_lookup(item,native,title,machine,provider)
              SELECT i.id,json_extract(i.json,'$.session.sourceSessionId'),
                COALESCE(json_extract(i.json,'$.session.title'),json_extract(i.json,'$.session.repository.title')),
                s.machine,s.provider FROM session_status s JOIN items i ON i.id=s.item;
            """, cancellationToken: token);
    }

    private static Task<int> UpsertSessionLookupAsync(SQLiteAsyncSession db, LedgerItem item, SessionFact session, CancellationToken token) =>
        db.ExecuteNonQueryAsync("""
            INSERT INTO session_lookup(item,native,title,machine,provider) VALUES(@item,@native,@title,@machine,@provider)
            ON CONFLICT(item) DO UPDATE SET native=excluded.native,title=excluded.title
            """, new Dictionary<string, object?> { ["item"] = item.Id, ["native"] = session.SourceSessionId?.ToString(),
                ["title"] = session.Title ?? session.Repository?.Title, ["machine"] = item.MachineId, ["provider"] = item.Provider }, token);

    /// <summary>Finds one native identity or unique explicit title without loading unrelated item evidence.</summary>
    public async Task<SessionStatus> SessionStatusAsync(string query, DateTimeOffset now, CancellationToken token = default,
        HashSet<string>? silentMachines = null) {
        if (!Label(query, 240)) throw new ArgumentException("Invalid session identity or title fragment.");
        var policy = await ReadPolicyAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        var args = new Dictionary<string, object?> { ["query"] = Guid.TryParse(query, out var id) ? id.ToString() : query };
        var matches = await db.QueryAsListAsync("SELECT item,native,machine,provider,title FROM session_lookup WHERE native=@query OR item=@query LIMIT 3",
            row => (Item: row.GetString(0), Match: new SessionMatch(row.IsDBNull(1) ? row.GetString(0) : row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4))),
            args, cancellationToken: token);
        if (matches.Count == 0 && !Guid.TryParse(query, out _)) matches = await db.QueryAsListAsync("""
            SELECT item,native,machine,provider,title FROM session_lookup WHERE instr(lower(title),lower(@query))>0 ORDER BY item LIMIT 3
            """, row => (row.GetString(0), new SessionMatch(row.IsDBNull(1) ? row.GetString(0) : row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4))), args, cancellationToken: token);
        if (matches.Count == 0) return new() { State = "not-found" };
        if (matches.Count > 1) return Compact(new SessionStatus { State = "ambiguous", Matches = matches.Select(row => row.Match).ToArray() },
            value => value with { Matches = value.Matches![..^1], Truncated = true }, value => value.Matches!.Length);
        args["item"] = matches[0].Item;
        var json = (string)(await db.ExecuteScalarAsync("SELECT json FROM items WHERE id=@item", args, token))!;
        var item = ApplyGlobal(Read<LedgerItem>(json), await ReadGlobalAsync(db, token), now);
        var fact = item.Session!;
        var verdict = rules.Evaluate(item, now, silentMachines?.Contains(item.MachineId) == true);
        args["native"] = fact.SourceSessionId?.ToString();
        args["machine"] = item.MachineId;
        args["actor"] = item.Provider + ":" + fact.SourceSessionId;
        args["ownerActor"] = args["actor"] + ":owner-driven";
        args["now"] = now.ToString("O");
        var leases = await db.QueryAsListAsync("""
            SELECT json FROM reservations WHERE state='active' AND expires>@now AND machine=@machine
              AND holder IN (@item,@native,@actor,@ownerActor) ORDER BY key,ordinal
            """, row => Read<Lease>(row.GetString(0)), args, cancellationToken: token);
        var question = await HasOpenQuestionAsync(db, item, now, true, token);
        var takeovers = await db.QueryAsListAsync("""
            SELECT json FROM lease_history WHERE json_extract(json,'$.previousHolder') IN (@item,@native,@actor,@ownerActor)
            ORDER BY json_extract(json,'$.takenOverAtUtc') DESC LIMIT 2
            """, row => Read<Lease>(row.GetString(0)), args, cancellationToken: token);
        var result = new SessionStatus { State = "found", Id = fact.SourceSessionId?.ToString() ?? item.Id,
            Provider = item.Provider, Machine = item.MachineId, Title = fact.Title ?? fact.Repository?.Title,
            Verdict = verdict.Value, Reason = verdict.ReasonCode, LastKnown = verdict.LastKnown, TurnOpen = fact.TurnOpen,
            TurnOpenSinceUtc = fact.TurnOpen == true ? fact.TurnStartedAtUtc : null, LastActivityAtUtc = fact.LastActivityAtUtc,
            QuestionWaiting = question, UsageLimited = fact.UsageLimit, Archived = fact.Archived,
            Repo = fact.Repository?.Repo, Branch = fact.Repository?.Branch, PullRequests = fact.Repository?.PullRequests ?? [],
            Leases = leases.ToArray(), TotalLeases = leases.Count, Origin = fact.Origin, OriginEvidence = fact.OriginEvidence,
            FreshAdvice = FreshAdvice(fact, policy, now), Takeovers = takeovers.Count == 0 ? null :
                takeovers.Select(lease => new LeaseTakeover(lease.Key, lease.Holder, lease.Machine, lease.TakenOverAtUtc!.Value)).ToArray() };
        return Compact(result, value => value.Leases.Length > 0 ? value with { Leases = value.Leases[..^1], Truncated = true } :
            value.PullRequests.Length > 0 ? value with { PullRequests = value.PullRequests[..^1], Truncated = true } :
            value with { Takeovers = value.Takeovers![..^1], Truncated = true },
            value => value.Leases.Length + value.PullRequests.Length + (value.Takeovers?.Length ?? 0));
    }

    private async Task<bool> HasOpenQuestionAsync(SQLiteAsyncSession db, LedgerItem item, DateTimeOffset now, bool humanOnly, CancellationToken token) {
        bool Matches(LedgerItem value) {
            var verdict = rules.Evaluate(value, now);
            if (value.Supersession is not null) return false;
            return value.Session?.Question == true || verdict.Matches.Any(match =>
                match.ReasonCode == "question" && (!humanOnly || match.Verdict == "needs-you"));
        }
        if (Matches(item)) return true;
        var children = await db.QueryAsListAsync("SELECT json FROM items WHERE json_extract(json,'$.parentItemId')=@item",
            row => Read<LedgerItem>(row.GetString(0)), new Dictionary<string, object?> { ["item"] = item.Id }, cancellationToken: token);
        return children.Any(child => Matches(ApplyParent(child, item)));
    }
}
