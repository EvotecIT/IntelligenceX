using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Single-writer durable coordination journal and projection, committed before acknowledgement.</summary>
public sealed class CoordinationStore(string path, CoordinationPolicy initialPolicy,
    CoordinationPrincipal[]? configuredPrincipals = null) : IDisposable {
    private readonly SQLite _database = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private FileStream? _writerLease;

    /// <summary>Called with the requests a command moved to the archive table, after it commits and before the next
    /// command starts.</summary>
    internal Action<IReadOnlyList<CoordinationArchiveEntry>>? Archived { get; set; }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public async Task InitializeAsync(CancellationToken token = default) {
        CoordinationValidation.Policy(initialPolicy);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        _writerLease = new FileStream(path + ".writer.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var db = await _database.OpenSessionAsync(path, token);
        await db.ExecuteNonQueryAsync("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS coordination_state(id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS coordination_changes(sequence INTEGER PRIMARY KEY, request TEXT NOT NULL,
              stage TEXT NOT NULL, principal TEXT NOT NULL, recorded TEXT NOT NULL, json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS coordination_changes_request ON coordination_changes(request,sequence);
            CREATE TABLE IF NOT EXISTS coordination_archive(request TEXT PRIMARY KEY, finished TEXT NOT NULL, json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS coordination_archive_finished ON coordination_archive(finished);
            CREATE INDEX IF NOT EXISTS coordination_archive_binding ON coordination_archive(
              json_extract(json,'$.binding.provider'), replace(lower(json_extract(json,'$.binding.sessionId')),'-',''),
              json_extract(json,'$.binding.machineId'));
            CREATE TABLE IF NOT EXISTS coordination_ask_titles(request TEXT PRIMARY KEY,title TEXT NOT NULL);
            """, cancellationToken: token);
        await db.ExecuteNonQueryAsync("INSERT OR IGNORE INTO coordination_state(id,json) VALUES(1,@json)",
            new Dictionary<string, object?> { ["json"] = JsonSerializer.Serialize(new CoordinationState {
                Policy = initialPolicy, ResultDeliveryAttempts = []
            }, Json) }, token);
        await ExecuteAsync((engine, _) => { engine.ValidateRequesterPrincipals(); return true; }, DateTimeOffset.UtcNow, token);
    }

    public Task<T> ExecuteAsync<T>(Func<CoordinationEngine, CoordinationState, T> command, DateTimeOffset now,
        CancellationToken token = default) => ExecuteAsync(null, (engine, state, _) => command(engine, state), now, token);

    /// <summary>Runs a command that creates request <paramref name="requestId"/>, with that id's archive entry read in the
    /// same transaction, so an id archived meanwhile can't be taken again.</summary>
    public Task<T> ExecuteAsync<T>(string? requestId, Func<CoordinationEngine, CoordinationState, CoordinationArchiveEntry?, T> command,
        DateTimeOffset now, CancellationToken token = default) =>
        ExecuteAsync(requestId, command, now, token, null);

    /// <summary>The display title an ask was submitted with, if any.</summary>
    internal async Task<string?> AskTitleAsync(string requestId, CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        return await db.ExecuteScalarAsync("SELECT title FROM coordination_ask_titles WHERE request=@request",
            new Dictionary<string, object?> { ["request"] = requestId }, cancellationToken: token) as string;
    }

    /// <summary>Records a request a machine finished in its local store, with its title. <paramref name="write"/> stores its
    /// result files once the import is accepted and before it commits, so a refused import writes nothing.</summary>
    internal Task<CoordinationAcknowledgement> ImportAsync(CoordinationArchiveEntry entry, string? title, CoordinationPrincipal caller,
        Action write, DateTimeOffset now, CancellationToken token) {
        var trimmed = title?.Trim();
        CoordinationValidation.Require(title is null || trimmed is { Length: > 0 and <= 120 } && !trimmed.Any(char.IsControl), "invalid-display-title");
        return ExecuteAsync(entry.RequestId, (engine, _, archived) => {
            var acknowledgement = engine.Import(entry, caller, archived);
            if (archived is null) write();
            return acknowledgement;
        }, now, token, trimmed);
    }

    internal Task<CoordinationWork> AskAsync(CoordinationAsk ask, CoordinationPrincipal caller, DateTimeOffset now, CancellationToken token,
        string? previousResult = null) {
        var title = ask?.DisplayTitle?.Trim();
        CoordinationValidation.Require(ask?.DisplayTitle is null || title is { Length: > 0 and <= 120 } &&
            !title.Any(char.IsControl), "invalid-display-title");
        return ExecuteAsync(ask?.Id, (engine, _, archived) => engine.Ask(ask!, caller, archived, previousResult), now, token, title);
    }

    private async Task<T> ExecuteAsync<T>(string? requestId, Func<CoordinationEngine, CoordinationState, CoordinationArchiveEntry?, T> command,
        DateTimeOffset now, CancellationToken token, string? askTitle) {
        if (_writerLease is null) throw new InvalidOperationException("Coordination store has not initialized.");
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            List<CoordinationArchiveEntry>? archived = null;
            var committed = await db.RunInTransactionAsync(async (tx, ct) => {
                var value = (string)(await tx.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1", cancellationToken: ct))!;
                var state = JsonSerializer.Deserialize<CoordinationState>(value, Json)!;
                if (state.ResultDeliveryAttempts is null) {
                    // Legacy projections retain only the latest attempt. The journal retains every send identity.
                    var sends = await tx.QueryAsListAsync("SELECT json FROM coordination_changes WHERE stage='result-sending' ORDER BY sequence",
                        r => JsonSerializer.Deserialize<CoordinationChange>(r.GetString(0), Json)!, cancellationToken: ct);
                    var sendsByResult = sends.ToLookup(c => (c.RequestId, c.Generation));
                    state.ResultDeliveryAttempts = [];
                    foreach (var outbox in state.ResultOutbox.Values) {
                        var ids = sendsByResult[(outbox.RequestId, outbox.Generation)]
                            .Select(c => c.EvidenceRef).OfType<string>().ToHashSet(StringComparer.Ordinal);
                        if (outbox.AttemptId is { } latest) ids.Add(latest);
                        state.ResultDeliveryAttempts[outbox.RequestId] = new(outbox.Generation, ids);
                    }
                }
                CoordinationArchiveEntry? archivedEntry = null;
                if (requestId is not null && await tx.ExecuteScalarAsync("SELECT json FROM coordination_archive WHERE request=@request",
                    new Dictionary<string, object?> { ["request"] = requestId }, cancellationToken: ct) is string archivedJson)
                    archivedEntry = JsonSerializer.Deserialize<CoordinationArchiveEntry>(archivedJson, Json);
                var engine = new CoordinationEngine(state, now, configuredPrincipals);
                var alreadyRequested = requestId is not null && (archivedEntry is not null || state.Work.ContainsKey(requestId));
                var result = command(engine, state, archivedEntry);
                if (askTitle is not null) {
                    var args = new Dictionary<string, object?> { ["request"] = requestId, ["title"] = askTitle };
                    if (alreadyRequested) {
                        var existing = await tx.ExecuteScalarAsync("SELECT title FROM coordination_ask_titles WHERE request=@request",
                            new Dictionary<string, object?> { ["request"] = requestId }, ct);
                        CoordinationValidation.Require(existing as string == askTitle, "request-id-conflict");
                    } else await tx.ExecuteNonQueryAsync("INSERT INTO coordination_ask_titles(request,title) VALUES(@request,@title)", args, ct);
                }
                var updated = JsonSerializer.Serialize(state, Json);
                if (updated != value) await tx.ExecuteNonQueryAsync("UPDATE coordination_state SET json=@json WHERE id=1",
                    new Dictionary<string, object?> { ["json"] = updated }, ct);
                foreach (var entry in engine.Archived) await tx.ExecuteNonQueryAsync(
                    "INSERT OR REPLACE INTO coordination_archive(request,finished,json) VALUES(@request,@finished,@json)",
                    new Dictionary<string, object?> { ["request"] = entry.RequestId, ["finished"] = entry.FinishedAtUtc.UtcDateTime.ToString("O"),
                        ["json"] = JsonSerializer.Serialize(entry, Json) }, ct);
                foreach (var change in engine.Changes) await tx.ExecuteNonQueryAsync("""
                    INSERT INTO coordination_changes(sequence,request,stage,principal,recorded,json)
                    VALUES(@sequence,@request,@stage,@principal,@recorded,@json)
                    """, new Dictionary<string, object?> {
                        ["sequence"] = change.Sequence, ["request"] = change.RequestId, ["stage"] = change.Stage,
                        ["principal"] = change.Principal, ["recorded"] = change.AtUtc.ToString("O"),
                        ["json"] = JsonSerializer.Serialize(change, Json)
                    }, ct);
                archived = engine.Archived;
                return result;
            }, token);
            if (archived is { Count: > 0 }) Archived?.Invoke(archived);
            return committed;
        } finally { _gate.Release(); }
    }

    /// <summary>Reads the committed projection without waiting for a command in progress.</summary>
    public async Task<T> ReadAsync<T>(Func<CoordinationState, T> read, CancellationToken token = default) {
        if (_writerLease is null) throw new InvalidOperationException("Coordination store has not initialized.");
        await using var db = await _database.OpenSessionAsync(path, token);
        return read(await ReadStateAsync(db, token));
    }

    private static async Task<CoordinationState> ReadStateAsync(SQLiteAsyncSession db, CancellationToken token) {
        var value = (string)(await db.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1", cancellationToken: token))!;
        return JsonSerializer.Deserialize<CoordinationState>(value, Json)!;
    }

    /// <summary>One archived request, or null.</summary>
    public async Task<CoordinationArchiveEntry?> ArchivedAsync(string requestId, CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var value = await db.ExecuteScalarAsync("SELECT json FROM coordination_archive WHERE request=@request",
            new Dictionary<string, object?> { ["request"] = requestId }, cancellationToken: token);
        return value is string json ? JsonSerializer.Deserialize<CoordinationArchiveEntry>(json, Json) : null;
    }

    /// <summary>The most entries <see cref="ArchivedSinceAsync"/> returns at once.</summary>
    internal const int ArchivePageSize = 10000;

    /// <summary>Archived requests that finished at or after a time, oldest first, at most <see cref="ArchivePageSize"/>.</summary>
    public async Task<CoordinationArchiveEntry[]> ArchivedSinceAsync(DateTimeOffset since, CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var rows = await db.QueryAsListAsync("SELECT json FROM coordination_archive WHERE finished>=@since ORDER BY finished,request LIMIT 10000",
            r => JsonSerializer.Deserialize<CoordinationArchiveEntry>(r.GetString(0), Json)!,
            new Dictionary<string, object?> { ["since"] = since.UtcDateTime.ToString("O") }, cancellationToken: token);
        return rows.ToArray();
    }

    internal sealed record WorkAsk(SessionAskStep Step, string HelperMachine, string RequesterMachine, string? Title,
        bool ResultPublished, DateTimeOffset? QuestionAtUtc = null, DateTimeOffset? ResumedAtUtc = null) {
        internal bool Finished => ResultPublished || Step.State is "succeeded" or "failed" or "cancelled" ||
            Step.Stages.ContainsKey("terminal");
    }

    /// <summary>Reads live and archived bindings for the requested native identities. The archive index also finds
    /// helpers whose first lineage edge came from the native reporter before the binding was recorded.</summary>
    internal async Task<WorkAsk[]> WorkAsksAsync(HashSet<(string Machine, string Provider, Guid Session)> active,
        HashSet<(string Machine, string Provider, Guid Session)> candidates, CancellationToken token) {
        if (active.Count == 0 && candidates.Count == 0) return [];
        await using var db = await _database.OpenSessionAsync(path, token);
        return await db.RunInTransactionAsync(SQLiteTransactionMode.Deferred, async (tx, ct) => {
            var state = await ReadStateAsync(tx, ct);
            var live = state.Bindings.Values.Where(binding => Guid.TryParse(binding.SessionId, out var session) &&
                (active.Contains((binding.MachineId, binding.Provider, session)) || candidates.Contains((binding.MachineId, binding.Provider, session))) &&
                state.Work.TryGetValue(binding.RequestId, out var work) && !work.Request.ReuseSession && work.Request.ReturnTo is not null).ToArray();
            var display = await tx.QueryAsListAsync("""
                SELECT requested.value,titles.title,(
                    SELECT recorded FROM coordination_changes
                    WHERE request=requested.value AND stage='question-resumed' ORDER BY sequence DESC LIMIT 1)
                FROM json_each(@requests) requested
                LEFT JOIN coordination_ask_titles titles ON titles.request=requested.value
                """, row => (Request: row.GetString(0), Title: row.IsDBNull(1) ? null : row.GetString(1),
                    Resumed: row.IsDBNull(2) ? (DateTimeOffset?)null :
                        DateTimeOffset.Parse(row.GetString(2), System.Globalization.CultureInfo.InvariantCulture)),
                new Dictionary<string, object?> { ["requests"] = JsonSerializer.Serialize(live.Select(binding => binding.RequestId)) },
                cancellationToken: ct);
            var byRequest = display.ToDictionary(row => row.Request, StringComparer.Ordinal);
            var questions = state.Questions.Values.Where(question => question.Kind == "question" && question.State != "resumed")
                .ToLookup(question => (question.RequestId, question.Generation));
            var archived = candidates.Count == 0 ? [] : await ReadArchivedWorkAsksAsync(tx, candidates, ct);
            return live.Select(binding => {
                var work = state.Work[binding.RequestId];
                var requester = work.Request.ReturnTo!;
                var details = byRequest[work.Request.Id];
                return new WorkAsk(new(work.Request.Id, requester.Provider, requester.SessionId, binding.Provider, binding.SessionId,
                    work.State, work.RequestedAtUtc, work.Stages ?? []), binding.MachineId, requester.MachineId,
                    details.Title, state.ResultOutbox.ContainsKey(work.Request.Id),
                    questions[(binding.RequestId, binding.Generation)].MaxBy(question => question.Number)?.AskedAtUtc, details.Resumed);
            }).Concat(archived).ToArray();
        }, token);
    }

    private static Task<IReadOnlyList<WorkAsk>> ReadArchivedWorkAsksAsync(SQLiteAsyncSession db,
        HashSet<(string Machine, string Provider, Guid Session)> sessions, CancellationToken token) =>
        db.QueryAsListAsync("""
            SELECT archive.request,
                json_extract(archive.json,'$.work.request.returnTo.provider'),
                json_extract(archive.json,'$.work.request.returnTo.sessionId'),
                json_extract(archive.json,'$.binding.provider'), json_extract(archive.json,'$.binding.sessionId'),
                json_extract(archive.json,'$.work.state'), json_extract(archive.json,'$.work.requestedAtUtc'),
                json_extract(archive.json,'$.work.stages'), json_extract(archive.json,'$.binding.machineId'),
                json_extract(archive.json,'$.work.request.returnTo.machineId'), titles.title,
                json_type(archive.json,'$.result')='object'
            FROM json_each(@sessions) session
            JOIN coordination_archive archive ON
                json_extract(archive.json,'$.binding.provider')=json_extract(session.value,'$.provider') AND
                replace(lower(json_extract(archive.json,'$.binding.sessionId')),'-','')=json_extract(session.value,'$.native') AND
                json_extract(archive.json,'$.binding.machineId')=json_extract(session.value,'$.machine')
            LEFT JOIN coordination_ask_titles titles ON titles.request=archive.request
            WHERE json_type(archive.json,'$.work.request.returnTo')='object' AND
                json_extract(archive.json,'$.work.request.reuseSession') IS NOT 1
            """, row => new WorkAsk(new(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.GetString(4),
                row.GetString(5), DateTimeOffset.Parse(row.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                row.IsDBNull(7) ? [] : JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(row.GetString(7), Json)!),
                row.GetString(8), row.GetString(9), row.IsDBNull(10) ? null : row.GetString(10), !row.IsDBNull(11) && row.GetBoolean(11)),
            new Dictionary<string, object?> { ["sessions"] = JsonSerializer.Serialize(sessions.Select(session =>
                new { machine = session.Machine, provider = session.Provider, native = session.Session.ToString("N") })) },
            cancellationToken: token);

    public async Task<CoordinationChange[]> ChangesAsync(long after, int limit, CancellationToken token = default) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var rows = await db.QueryAsListAsync("SELECT json FROM coordination_changes WHERE sequence>@after ORDER BY sequence LIMIT @limit",
                r => JsonSerializer.Deserialize<CoordinationChange>(r.GetString(0), Json)!,
                new Dictionary<string, object?> { ["after"] = after, ["limit"] = Math.Clamp(limit, 1, 100) }, cancellationToken: token);
            return rows.ToArray();
        } finally { _gate.Release(); }
    }

    public void Dispose() { _database.Dispose(); _writerLease?.Dispose(); _gate.Dispose(); }
}
