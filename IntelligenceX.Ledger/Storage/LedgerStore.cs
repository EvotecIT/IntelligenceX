using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Reporting;
using System.Text.Json;

namespace IntelligenceX.Ledger.Storage;

/// <summary>Single service-owned database. History, projections and receipts commit together.</summary>
public sealed partial class LedgerStore(string path, LedgerRules rules,
    Func<CancellationToken, Task<CoordinationPolicy>>? policyReader = null) : IDisposable {
    internal Task<CoordinationPolicy> ReadPolicyAsync(CancellationToken token) =>
        policyReader?.Invoke(token) ?? Task.FromResult(new CoordinationPolicy(1, []));
    private sealed record GlobalObservation(LedgerEvent Event, DateTimeOffset Received);
    private sealed record ProofObservation(PullRequestProof Fact, EvidenceRef Evidence);
    private sealed record LinkObservation(PullRequestLinks Fact, EvidenceRef Evidence);
    private sealed record AssociationObservation(DateTimeOffset ObservedAtUtc, EvidenceRef Evidence);
    private sealed record GlobalSnapshot(GlobalObservation? Claims, GlobalObservation? PullRequests) {
        public string? ContentSignature { get; init; }
        public Dictionary<string, ProofObservation> Proofs { get; init; } = [];
        public Dictionary<string, LinkObservation> Links { get; init; } = [];
        public Dictionary<string, Dictionary<string, AssociationObservation>> BranchLinks { get; init; } = [];
        public Dictionary<string, Dictionary<string, AssociationObservation>> ItemLinks { get; init; } = [];
        public Dictionary<string, Dictionary<string, AssociationObservation>> QualifiedItemLinks { get; init; } = [];
    }
    private static void RememberLinks(Dictionary<string, Dictionary<string, AssociationObservation>> map, string key,
        string[] ids, DateTimeOffset at, EvidenceRef evidence) {
        if (!map.TryGetValue(key, out var known)) map[key] = known = [];
        foreach (var id in ids)
            if (!known.TryGetValue(id, out var prior) || at >= prior.ObservedAtUtc) known[id] = new(at, evidence);
    }
    private readonly SQLite _database = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static string Json<T>(T value) => JsonSerializer.Serialize(value, LedgerJson.Options);
    private static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, LedgerJson.Options)!;

    /// <summary>
    /// Version of the derived tables (session_status, session_lookup, repository_sessions) that startup rebuilds from
    /// items. Ingest keeps them current, so the rebuild runs once per version instead of on every start. Raise it when a
    /// projection's derivation changes and existing rows need rebuilding.
    /// </summary>
    internal const int ProjectionVersion = 1;

    /// <summary>
    /// Requests open their own short-lived connections. Without one connection that stays open, the last request to
    /// close checkpoints the whole WAL into the database, so one connection is kept for the store's lifetime.
    /// </summary>
    private Microsoft.Data.Sqlite.SqliteConnection? _walAnchor;

    public async Task InitializeAsync(CancellationToken token = default, IProgress<StartupStep>? progress = null) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        progress?.Report(new("opening-ledger"));
        await using var db = await _database.OpenSessionAsync(path, token);
        // A new database starts with incremental vacuum; an existing one switches with the stopped compact command.
        await db.ExecuteNonQueryAsync("""
            PRAGMA auto_vacuum=INCREMENTAL;
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS events (
              ordinal INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT NOT NULL UNIQUE,
              reporter TEXT NOT NULL, epoch TEXT NOT NULL, sequence INTEGER NOT NULL,
              item TEXT NOT NULL, received TEXT NOT NULL, json TEXT NOT NULL,
              UNIQUE(reporter,epoch,sequence));
            CREATE INDEX IF NOT EXISTS events_item ON events(item,ordinal);
            CREATE INDEX IF NOT EXISTS events_received ON events(received);
            CREATE TABLE IF NOT EXISTS receipts (id TEXT PRIMARY KEY, reporter TEXT NOT NULL, epoch TEXT NOT NULL,
              sequence INTEGER NOT NULL, fingerprint TEXT NOT NULL, received TEXT NOT NULL, UNIQUE(reporter,epoch,sequence));
            CREATE INDEX IF NOT EXISTS receipts_received ON receipts(received);
            CREATE TABLE IF NOT EXISTS items (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS heartbeats (reporter TEXT PRIMARY KEY, machine TEXT NOT NULL, received TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS machines (id TEXT PRIMARY KEY, firstseen TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS source_health (reporter TEXT PRIMARY KEY, machine TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS reporter_health (reporter TEXT PRIMARY KEY, machine TEXT NOT NULL, observed TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS leases (key TEXT PRIMARY KEY, id TEXT NOT NULL, machine TEXT NOT NULL, expires TEXT NOT NULL, json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS leases_machine ON leases(machine,expires);
            CREATE TABLE IF NOT EXISTS session_status (item TEXT PRIMARY KEY, machine TEXT NOT NULL, provider TEXT NOT NULL,
              open INTEGER NOT NULL, limited INTEGER NOT NULL, archived INTEGER NOT NULL, account TEXT);
            CREATE INDEX IF NOT EXISTS session_status_machine ON session_status(machine,archived,open);
            CREATE TABLE IF NOT EXISTS global_work (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL, received TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS transitions (ordinal INTEGER PRIMARY KEY AUTOINCREMENT,
              item TEXT NOT NULL, recorded TEXT NOT NULL, json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS transitions_recorded ON transitions(recorded);
            CREATE TABLE IF NOT EXISTS reviews (transition INTEGER PRIMARY KEY, mark TEXT NOT NULL, recorded TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS declaration_receipts (item TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL,
              PRIMARY KEY(item,id));
            """, cancellationToken: token);
        await InitializeRepositoryActivityAsync(db, token);
        await InitializeCheckoutsAsync(db, token);
        await InitializeReservationsAsync(db, token);
        await InitializeSessionLookupAsync(db, token);
        await InitializeLineageAsync(db, token);
        await InitializeGitHubUsageAsync(db, token);
        await InitializeAuthAsync(db, token);
        if (Convert.ToInt32(await db.ExecuteScalarAsync("PRAGMA user_version", cancellationToken: token)) < ProjectionVersion)
            await RebuildProjectionsAsync(db, progress, token);
        _walAnchor ??= OpenAnchor(path);
        progress?.Report(new("ledger-ready"));
    }

    private static Microsoft.Data.Sqlite.SqliteConnection OpenAnchor(string path) {
        var anchor = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder {
            DataSource = path, Pooling = false }.ToString());
        anchor.Open();
        // SQLite opens files lazily; the first statement attaches the connection to the WAL, which it then holds open.
        using var attach = anchor.CreateCommand();
        attach.CommandText = "PRAGMA journal_mode";
        attach.ExecuteScalar();
        return anchor;
    }

    /// <summary>
    /// Replaces the derived session tables with rows rebuilt from items in one transaction, so a large rebuild costs one
    /// commit instead of one per session and leaves no row from an older derivation, and records the version so later
    /// starts skip it. Progress is reported every 100 sessions.
    /// </summary>
    private static async Task RebuildProjectionsAsync(SQLiteAsyncSession db, IProgress<StartupStep>? progress, CancellationToken token) {
        progress?.Report(new("rebuilding-projections"));
        var items = await db.QueryAsListAsync("""
            SELECT json FROM items WHERE json_extract(json,'$.kind')='session'
              AND json_type(json,'$.session.repository')='object'
            """, row => Read<LedgerItem>(row.GetString(0)), cancellationToken: token);
        await db.RunInTransactionAsync(async (tx, ct) => {
            await tx.ExecuteNonQueryAsync("""
                DELETE FROM repository_sessions;
                DELETE FROM session_lookup;
                DELETE FROM session_status;
                INSERT INTO session_status(item,machine,provider,open,limited,archived,account)
                  SELECT id,json_extract(json,'$.machineId'),json_extract(json,'$.provider'),
                    COALESCE(json_extract(json,'$.session.turnOpen'),0),COALESCE(json_extract(json,'$.session.usageLimit'),0),
                    COALESCE(json_extract(json,'$.session.archived'),0),json_extract(json,'$.session.accountId')
                  FROM items WHERE json_extract(json,'$.kind')='session' AND json_type(json,'$.session')='object';
                INSERT INTO session_lookup(item,native,title,machine,provider)
                  SELECT i.id,json_extract(i.json,'$.session.sourceSessionId'),
                    COALESCE(json_extract(i.json,'$.session.title'),json_extract(i.json,'$.session.repository.title')),
                    s.machine,s.provider FROM session_status s JOIN items i ON i.id=s.item;
                """, cancellationToken: ct);
            for (var index = 0; index < items.Count; index++) {
                await UpsertRepositorySessionAsync(tx, items[index], ct);
                if ((index + 1) % 100 == 0) progress?.Report(new("rebuilding-projections", index + 1, items.Count));
            }
            await tx.ExecuteNonQueryAsync("PRAGMA user_version=" + ProjectionVersion, cancellationToken: ct);
        }, token);
    }

    public async Task<Guid[]> IngestAsync(LedgerEvent[] events, DateTimeOffset received, CancellationToken token = default, bool journal = true) {
        if (!journal && events.Any(e => e.GlobalWork is null)) throw new ArgumentException("Only source-check refreshes may bypass event history.");
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                var global = await ReadGlobalAsync(tx, ct);
                foreach (var observation in events) {
                    // Older reporters remain supported without keeping their liveness pings in history.
                    if (observation.Heartbeat || observation.Source == "watcher" && observation.Coverage is not null) {
                        await UpsertReporterHealthAsync(tx, new(observation.MachineId, observation.ReporterId, observation.SourceVersion,
                            observation.ObservedAtUtc, observation.Coverage ?? []), observation.Heartbeat, ct);
                        continue;
                    }
                    LedgerItem? progressItem = null;
                    ReporterProgress? progressUpdate = null;
                    if (observation.Source == "watcher" && observation.Session is { TurnOpen: true, TurnId: not null, LastActivityAtUtc: not null } incoming) {
                        var retained = await tx.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
                            new Dictionary<string, object?> { ["id"] = observation.ItemId }, ct);
                        if (retained is string retainedJson) {
                            var current = Read<LedgerItem>(retainedJson);
                            var compared = incoming with { LastActivityAtUtc = current.Session?.LastActivityAtUtc,
                                ArchiveObservedAtUtc = current.Session?.ArchiveObservedAtUtc, AppOwned = current.Session?.AppOwned };
                            if (current.Session?.TurnOpen == true && current.MachineId == observation.MachineId && current.Provider == observation.Provider &&
                                current.Kind == (observation.ItemKind ?? "session") && current.ParentItemId == observation.ParentItemId && Json(compared) == Json(current.Session)) {
                                progressItem = current;
                                progressUpdate = new(observation.ItemId, incoming.TurnId, incoming.LastActivityAtUtc.Value);
                            }
                        }
                    }
                    var json = Json(observation);
                    if (journal) {
                    var existing = await tx.ExecuteScalarAsync("SELECT json FROM events WHERE id=@id",
                        new Dictionary<string, object?> { ["id"] = observation.EventId.ToString() }, ct);
                    if (existing is string previous) {
                        // Additive optional fields in schema v1 must not turn an old receipt into a conflict.
                        if (Json(Read<LedgerEvent>(previous)) != json) throw new EventConflictException();
                        continue;
                    }
                    // Observations left out of history keep a receipt, so retries and identity reuse are still checked.
                    var receipt = await tx.ExecuteScalarAsync("SELECT fingerprint FROM receipts WHERE id=@id",
                        new Dictionary<string, object?> { ["id"] = observation.EventId.ToString() }, ct);
                    if (receipt is string fingerprint) {
                        if (fingerprint != Fingerprint(json)) throw new EventConflictException();
                        continue;
                    }
                    var sequenceOwner = await tx.ExecuteScalarAsync("""
                        SELECT id FROM events WHERE reporter=@reporter AND epoch=@epoch AND sequence=@seq
                        UNION ALL SELECT id FROM receipts WHERE reporter=@reporter AND epoch=@epoch AND sequence=@seq LIMIT 1
                        """, new Dictionary<string, object?> { ["reporter"] = observation.ReporterId, ["epoch"] = observation.Epoch.ToString(), ["seq"] = observation.Sequence }, ct);
                    if (sequenceOwner is string) throw new EventConflictException();
                    if (progressItem is not null && progressUpdate is not null) {
                        await ApplyProgressAsync(tx, progressItem, progressUpdate,
                            new(observation.EventId, observation.Source, observation.SourceVersion, observation.ObservedAtUtc, received), received, ct);
                        continue;
                    }
                    // Item observations are journaled after projection, and only when they changed something.
                    if (!ItemObservation(observation)) await JournalAsync(tx, observation, json, received, ct);
                    }
                    if (observation.GlobalWork is not null) {
                        global ??= new(null, null);
                        var contentSignature = ReporterChangeTracker.GlobalSignature(observation.GlobalWork);
                        if (!journal && global.ContentSignature != contentSignature) throw new EventConflictException();
                        global = global with { ContentSignature = contentSignature };
                        if (observation.GlobalWork.ClaimsAtUtc is { } claimAt &&
                            (global.Claims?.Event.GlobalWork?.ClaimsAtUtc is not { } oldClaim || claimAt >= oldClaim))
                            global = global with { Claims = new(observation, received) };
                        if (observation.GlobalWork.PullRequestsAtUtc is { } prAt &&
                            (global.PullRequests?.Event.GlobalWork?.PullRequestsAtUtc is not { } oldPr || prAt >= oldPr))
                            global = global with { PullRequests = new(observation, received) };
                        var reference = new EvidenceRef(observation.EventId, observation.Source, observation.SourceVersion, observation.ObservedAtUtc, received);
                        foreach (var pair in observation.GlobalWork.OpenPullRequestsByBranch) {
                            if (observation.GlobalWork.PullRequestsAtUtc is not { } checkedAt) continue;
                            RememberLinks(global.BranchLinks, pair.Key, pair.Value, checkedAt, reference);
                        }
                        foreach (var proof in observation.GlobalWork.PullRequestProofs ?? [])
                            if (!global.Proofs.TryGetValue(proof.Id, out var prior) || proof.CheckedAtUtc >= prior.Fact.CheckedAtUtc)
                                global.Proofs[proof.Id] = new(proof, reference);
                        foreach (var pair in observation.GlobalWork.LinksByItem ?? []) {
                            RememberLinks(global.ItemLinks, pair.Key, pair.Value.Ids, pair.Value.CheckedAtUtc, reference);
                            if (pair.Value.Scope is { } task)
                                RememberLinks(global.QualifiedItemLinks, TaskLinkKey(pair.Key, task), pair.Value.Ids, pair.Value.CheckedAtUtc, reference);
                            if (!global.Links.TryGetValue(pair.Key, out var prior) || pair.Value.CheckedAtUtc >= prior.Fact.CheckedAtUtc)
                                global.Links[pair.Key] = new(pair.Value, reference);
                        }
                        await tx.ExecuteNonQueryAsync("INSERT INTO global_work(id,json,received) VALUES(1,@json,@time) ON CONFLICT(id) DO UPDATE SET json=excluded.json,received=excluded.received",
                            new Dictionary<string, object?> { ["json"] = Json(global), ["time"] = received.ToString("O") }, ct);
                        continue;
                    }
                    if (observation.ReviewMark is { } review) {
                        if (await tx.ExecuteScalarAsync("SELECT ordinal FROM transitions WHERE ordinal=@id",
                            new Dictionary<string, object?> { ["id"] = review.TransitionId }, ct) is null) throw new EventConflictException();
                        await tx.ExecuteNonQueryAsync("INSERT INTO reviews(transition,mark,recorded) VALUES(@id,@mark,@time) ON CONFLICT(transition) DO UPDATE SET mark=excluded.mark,recorded=excluded.recorded",
                            new Dictionary<string, object?> { ["id"] = review.TransitionId, ["mark"] = review.Mark, ["time"] = received.ToString("O") }, ct);
                        continue;
                    }
                    if (observation.Heartbeat) {
                        await tx.ExecuteNonQueryAsync("""
                            INSERT INTO heartbeats(reporter,machine,received) VALUES(@reporter,@machine,@received)
                            ON CONFLICT(reporter) DO UPDATE SET received=MAX(heartbeats.received,excluded.received)
                            """, new Dictionary<string, object?> {
                                ["reporter"] = observation.ReporterId, ["machine"] = observation.MachineId,
                                ["received"] = observation.ObservedAtUtc.ToUniversalTime().ToString("O") }, ct);
                        continue;
                    }
                    if (observation.Coverage is not null) {
                        // Hints remain history, not a replacement for source-health diagnostics.
                        if (observation.Source is "hook" or "notify") continue;
                        await tx.ExecuteNonQueryAsync("INSERT INTO source_health(reporter,machine,json) VALUES(@reporter,@machine,@json) ON CONFLICT(reporter) DO UPDATE SET json=excluded.json",
                            new Dictionary<string, object?> { ["reporter"] = observation.Source == "watcher" ? observation.ReporterId : observation.ReporterId + ":" + observation.ItemId,
                                ["machine"] = observation.MachineId, ["json"] = json }, ct);
                        continue;
                    }
                    var stored = await tx.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
                        new Dictionary<string, object?> { ["id"] = observation.ItemId }, ct);
                    var item = stored is string value ? Read<LedgerItem>(value) : new LedgerItem {
                        Id = observation.ItemId, MachineId = observation.MachineId, Provider = observation.Provider,
                        Kind = observation.ItemKind ?? (observation.Delivery is null ? "session" : "delivery"), ParentItemId = observation.ParentItemId,
                        FirstObservedAtUtc = received
                    };
                    if (item.MachineId != observation.MachineId || item.Provider != observation.Provider ||
                        observation.ItemKind is not null && item.Kind != observation.ItemKind || item.ParentItemId != observation.ParentItemId)
                        throw new EventConflictException();
                    var evidence = new EvidenceRef(observation.EventId, observation.Source, observation.SourceVersion,
                        observation.ObservedAtUtc, received);
                    var decisionChanged = item.Current is null;
                    item = AdvanceScope(item, observation.Session?.Scope ?? observation.Declaration?.Scope ?? observation.Control?.Scope ?? observation.Safety?.Scope, evidence);
                    if (observation.Session?.AppOwned is { } owned &&
                        (item.OwnershipEvidence is null || observation.ObservedAtUtc >= item.OwnershipEvidence.ObservedAtUtc)) {
                        decisionChanged |= item.Session?.AppOwned != owned;
                        item = item with { Session = (item.Session ?? new()) with { AppOwned = owned }, OwnershipEvidence = evidence };
                    }
                    if (observation.Session is not null && (observation.Session.Scope is null || LedgerScope.SameTask(observation.Session.Scope, item.Scope)) &&
                        (item.Session?.TurnStartedAtUtc is null || observation.Session.TurnStartedAtUtc is null || observation.Session.TurnStartedAtUtc >= item.Session.TurnStartedAtUtc ||
                            // A full reread can recover an earlier start for a turn observed midstream.
                            item.Session.TurnOpen == true && observation.Session.TurnOpen == false &&
                            observation.Session.Outcome is "completed" or "failed" or "interrupted" or "incomplete" &&
                            observation.Session.TurnEndedAtUtc >= item.Session.TurnStartedAtUtc &&
                            observation.Session.TurnEndedAtUtc > item.Session.LastActivityAtUtc) &&
                        (item.SessionEvidence is null || observation.ObservedAtUtc >= item.SessionEvidence.ObservedAtUtc)) {
                        var observed = ObserveSessionConditions(item, observation.Session, received) with { AppOwned = item.Session?.AppOwned };
                        if (observed.StartedBySessionId is null && observed.SourceSessionId is { } native &&
                            item.Session is { StartedBySessionId: not null } previous && previous.SourceSessionId == native)
                            observed = observed with { StartedBySessionId = previous.StartedBySessionId,
                                StartedByProvider = previous.StartedByProvider, StartedVia = previous.StartedVia };
                        decisionChanged |= Json(item.Session) != Json(observed);
                        item = item with { Session = observed, SessionEvidence = evidence };
                        if (observation.Session.RepositoryBranchKey is { } branchKey && !item.RepositoryBranchKeys.Contains(branchKey))
                            item = item with { RepositoryBranchKeys = item.RepositoryBranchKeys.Append(branchKey).ToArray() };
                    }
                    if (observation.SignIn is { } signIn && (item.SignIn is null || signIn.AtUtc >= item.SignIn.AtUtc)) {
                        decisionChanged |= item.SignIn is null || item.SignIn.Required != signIn.Required || item.SignIn.Reason != signIn.Reason ||
                            item.SignIn.Held != signIn.Held;
                        item = item with { SignIn = signIn, SignInEvidence = evidence };
                    }
                    if (observation.Work is not null) {
                        var old = Json(item.ReportedWork);
                        item = MergeWork(item, observation.Work, evidence);
                        decisionChanged |= old != Json(item.ReportedWork);
                    }
                    if (observation.Declaration is not null || observation.Control is not null || observation.Safety is not null) {
                        var before = Json(item);
                        item = await ApplyDeclaredAsync(tx, item, observation, evidence, ct);
                        decisionChanged |= before != Json(item);
                    }
                    if (observation.Delivery is { } delivery) {
                        if (!item.Deliveries.TryGetValue(delivery.DeliveryId, out var state))
                            item.Deliveries[delivery.DeliveryId] = state = new() { Id = delivery.DeliveryId };
                        if (state.TargetItemId is not null && delivery.TargetItemId is not null && state.TargetItemId != delivery.TargetItemId)
                            throw new EventConflictException();
                        state.TargetItemId ??= delivery.TargetItemId;
                        if (state.Scope is not null && delivery.Scope is not null && state.Scope != delivery.Scope)
                            throw new EventConflictException();
                        state.Scope ??= delivery.Scope;
                        state.NextActor ??= delivery.NextActor;
                        state.AcceptanceBy ??= delivery.AcceptanceBy;
                        state.ResultRef ??= delivery.ResultRef;
                        // Keep the first receipt; later direct evidence may fill unknown fields of that same stage.
                        var stage = state.Stages[delivery.Stage];
                        if (stage.Knowledge == "unknown") {
                            state.Stages[delivery.Stage] = new("known", delivery.AtUtc, observation.EventId, delivery.Outcome, evidence, delivery.CauseCode);
                            decisionChanged = true;
                        } else {
                            var improveTime = delivery.AtUtc.HasValue && (stage.AtUtc is null || delivery.AtUtc < stage.AtUtc);
                            var improveOutcome = stage.Outcome is null && delivery.Outcome is not null;
                            var improveCause = stage.CauseCode is null && delivery.CauseCode is not null;
                            if (improveTime || improveOutcome || improveCause) {
                                state.Stages[delivery.Stage] = stage with {
                                    AtUtc = improveTime ? delivery.AtUtc : stage.AtUtc, TimeEvidence = improveTime ? evidence : stage.TimeEvidence,
                                    Outcome = improveOutcome ? delivery.Outcome : stage.Outcome, OutcomeEvidence = improveOutcome ? evidence : stage.OutcomeEvidence,
                                    CauseCode = improveCause ? delivery.CauseCode : stage.CauseCode, CauseEvidence = improveCause ? evidence : stage.CauseEvidence
                                };
                                decisionChanged = true;
                            }
                        }
                        var orderAt = delivery.AtUtc ?? observation.ObservedAtUtc;
                        var historicalAdmission = delivery.Stage is "requested" or "admitted" &&
                            state.LastStage is "submitted" or "started" or "terminal" or "accepted" or "held" or "uncertain";
                        var unprovenRecovery = (state.LastStage is "held" or "uncertain") && delivery.Stage is not ("held" or "uncertain" or "terminal" or "accepted") &&
                            (delivery.Stage != "started" || delivery.AtUtc is null || delivery.AtUtc <= state.LastStageOrderAtUtc);
                        var knownStageTime = state.Stages[delivery.Stage].AtUtc;
                        var refineCurrentTime = state.LastStage == delivery.Stage && state.LastStageTimeIsInferred && knownStageTime.HasValue;
                        if (!historicalAdmission && !unprovenRecovery && (state.LastStageOrderAtUtc is null || orderAt >= state.LastStageOrderAtUtc ||
                            refineCurrentTime || state.LastStageTimeIsInferred && state.LastStage is "requested" or "admitted" && observation.ObservedAtUtc >= state.DispositionObservedAtUtc)) {
                            decisionChanged |= state.LastStage != delivery.Stage || refineCurrentTime;
                            state.LastStage = delivery.Stage; state.LastStageAtUtc = refineCurrentTime ? knownStageTime : delivery.AtUtc ?? received;
                            state.LastStageTimeIsInferred = !refineCurrentTime && delivery.AtUtc is null;
                            state.DispositionObservedAtUtc = observation.ObservedAtUtc;
                            state.LastStageOrderAtUtc = refineCurrentTime ? knownStageTime : orderAt;
                        }
                    }
                    if (observation.Coverage is not null) item = item with { Coverage = observation.Coverage };
                    if (item.ParentItemId is { } parentId) {
                        var parentJson = await tx.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
                            new Dictionary<string, object?> { ["id"] = parentId }, ct);
                        item = ApplyParent(item, parentJson is string parentValue ? Read<LedgerItem>(parentValue) : null);
                    }
                    item = ApplyGlobal(item, global, received);
                    // Only the runtime's heartbeat check can release a last-known projection.
                    // Historical stages may enrich history without proving that the reporter is live.
                    if (decisionChanged) item = await EvaluateAsync(tx, item, received, ct, item.Current?.LastKnown == true);
                    // The projection always keeps the newest evidence and activity time; history keeps only real changes.
                    if (journal && (stored is not string storedJson || JournalShape(storedJson) != JournalShape(Json(item))))
                        await JournalAsync(tx, observation, json, received, ct);
                    else if (journal) await ReceiptAsync(tx, observation, json, received, ct);
                    await tx.ExecuteNonQueryAsync("INSERT INTO items(id,json) VALUES(@id,@json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
                        new Dictionary<string, object?> { ["id"] = item.Id, ["json"] = Json(item) }, ct);
                    if (item.Kind == "session" && item.Session is { } session) await UpsertSessionStatusAsync(tx, item, session, ct);
                    if (item.Kind == "session") await UpsertRepositorySessionAsync(tx, item, ct);
                    // Only the canonical item for that machine and provider feeds the sign-in history.
                    if (item.Kind == "sign-in" && item.SignIn is { } published && ReferenceEquals(published, observation.SignIn) &&
                        item.Id == item.MachineId + ":sign-in:" + published.Provider)
                        await RecordSignInAsync(tx, item.MachineId, published, ct);
                }
                return events.Select(x => x.EventId).ToArray();
            }, token);
        } finally { _gate.Release(); }
    }

    /// <summary>Observations that update an item projection, as opposed to health, global work or review marks.</summary>
    private static bool ItemObservation(LedgerEvent observation) =>
        observation.GlobalWork is null && observation.ReviewMark is null && !observation.Heartbeat && observation.Coverage is null;

    private static Task<int> JournalAsync(SQLiteAsyncSession tx, LedgerEvent observation, string json, DateTimeOffset received, CancellationToken token) =>
        tx.ExecuteNonQueryAsync("""
            INSERT INTO events(id,reporter,epoch,sequence,item,received,json)
            VALUES(@id,@reporter,@epoch,@seq,@item,@received,@json)
            """, new Dictionary<string, object?> {
                ["id"] = observation.EventId.ToString(), ["reporter"] = observation.ReporterId,
                ["epoch"] = observation.Epoch.ToString(), ["seq"] = observation.Sequence,
                ["item"] = observation.ItemId, ["received"] = received.ToString("O"), ["json"] = json
            }, token);

    private static Task<int> ReceiptAsync(SQLiteAsyncSession tx, LedgerEvent observation, string json, DateTimeOffset received, CancellationToken token) =>
        tx.ExecuteNonQueryAsync("""
            INSERT INTO receipts(id,reporter,epoch,sequence,fingerprint,received)
            VALUES(@id,@reporter,@epoch,@seq,@fingerprint,@received)
            """, new Dictionary<string, object?> {
                ["id"] = observation.EventId.ToString(), ["reporter"] = observation.ReporterId,
                ["epoch"] = observation.Epoch.ToString(), ["seq"] = observation.Sequence,
                ["fingerprint"] = Fingerprint(json), ["received"] = received.ToString("O")
            }, token);

    private static string Fingerprint(string json) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));

    // Fields that move on every observation without saying anything new about the item. A delivery's
    // disposition time only orders repeats of the same stage; a new stage or effective time changes other fields.
    private static readonly HashSet<string> RefreshOnlyFields = new(StringComparer.Ordinal) {
        "lastActivityAtUtc", "lastProgressAtUtc", "archiveObservedAtUtc", "evaluatedAtUtc", "dispositionObservedAtUtc" };

    /// <summary>
    /// An item's stored form without evidence references and refresh-only timestamps. Two observations with the
    /// same shape differ only in when something was seen again, so the second one isn't worth a history row.
    /// </summary>
    internal static string JournalShape(string itemJson) {
        var node = System.Text.Json.Nodes.JsonNode.Parse(itemJson);
        Strip(node);
        return node?.ToJsonString() ?? "";

        static void Strip(System.Text.Json.Nodes.JsonNode? value) {
            if (value is System.Text.Json.Nodes.JsonObject obj) {
                foreach (var name in obj.Select(pair => pair.Key).ToArray())
                    // Evidence references are objects or arrays; a text field such as originEvidence is real content.
                    if ((name == "evidence" || name.EndsWith("Evidence", StringComparison.Ordinal)) && obj[name] is System.Text.Json.Nodes.JsonObject or System.Text.Json.Nodes.JsonArray ||
                        RefreshOnlyFields.Contains(name)) obj.Remove(name);
                    else Strip(obj[name]);
            } else if (value is System.Text.Json.Nodes.JsonArray array) foreach (var child in array) Strip(child);
        }
    }

    /// <summary>Holds the single in-process writer, so tests can prove which paths never wait for it.</summary>
    internal SemaphoreSlim WriterGate => _gate;

    /// <summary>Runs after a full evaluation and before its changed rows are written.</summary>
    internal Func<CancellationToken, Task>? BeforeEvaluationWrite { get; set; }

    public async Task<LedgerItem[]> EvaluateAllAsync(DateTimeOffset now, CancellationToken token = default, HashSet<string>? silentMachines = null) {
        // The full evaluation reads one snapshot and computes without the writer. Holding it here made
        // reporter ingest and health wait for every work view and shadow report, so their 10-second
        // deliveries timed out and live sessions looked stale.
        await using var db = await _database.OpenSessionAsync(path, token);
        var (stored, global) = await db.RunInTransactionAsync(SQLiteTransactionMode.Deferred, async (tx, ct) =>
            (await tx.QueryAsListAsync("SELECT json FROM items ORDER BY id", r => r.GetString(0), cancellationToken: ct),
             await ReadGlobalAsync(tx, ct)), token);
        var rows = stored.Select(Read<LedgerItem>).ToArray();
        var parents = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var results = new LedgerItem[rows.Length];
        var changed = new List<(string Stored, LedgerItem Item, string? Transition)>();
        for (var index = 0; index < rows.Length; index++) {
            token.ThrowIfCancellationRequested();
            var row = rows[index];
            var related = ApplyParent(row, row.ParentItemId is { } parentId ? parents.GetValueOrDefault(parentId) : null);
            var (item, transition) = Reevaluate(ApplyGlobal(related, global, now), now, silentMachines?.Contains(row.MachineId) == true);
            // Polling computes a fresh evaluation time without rewriting otherwise unchanged rows.
            var previous = row with { Current = row.Current is null ? null : row.Current with { EvaluatedAtUtc = item.Current!.EvaluatedAtUtc } };
            if (Json(previous) != Json(item)) changed.Add((stored[index], item, transition));
            results[index] = item;
        }
        if (changed.Count == 0) return results;
        if (BeforeEvaluationWrite is { } pending) await pending(token);
        await _gate.WaitAsync(token);
        try {
            await db.RunInTransactionAsync(async (tx, ct) => {
                foreach (var (previous, item, transition) in changed) {
                    // Ingest may have stored newer evidence since the snapshot. Keep it; the next
                    // evaluation reconsiders that row instead of overwriting it with older input.
                    var written = await tx.ExecuteNonQueryAsync("UPDATE items SET json=@json WHERE id=@id AND json=@previous",
                        new Dictionary<string, object?> { ["json"] = Json(item), ["id"] = item.Id, ["previous"] = previous }, ct);
                    if (written == 0) await KeepConcurrentLimitClearanceAsync(tx, item, now, ct);
                    else if (transition is not null) await InsertTransitionAsync(tx, item.Id, now, transition, ct);
                }
            }, token);
        } finally { _gate.Release(); }
        return results;
    }

    private static async Task<GlobalSnapshot?> ReadGlobalAsync(SQLiteAsyncSession db, CancellationToken token) {
        var rows = await db.QueryAsListAsync("SELECT json,received FROM global_work WHERE id=1",
            r => (Json: r.GetString(0), Received: DateTimeOffset.Parse(r.GetString(1))), cancellationToken: token);
        if (rows.Count == 0) return null;
        using var document = JsonDocument.Parse(rows[0].Json);
        if (document.RootElement.TryGetProperty("claims", out _)) return Read<GlobalSnapshot>(rows[0].Json);
        // Preserve observations collected by the earlier local Phase 1 candidate.
        var legacy = new GlobalObservation(Read<LedgerEvent>(rows[0].Json), rows[0].Received);
        return new(legacy.Event.GlobalWork?.ClaimsAtUtc is null ? null : legacy,
            legacy.Event.GlobalWork?.PullRequestsAtUtc is null ? null : legacy);
    }

    private LedgerItem ApplyGlobal(LedgerItem item, GlobalSnapshot? envelope, DateTimeOffset now) {
        envelope ??= new(null, null);
        var globalClaims = envelope.Claims?.Event.GlobalWork;
        var globalPrs = envelope.PullRequests?.Event.GlobalWork;
        var work = item.ReportedWork ?? item.Work ?? new();
        var useClaims = globalClaims?.ClaimsAtUtc is { } claims && (work.ActiveClaim is null || work.ClaimsAtUtc is null || claims >= work.ClaimsAtUtc);
        var usePrs = globalPrs?.PullRequestsAtUtc is { } prs && (work.PullRequestsKnown != true || work.PullRequestsAtUtc is null || prs >= work.PullRequestsAtUtc);
        var claimsAt = useClaims ? globalClaims!.ClaimsAtUtc : work.ClaimsAtUtc;
        var prsAt = usePrs ? globalPrs!.PullRequestsAtUtc : work.PullRequestsAtUtc;
        var claimsKnown = claimsAt.HasValue && now - claimsAt <= rules.EvidenceMaxAge && (useClaims || work.ActiveClaim is not null);
        var prsKnown = usePrs ? now - globalPrs!.PullRequestsAtUtc <= rules.EvidenceMaxAge && item.Session?.RepositoryBranchKey is not null
            : prsAt.HasValue && now - prsAt <= rules.EvidenceMaxAge && work.PullRequestsKnown == true;
        var open = item.Session?.RepositoryBranchKey is { } branch && globalPrs is not null && globalPrs.OpenPullRequestsByBranch.TryGetValue(branch, out var found) ? found : [];
        var associated = item.RepositoryBranchKeys.Concat(item.Session?.RepositoryBranchKey is { } branchKey ? [branchKey] : Array.Empty<string>())
            .Distinct(StringComparer.Ordinal).Where(envelope.BranchLinks.ContainsKey).SelectMany(key => envelope.BranchLinks[key])
            .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.MaxBy(pair => pair.Value.ObservedAtUtc).Value);
        var itemLinks = envelope.ItemLinks.GetValueOrDefault(item.Id) ?? [];
        envelope.Links.TryGetValue(item.Id, out var declared);
        var ownership = item.Scope is { } taskScope ? envelope.QualifiedItemLinks.GetValueOrDefault(TaskLinkKey(item.Id, taskScope)) ?? [] : itemLinks;
        var owned = work.OwnedPullRequests.Concat(ownership.Keys).Distinct(StringComparer.Ordinal).ToArray();
        var linked = item.LinkedPullRequestIds.Concat(work.OpenPullRequests).Concat(open).Concat(associated.Keys).Concat(itemLinks.Keys).Concat(declared?.Fact.Ids ?? [])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var relationAt = linked.SequenceEqual(item.LinkedPullRequestIds) ? item.LinkedEvidenceChangedAtUtc :
            new[] { work.PullRequestsAtUtc, globalPrs?.PullRequestsAtUtc, declared?.Fact.CheckedAtUtc }.Max();
        var positives = new Dictionary<string, PullRequestPositive>(item.PositivePullRequests, StringComparer.Ordinal);
        void RememberPositive(string id, DateTimeOffset at, EvidenceRef evidence) {
            if (!positives.TryGetValue(id, out var old) || at >= old.ObservedAtUtc) positives[id] = new(at, evidence);
        }
        foreach (var pair in associated) RememberPositive(pair.Key, pair.Value.ObservedAtUtc, pair.Value.Evidence);
        if (work.PullRequestsAtUtc is { } localAt && (item.WorkDimensionEvidence.GetValueOrDefault("pr") ?? item.WorkEvidence) is { } localRef)
            foreach (var id in work.OpenPullRequests) RememberPositive(id, localAt, localRef);
        foreach (var id in linked)
            if (envelope.Proofs.TryGetValue(id, out var proof) && proof.Fact.State == "open") RememberPositive(id, proof.Fact.CheckedAtUtc, proof.Evidence);
        DateTimeOffset? PositiveAt(string id) => positives.GetValueOrDefault(id)?.ObservedAtUtc;
        var positive = new HashSet<string>(positives.Keys, StringComparer.Ordinal);
        foreach (var id in linked) {
            if (!envelope.Proofs.TryGetValue(id, out var proof)) continue;
            if (proof.Fact.State == "open") positive.Add(id);
            else if (proof.Fact.State == "merged" || PositiveAt(id) is not { } positiveAt || proof.Fact.CheckedAtUtc >= positiveAt) positive.Remove(id);
        }
        var localComplete = work.PullRequestsKnown == true && work.PullRequestsAtUtc is { } localCheck && now - localCheck <= rules.EvidenceMaxAge &&
            (relationAt is null || localCheck >= relationAt) && !positives.Values.Any(proof => proof.ObservedAtUtc > localCheck);
        var hadPrs = item.Work?.HadPullRequests == true || work.HadPullRequests || linked.Length > 0 || declared is not null;
        if (usePrs || !localComplete) {
            var scopeFresh = globalPrs?.PullRequestsAtUtc is { } checkedAt && now - checkedAt <= rules.EvidenceMaxAge && item.Session?.RepositoryBranchKey is not null ||
                declared?.Fact is { Complete: true } link && now - link.CheckedAtUtc <= rules.EvidenceMaxAge;
            var allClosed = linked.Length > 0 && linked.All(id => envelope.Proofs.TryGetValue(id, out var proof) &&
                (proof.Fact.State == "merged" || proof.Fact.State == "closed" && now - proof.Fact.CheckedAtUtc <= rules.EvidenceMaxAge) && !positive.Contains(id));
            prsKnown = scopeFresh && (!hadPrs || declared?.Fact.Complete != false && allClosed);
        }
        var effectiveOpen = localComplete && !usePrs ? work.OpenPullRequests : positive.Count > 0 ? positive.Order(StringComparer.Ordinal).ToArray() :
            prsKnown ? [] : item.Work?.OpenPullRequests ?? work.OpenPullRequests;
        effectiveOpen = effectiveOpen.Concat(linked.Where(id => envelope.Proofs.TryGetValue(id, out var proof) && proof.Fact.State == "open" &&
            (!localComplete || proof.Fact.CheckedAtUtc > work.PullRequestsAtUtc))).Distinct(StringComparer.Ordinal).ToArray();
        if (item.Scope is not null) {
            var taskPr = TaskPullRequests(item, work, envelope, declared, owned, positives, positive, now);
            prsKnown = taskPr.Known;
            effectiveOpen = taskPr.Open;
            hadPrs = work.HadPullRequests || owned.Length > 0;
        }
        // Exact terminal proof retires stale positive search entries and their implicit waits in both projections.
        effectiveOpen = effectiveOpen.Where(id => !envelope.Proofs.TryGetValue(id, out var proof) ||
            proof.Fact.State == "open" || proof.Fact.State != "merged" && PositiveAt(id) is { } positiveAt && proof.Fact.CheckedAtUtc < positiveAt).ToArray();
        static EvidenceRef? Evidence(GlobalObservation? observation) => observation is null ? null : new(observation.Event.EventId,
            observation.Event.Source, observation.Event.SourceVersion, observation.Event.ObservedAtUtc, observation.Received);
        return item with { ReportedWork = item.ReportedWork ?? work, LinkedPullRequestIds = linked, LinkedEvidenceChangedAtUtc = relationAt, PositivePullRequests = positives,
            PullRequestProofEvidence = linked.Where(envelope.Proofs.ContainsKey).Select(id => envelope.Proofs[id].Evidence)
                .Concat(associated.Values.Concat(itemLinks.Values).Concat(ownership.Values).Select(link => link.Evidence))
                .Concat(positives.Values.Select(proof => proof.Evidence))
                .Concat(declared is null ? [] : new[] { declared.Evidence }).DistinctBy(e => e.EventId).ToArray(), Work = work with {
            ActiveClaim = claimsKnown ? useClaims ? globalClaims!.ActiveClaimItems.Contains(item.Id) : work.ActiveClaim : null, ClaimsAtUtc = claimsAt,
            PullRequestsKnown = prsKnown ? true : null, PullRequestsAtUtc = prsAt,
            OpenPullRequests = effectiveOpen, HadPullRequests = hadPrs,
            OwnedPullRequests = owned,
            CancelledPullRequests = (item.Scope is null ? linked : owned).Where(id => envelope.Proofs.TryGetValue(id, out var proof) &&
                proof.Fact.State == "closed" && !positive.Contains(id)).ToArray() },
            ClaimEvidence = useClaims ? Evidence(envelope.Claims) : item.WorkDimensionEvidence.GetValueOrDefault("claim") ?? item.WorkEvidence,
            PullRequestEvidence = usePrs ? Evidence(envelope.PullRequests) : item.WorkDimensionEvidence.GetValueOrDefault("pr") ?? item.WorkEvidence,
            GlobalEvidence = Evidence(envelope.Claims ?? envelope.PullRequests) };
    }

    private async Task<LedgerItem> EvaluateAsync(SQLiteAsyncSession tx, LedgerItem item, DateTimeOffset now, CancellationToken token, bool silent = false) {
        var (evaluated, transition) = Reevaluate(item, now, silent);
        if (transition is not null) await InsertTransitionAsync(tx, evaluated.Id, now, transition, token);
        return evaluated;
    }

    private static Task<int> InsertTransitionAsync(SQLiteAsyncSession tx, string item, DateTimeOffset now, string transition, CancellationToken token) =>
        tx.ExecuteNonQueryAsync("INSERT INTO transitions(item,recorded,json) VALUES(@item,@recorded,@json)",
            new Dictionary<string, object?> { ["item"] = item, ["recorded"] = now.ToString("O"), ["json"] = transition }, token);

    /// <summary>Evaluates one item and returns the transition record when its verdict changed.</summary>
    private (LedgerItem Item, string? Transition) Reevaluate(LedgerItem item, DateTimeOffset now, bool silent = false) {
        var owned = item.Work?.OwnedPullRequests.Intersect(item.Work.OpenPullRequests).Order(StringComparer.Ordinal).ToArray() ?? [];
        var waits = new Dictionary<string, WaitFact>(item.InferredReviewWaits, StringComparer.Ordinal);
        if (item.Session?.Outcome == "completed" && item.Work?.Wait is null) {
            foreach (var id in owned) {
                if (waits.ContainsKey(id)) continue;
                var first = item.ImplicitReviewWait?.ConditionId == id ? item.ImplicitReviewWait.RecordedAtUtc : now;
                waits[id] = rules.ReviewWait(id, first, item.Scope);
            }
        }
        foreach (var id in waits.Keys.Where(id => !owned.Contains(id)).ToArray()) waits.Remove(id);
        var inferred = item.Session?.Outcome == "completed" && item.Work?.Wait is null ? owned.Select(id => waits[id]).FirstOrDefault() : null;
        item = item with { InferredReviewWaits = waits, ImplicitReviewWait = inferred };
        var verdict = rules.Evaluate(item, now, silent);
        item = RememberLimitClearance(item, verdict, now);
        string? transition = null;
        if (!item.IsMessageEvidence && (item.Current?.Value != verdict.Value || item.Current.ReasonCode != verdict.ReasonCode || item.Current.EpisodeId != verdict.EpisodeId ||
            item.Current.SinceUtc != verdict.SinceUtc))
            transition = Json(new { item.Id, item.MachineId, item.Provider, verdict, evidence = item.Evidence() });
        return (item with { Current = verdict }, transition);
    }

    // Read paths use their own WAL snapshot. They never take the writer gate, so a slow
    // writer cannot delay session lookups, machine rows, coverage or shadow reports.
    public async Task<object[]> HistoryAsync(string itemId, long after, int limit, CancellationToken token) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var rows = await db.QueryAsListAsync("SELECT ordinal,received,json FROM events WHERE item=@id AND ordinal>@after ORDER BY ordinal LIMIT @limit",
            r => (object)new { ordinal = r.GetInt64(0), receivedAtUtc = r.GetString(1), observation = Read<LedgerEvent>(r.GetString(2)) },
            new Dictionary<string, object?> { ["id"] = itemId, ["after"] = after, ["limit"] = limit }, cancellationToken: token);
        return rows.ToArray();
    }

    public async Task<Dictionary<string, DateTimeOffset>> HeartbeatsAsync(CancellationToken token) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var rows = await db.QueryAsListAsync("SELECT reporter,MAX(received) FROM (SELECT reporter,received FROM heartbeats UNION ALL SELECT reporter,observed AS received FROM reporter_health) GROUP BY reporter", r =>
            (r.GetString(0), DateTimeOffset.Parse(r.GetString(1))), cancellationToken: token);
        return rows.ToDictionary(x => x.Item1, x => x.Item2);
    }

    private Dictionary<string, DateTimeOffset>? _enrolled;

    /// <summary>Records first enrollment once. Later machine reads reuse it without entering the writer.</summary>
    public async Task<Dictionary<string, DateTimeOffset>> EnrollAsync(IEnumerable<string> machines, CancellationToken token) {
        var requested = machines.ToArray();
        if (Volatile.Read(ref _enrolled) is { } known && requested.All(known.ContainsKey)) return new(known);
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            foreach (var machine in requested) await db.ExecuteNonQueryAsync("INSERT OR IGNORE INTO machines(id,firstseen) VALUES(@id,@time)",
                new Dictionary<string, object?> { ["id"] = machine, ["time"] = DateTimeOffset.UtcNow.ToString("O") }, token);
            var rows = await db.QueryAsListAsync("SELECT id,firstseen FROM machines", r => (r.GetString(0), DateTimeOffset.Parse(r.GetString(1))), cancellationToken: token);
            var enrolled = rows.ToDictionary(x => x.Item1, x => x.Item2);
            Volatile.Write(ref _enrolled, enrolled);
            return new(enrolled);
        } finally { _gate.Release(); }
    }

    public async Task<LedgerEvent[]> CoverageAsync(CancellationToken token) {
        await using var db = await _database.OpenSessionAsync(path, token);
        // One statement, so the fallback rows and the health rows they give way to come from the same snapshot.
        var all = await db.QueryAsListAsync("""
            SELECT json,NULL FROM source_health WHERE reporter NOT IN (SELECT reporter FROM reporter_health)
            UNION ALL SELECT json,observed FROM reporter_health
            """, r => (Json: r.GetString(0), Observed: r.IsDBNull(1) ? null : r.GetString(1)), cancellationToken: token);
        var rows = all.Where(row => row.Observed is null).Select(row => Read<LedgerEvent>(row.Json));
        var health = all.Where(row => row.Observed is not null).Select(row => Read<ReporterHealth>(row.Json) with {
            ObservedAtUtc = DateTimeOffset.Parse(row.Observed!) });
        return rows.Concat(health.Select(h => new LedgerEvent { EventId = Guid.Empty, Epoch = Guid.Empty, Sequence = 1,
            ReporterId = h.ReporterId, MachineId = h.MachineId, ItemId = "source-health", Provider = "machine", Source = "watcher",
            SourceVersion = h.SourceVersion, ObservedAtUtc = h.ObservedAtUtc, Coverage = h.Coverage })).ToArray();
    }

    public async Task<object[]> DailyAsync(DateOnly date, CancellationToken token) {
        await using var db = await _database.OpenSessionAsync(path, token);
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var rows = await db.QueryAsListAsync("""
            SELECT t.ordinal,t.json,r.mark,i.json FROM transitions t
            LEFT JOIN reviews r ON r.transition=t.ordinal LEFT JOIN items i ON i.id=t.item
            WHERE t.recorded>=@start AND t.recorded<@end ORDER BY t.ordinal
            """, r => (IsMessageEvidence: !r.IsDBNull(3) && Read<LedgerItem>(r.GetString(3)).IsMessageEvidence,
                Row: (object)new { transitionId = r.GetInt64(0), transition = JsonSerializer.Deserialize<JsonElement>(r.GetString(1)),
                    mark = r.IsDBNull(2) ? "unmarked" : r.GetString(2) }), new Dictionary<string, object?> {
                ["start"] = start.ToString("O"), ["end"] = start.AddDays(1).ToString("O") }, cancellationToken: token);
        return rows.Where(row => !row.IsMessageEvidence).Select(row => row.Row).ToArray();
    }

    public async Task<object> ReportAsync(DateOnly date, DateTimeOffset now, CancellationToken token) {
        var transitions = await DailyAsync(date, token);
        var reporterHealth = await HeartbeatsAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var bounds = await db.QueryAsListAsync("SELECT MIN(received),MAX(received),COUNT(*) FROM events WHERE received>=@start AND received<@end",
            r => new { firstReceiptAtUtc = r.IsDBNull(0) ? (DateTimeOffset?)null : DateTimeOffset.Parse(r.GetString(0)),
                latestReceiptAtUtc = r.IsDBNull(1) ? (DateTimeOffset?)null : DateTimeOffset.Parse(r.GetString(1)), eventCount = r.GetInt64(2) },
            new Dictionary<string, object?> { ["start"] = start.ToString("O"), ["end"] = start.AddDays(1).ToString("O") }, cancellationToken: token);
        var unknowns = await db.QueryAsListAsync("SELECT json FROM items", r => Read<LedgerItem>(r.GetString(0)), cancellationToken: token);
        return new { date, generatedAtUtc = now, coverage = "observed-only", observationCoverage = bounds.Single(), reporterHealthAsOfUtc = now, reporterHealth,
            unresolvedUnknownsAsOfUtc = now, unresolvedUnknowns = unknowns.Where(i => !i.IsMessageEvidence && i.Current?.ReasonCode == "unknown")
                .Select(i => new { i.Id, i.MachineId, i.Provider, i.Current!.SinceUtc, i.Current.MissingFacts }).ToArray(), transitions };
    }

    public void Dispose() { _database.Dispose(); _gate.Dispose(); _leaseGate.Dispose(); _walAnchor?.Dispose(); }
}

public sealed class EventConflictException : Exception;
