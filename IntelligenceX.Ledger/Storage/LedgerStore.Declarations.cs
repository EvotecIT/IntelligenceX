using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static LedgerItem AdvanceScope(LedgerItem item, TaskScope? scope, EvidenceRef evidence) {
        if (scope is null) return item;
        if (item.Scope is { } prior) {
            if (scope.Generation < prior.Generation) return item;
            if (scope.Generation == prior.Generation) {
                if (scope.TaskId != prior.TaskId) throw new EventConflictException();
                return item;
            }
        }
        var sameTurn = scope.TurnId is not null && scope.TurnId == item.Session?.TurnId;
        return item with {
            Scope = scope, ScopeEvidence = evidence, Supersession = null, ReportedWork = null, Work = null, WorkEvidence = null,
            WorkDimensionEvidence = [], WorkDimensionScopes = [], ImplicitReviewWait = null, InferredReviewWaits = [], Declarations = [], Control = null,
            ConditionFirstObservedAtUtc = item.ConditionFirstObservedAtUtc.Where(pair => sameTurn && SessionConditions.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            ControlEvidence = null, Safety = null, SafetyEvidence = null,
            LinkedPullRequestIds = [], PositivePullRequests = [], RepositoryBranchKeys = [], CandidateRef = null, CandidateAtUtc = null, CandidateEvidence = null,
            Session = sameTurn ? item.Session : null, SessionEvidence = sameTurn ? item.SessionEvidence : null
        };
    }

    private static async Task<LedgerItem> ApplyDeclaredAsync(SQLiteAsyncSession tx, LedgerItem item,
        LedgerEvent observation, EvidenceRef evidence, CancellationToken token) {
        if (observation.Declaration is { } declaration) {
            var json = Json(declaration);
            var parameters = new Dictionary<string, object?> { ["item"] = item.Id, ["id"] = declaration.Id, ["json"] = json };
            var prior = await tx.ExecuteScalarAsync("SELECT json FROM declaration_receipts WHERE item=@item AND id=@id", parameters, token);
            if (prior is string receipt) {
                if (receipt != json) throw new EventConflictException();
                return item;
            }
            await tx.ExecuteNonQueryAsync("INSERT INTO declaration_receipts(item,id,json) VALUES(@item,@id,@json)", parameters, token);
            if (!LedgerScope.SameTask(declaration.Scope, item.Scope)) return item;
            var condition = LedgerScope.DeclarationCondition(declaration);
            // Upgrade a previously persisted single-episode condition without losing its origin.
            if (item.Declarations.Remove(condition, out var legacy))
                item.Declarations[LedgerScope.Id(condition, legacy.Fact.EpisodeId)] = legacy;
            var key = LedgerScope.Id(condition, declaration.EpisodeId);
            item.Declarations.TryGetValue(key, out var previous);
            if (previous is not null && declaration.RecordedAtUtc == previous.Fact.RecordedAtUtc && declaration != previous.Fact) {
                // Two different facts cannot establish an ordering by their arrival or arbitrary identifiers.
                throw new EventConflictException();
            }
            var origin = previous?.EpisodeStartedAtUtc ?? previous?.Fact.RecordedAtUtc;
            var earlier = origin is null || declaration.RecordedAtUtc < origin;
            var incomingOrigin = earlier ? declaration.RecordedAtUtc : origin!.Value;
            if (item.Declarations.Any(pair => pair.Key != key && LedgerScope.DeclarationCondition(pair.Value.Fact) == condition &&
                (pair.Value.EpisodeStartedAtUtc ?? pair.Value.Fact.RecordedAtUtc) == incomingOrigin))
                throw new EventConflictException();
            // Resolution is terminal for this occurrence, even when an older opening/checkpoint arrives later.
            var replace = previous is null || declaration.Resolved && !previous.Fact.Resolved ||
                declaration.Resolved == previous.Fact.Resolved && declaration.RecordedAtUtc > previous.Fact.RecordedAtUtc;
            item.Declarations[key] = new(replace ? declaration : previous!.Fact, replace ? evidence : previous!.Evidence,
                earlier ? declaration.RecordedAtUtc : origin, earlier ? evidence : previous!.EpisodeEvidence ?? previous.Evidence);
            var candidates = LedgerScope.CurrentDeclarations(item.Declarations.Values).Where(d => d.Fact.CandidateRef is not null &&
                LedgerScope.Applies(d.Fact.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId)).ToArray();
            var candidate = candidates.MaxBy(d => d.EpisodeStartedAtUtc ?? d.Fact.RecordedAtUtc);
            if (candidate is not null) {
                var started = candidate.EpisodeStartedAtUtc ?? candidate.Fact.RecordedAtUtc;
                if (candidates.Any(d => (d.EpisodeStartedAtUtc ?? d.Fact.RecordedAtUtc) == started &&
                    d.Fact.CandidateRef != candidate.Fact.CandidateRef)) throw new EventConflictException();
                item = item with { CandidateRef = candidate.Fact.CandidateRef, CandidateAtUtc = started,
                    CandidateEvidence = candidate.EpisodeEvidence ?? candidate.Evidence };
            }
        }
        if (observation.Control is { } control && LedgerScope.SameTask(control.Scope, item.Scope)) {
            if (item.Control is { } previous && control.AtUtc == previous.AtUtc && control != previous)
                throw new EventConflictException();
            if (item.Control is null || control.AtUtc > item.Control.AtUtc)
                item = item with { Control = control, ControlEvidence = evidence };
        }
        if (observation.Safety is { } safety && LedgerScope.SameTask(safety.Scope, item.Scope)) {
            if (item.Safety is { } previous && safety.AtUtc == previous.AtUtc && safety != previous)
                throw new EventConflictException();
            if (item.Safety is null || safety.AtUtc > item.Safety.AtUtc)
                item = item with { Safety = safety, SafetyEvidence = evidence };
        }
        return item;
    }
}
