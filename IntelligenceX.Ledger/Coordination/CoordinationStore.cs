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
            """, cancellationToken: token);
        await db.ExecuteNonQueryAsync("INSERT OR IGNORE INTO coordination_state(id,json) VALUES(1,@json)",
            new Dictionary<string, object?> { ["json"] = JsonSerializer.Serialize(new CoordinationState {
                Policy = initialPolicy, ResultDeliveryAttempts = []
            }, Json) }, token);
        await ExecuteAsync((engine, _) => { engine.ValidateRequesterPrincipals(); return true; }, DateTimeOffset.UtcNow, token);
    }

    public async Task<T> ExecuteAsync<T>(Func<CoordinationEngine, CoordinationState, T> command, DateTimeOffset now,
        CancellationToken token = default) {
        if (_writerLease is null) throw new InvalidOperationException("Coordination store has not initialized.");
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
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
                var engine = new CoordinationEngine(state, now, configuredPrincipals);
                var result = command(engine, state);
                var updated = JsonSerializer.Serialize(state, Json);
                if (updated != value) await tx.ExecuteNonQueryAsync("UPDATE coordination_state SET json=@json WHERE id=1",
                    new Dictionary<string, object?> { ["json"] = updated }, ct);
                foreach (var change in engine.Changes) await tx.ExecuteNonQueryAsync("""
                    INSERT INTO coordination_changes(sequence,request,stage,principal,recorded,json)
                    VALUES(@sequence,@request,@stage,@principal,@recorded,@json)
                    """, new Dictionary<string, object?> {
                        ["sequence"] = change.Sequence, ["request"] = change.RequestId, ["stage"] = change.Stage,
                        ["principal"] = change.Principal, ["recorded"] = change.AtUtc.ToString("O"),
                        ["json"] = JsonSerializer.Serialize(change, Json)
                    }, ct);
                return result;
            }, token);
        } finally { _gate.Release(); }
    }

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
