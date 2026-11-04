using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using System.Text.Json;

namespace IntelligenceX.Ledger.Storage;

/// <summary>Single service-owned database. History, projections and receipts commit together.</summary>
public sealed partial class LedgerStore(string path, LedgerRules rules) : IDisposable {
    private sealed record GlobalObservation(LedgerEvent Event, DateTimeOffset Received);
    private sealed record ProofObservation(PullRequestProof Fact, EvidenceRef Evidence);
    private sealed record LinkObservation(PullRequestLinks Fact, EvidenceRef Evidence);
    private sealed record AssociationObservation(DateTimeOffset ObservedAtUtc, EvidenceRef Evidence);
    private sealed record GlobalSnapshot(GlobalObservation? Claims, GlobalObservation? PullRequests) {
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

    public async Task InitializeAsync(CancellationToken token = default) {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var db = await _database.OpenSessionAsync(path, token);
        await db.ExecuteNonQueryAsync("""
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS events (
              ordinal INTEGER PRIMARY KEY AUTOINCREMENT, id TEXT NOT NULL UNIQUE,
              reporter TEXT NOT NULL, epoch TEXT NOT NULL, sequence INTEGER NOT NULL,
              item TEXT NOT NULL, received TEXT NOT NULL, json TEXT NOT NULL,
              UNIQUE(reporter,epoch,sequence));
            CREATE INDEX IF NOT EXISTS events_item ON events(item,ordinal);
            CREATE TABLE IF NOT EXISTS items (id TEXT PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS heartbeats (reporter TEXT PRIMARY KEY, machine TEXT NOT NULL, received TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS machines (id TEXT PRIMARY KEY, firstseen TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS source_health (reporter TEXT PRIMARY KEY, machine TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS global_work (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL, received TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS transitions (ordinal INTEGER PRIMARY KEY AUTOINCREMENT,
              item TEXT NOT NULL, recorded TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS reviews (transition INTEGER PRIMARY KEY, mark TEXT NOT NULL, recorded TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS declaration_receipts (item TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL,
              PRIMARY KEY(item,id));
            """, cancellationToken: token);
    }

    public async Task<Guid[]> IngestAsync(LedgerEvent[] events, DateTimeOffset received, CancellationToken token = default) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                var global = await ReadGlobalAsync(tx, ct);
                foreach (var observation in events) {
                    var json = Json(observation);
                    var existing = await tx.ExecuteScalarAsync("SELECT json FROM events WHERE id=@id",
                        new Dictionary<string, object?> { ["id"] = observation.EventId.ToString() }, ct);
                    if (existing is string previous) {
                        // Additive optional fields in schema v1 must not turn an old receipt into a conflict.
                        if (Json(Read<LedgerEvent>(previous)) != json) throw new EventConflictException();
                        continue;
                    }
                    var sequenceOwner = await tx.ExecuteScalarAsync("SELECT id FROM events WHERE reporter=@reporter AND epoch=@epoch AND sequence=@seq",
                        new Dictionary<string, object?> { ["reporter"] = observation.ReporterId, ["epoch"] = observation.Epoch.ToString(), ["seq"] = observation.Sequence }, ct);
                    if (sequenceOwner is string) throw new EventConflictException();
                    await tx.ExecuteNonQueryAsync("""
                        INSERT INTO events(id,reporter,epoch,sequence,item,received,json)
                        VALUES(@id,@reporter,@epoch,@seq,@item,@received,@json)
                        """, new Dictionary<string, object?> {
                            ["id"] = observation.EventId.ToString(), ["reporter"] = observation.ReporterId,
                            ["epoch"] = observation.Epoch.ToString(), ["seq"] = observation.Sequence,
                            ["item"] = observation.ItemId, ["received"] = received.ToString("O"), ["json"] = json
                        }, ct);
                    if (observation.GlobalWork is not null) {
                        global ??= new(null, null);
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
                        (item.Session?.TurnStartedAtUtc is null || observation.Session.TurnStartedAtUtc is null || observation.Session.TurnStartedAtUtc >= item.Session.TurnStartedAtUtc) &&
                        (item.SessionEvidence is null || observation.ObservedAtUtc >= item.SessionEvidence.ObservedAtUtc)) {
                        ObserveCondition(item, "question", observation.Session.Question, received);
                        ObserveCondition(item, "input-unconfirmed", observation.Session.InputUnconfirmed, received);
                        ObserveCondition(item, "usage-limit", observation.Session.UsageLimit, received);
                        ObserveCondition(item, "complete", observation.Session.Outcome == "completed", received);
                        ObserveCondition(item, "idle", observation.Session.Outcome == "completed", received);
                        if (observation.Session.TurnOpen.HasValue) {
                            ObserveCondition(item, "active", observation.Session.TurnOpen == true, received);
                            ObserveCondition(item, "quiet", observation.Session.TurnOpen == true, received);
                        }
                        decisionChanged |= Json(item.Session) != Json(observation.Session);
                        item = item with { Session = observation.Session with { AppOwned = item.Session?.AppOwned }, SessionEvidence = evidence };
                        if (observation.Session.RepositoryBranchKey is { } branchKey && !item.RepositoryBranchKeys.Contains(branchKey))
                            item = item with { RepositoryBranchKeys = item.RepositoryBranchKeys.Append(branchKey).ToArray() };
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
                        if (!historicalAdmission && !unprovenRecovery && (state.LastStageOrderAtUtc is null || orderAt >= state.LastStageOrderAtUtc ||
                            state.LastStageTimeIsInferred && state.LastStage is "requested" or "admitted" && observation.ObservedAtUtc >= state.DispositionObservedAtUtc)) {
                            decisionChanged |= state.LastStage != delivery.Stage;
                            state.LastStage = delivery.Stage; state.LastStageAtUtc = delivery.AtUtc ?? received;
                            state.LastStageTimeIsInferred = delivery.AtUtc is null;
                            state.DispositionObservedAtUtc = observation.ObservedAtUtc;
                            state.LastStageOrderAtUtc = orderAt;
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
                    await tx.ExecuteNonQueryAsync("INSERT INTO items(id,json) VALUES(@id,@json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
                        new Dictionary<string, object?> { ["id"] = item.Id, ["json"] = Json(item) }, ct);
                }
                return events.Select(x => x.EventId).ToArray();
            }, token);
        } finally { _gate.Release(); }
    }

    public async Task<LedgerItem[]> EvaluateAllAsync(DateTimeOffset now, CancellationToken token = default, HashSet<string>? silentMachines = null) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                var rows = await tx.QueryAsListAsync("SELECT json FROM items ORDER BY id", r => Read<LedgerItem>(r.GetString(0)), cancellationToken: ct);
                var global = await ReadGlobalAsync(tx, ct);
                var parents = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
                var results = new List<LedgerItem>();
                foreach (var row in rows) {
                    var related = ApplyParent(row, row.ParentItemId is { } parentId ? parents.GetValueOrDefault(parentId) : null);
                    var item = await EvaluateAsync(tx, ApplyGlobal(related, global, now), now, ct, silentMachines?.Contains(row.MachineId) == true);
                    // Polling computes a fresh evaluation time without rewriting otherwise unchanged rows.
                    var previous = row with { Current = row.Current is null ? null : row.Current with { EvaluatedAtUtc = item.Current!.EvaluatedAtUtc } };
                    if (Json(previous) != Json(item)) await tx.ExecuteNonQueryAsync("UPDATE items SET json=@json WHERE id=@id",
                        new Dictionary<string, object?> { ["json"] = Json(item), ["id"] = item.Id }, ct);
                    results.Add(item);
                }
                return results.ToArray();
            }, token);
        } finally { _gate.Release(); }
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
        // Exact terminal proof retires stale positive search entries and their implicit waits.
        effectiveOpen = effectiveOpen.Where(id => !envelope.Proofs.TryGetValue(id, out var proof) ||
            proof.Fact.State == "open" || proof.Fact.State != "merged" && PositiveAt(id) is { } positiveAt && proof.Fact.CheckedAtUtc < positiveAt).ToArray();
        if (item.Scope is not null) {
            var taskPr = TaskPullRequests(item, work, envelope, declared, owned, positives, positive, now);
            prsKnown = taskPr.Known;
            effectiveOpen = taskPr.Open;
            hadPrs = work.HadPullRequests || owned.Length > 0;
        }
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
        if (item.Current?.Value != verdict.Value || item.Current.ReasonCode != verdict.ReasonCode || item.Current.EpisodeId != verdict.EpisodeId) {
            var transition = new { item.Id, item.MachineId, item.Provider, verdict,
                evidence = item.Evidence() };
            await tx.ExecuteNonQueryAsync("INSERT INTO transitions(item,recorded,json) VALUES(@item,@recorded,@json)",
                new Dictionary<string, object?> { ["item"] = item.Id, ["recorded"] = now.ToString("O"), ["json"] = Json(transition) }, token);
        }
        return item with { Current = verdict };
    }

    public async Task<object[]> HistoryAsync(string itemId, long after, int limit, CancellationToken token) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var rows = await db.QueryAsListAsync("SELECT ordinal,received,json FROM events WHERE item=@id AND ordinal>@after ORDER BY ordinal LIMIT @limit",
                r => (object)new { ordinal = r.GetInt64(0), receivedAtUtc = r.GetString(1), observation = Read<LedgerEvent>(r.GetString(2)) },
                new Dictionary<string, object?> { ["id"] = itemId, ["after"] = after, ["limit"] = limit }, cancellationToken: token);
            return rows.ToArray();
        } finally { _gate.Release(); }
    }

    public async Task<Dictionary<string, DateTimeOffset>> HeartbeatsAsync(CancellationToken token) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var rows = await db.QueryAsListAsync("SELECT reporter,received FROM heartbeats", r =>
                (r.GetString(0), DateTimeOffset.Parse(r.GetString(1))), cancellationToken: token);
            return rows.ToDictionary(x => x.Item1, x => x.Item2);
        } finally { _gate.Release(); }
    }

    public async Task<Dictionary<string, DateTimeOffset>> EnrollAsync(IEnumerable<string> machines, CancellationToken token) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            foreach (var machine in machines) await db.ExecuteNonQueryAsync("INSERT OR IGNORE INTO machines(id,firstseen) VALUES(@id,@time)",
                new Dictionary<string, object?> { ["id"] = machine, ["time"] = DateTimeOffset.UtcNow.ToString("O") }, token);
            var rows = await db.QueryAsListAsync("SELECT id,firstseen FROM machines", r => (r.GetString(0), DateTimeOffset.Parse(r.GetString(1))), cancellationToken: token);
            return rows.ToDictionary(x => x.Item1, x => x.Item2);
        } finally { _gate.Release(); }
    }

    public async Task<LedgerEvent[]> CoverageAsync(CancellationToken token) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var rows = await db.QueryAsListAsync("SELECT json FROM source_health", r => Read<LedgerEvent>(r.GetString(0)), cancellationToken: token);
            return rows.ToArray();
        } finally { _gate.Release(); }
    }

    public async Task<object[]> DailyAsync(DateOnly date, CancellationToken token) {
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var rows = await db.QueryAsListAsync("""
                SELECT t.ordinal,t.json,r.mark FROM transitions t LEFT JOIN reviews r ON r.transition=t.ordinal
                WHERE t.recorded>=@start AND t.recorded<@end ORDER BY t.ordinal
                """, r => (object)new { transitionId = r.GetInt64(0), transition = JsonSerializer.Deserialize<JsonElement>(r.GetString(1)),
                    mark = r.IsDBNull(2) ? "unmarked" : r.GetString(2) }, new Dictionary<string, object?> {
                    ["start"] = start.ToString("O"), ["end"] = start.AddDays(1).ToString("O") }, cancellationToken: token);
            return rows.ToArray();
        } finally { _gate.Release(); }
    }

    public async Task<object> ReportAsync(DateOnly date, DateTimeOffset now, CancellationToken token) {
        var transitions = await DailyAsync(date, token);
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var start = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var bounds = await db.QueryAsListAsync("SELECT MIN(received),MAX(received),COUNT(*) FROM events WHERE received>=@start AND received<@end",
                r => new { firstReceiptAtUtc = r.IsDBNull(0) ? (DateTimeOffset?)null : DateTimeOffset.Parse(r.GetString(0)),
                    latestReceiptAtUtc = r.IsDBNull(1) ? (DateTimeOffset?)null : DateTimeOffset.Parse(r.GetString(1)), eventCount = r.GetInt64(2) },
                new Dictionary<string, object?> { ["start"] = start.ToString("O"), ["end"] = start.AddDays(1).ToString("O") }, cancellationToken: token);
            var unknowns = await db.QueryAsListAsync("SELECT json FROM items", r => Read<LedgerItem>(r.GetString(0)), cancellationToken: token);
            return new { date, generatedAtUtc = now, coverage = "observed-only", observationCoverage = bounds.Single(),
                unresolvedUnknownsAsOfUtc = now, unresolvedUnknowns = unknowns.Where(i => i.Current?.ReasonCode == "unknown")
                    .Select(i => new { i.Id, i.MachineId, i.Provider, i.Current!.SinceUtc, i.Current.MissingFacts }).ToArray(), transitions };
        } finally { _gate.Release(); }
    }

    public void Dispose() { _database.Dispose(); _gate.Dispose(); }
}

public sealed class EventConflictException : Exception;
