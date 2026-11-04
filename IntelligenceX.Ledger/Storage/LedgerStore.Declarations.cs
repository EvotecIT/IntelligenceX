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
            if (!declaration.Resolved && declaration.CandidateRef is not null && LedgerScope.Applies(declaration.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId)) {
                if (declaration.RecordedAtUtc == item.CandidateAtUtc && declaration.CandidateRef != item.CandidateRef)
                    throw new EventConflictException();
                if (item.CandidateAtUtc is null || declaration.RecordedAtUtc > item.CandidateAtUtc)
                    item = item with { CandidateRef = declaration.CandidateRef, CandidateAtUtc = declaration.RecordedAtUtc, CandidateEvidence = evidence };
            }
            var key = LedgerScope.Id(declaration.Scope.TaskId, declaration.Scope.Generation.ToString(), declaration.Scope.TurnId!,
                declaration.CandidateRef ?? "", declaration.ConditionRef);
            item.Declarations.TryGetValue(key, out var previous);
            if (declaration.Resolved && previous is not null && declaration.EpisodeId != previous.Fact.EpisodeId)
                return item;
            if (previous is null ||
                declaration.RecordedAtUtc > previous.Fact.RecordedAtUtc) {
                var sameEpisode = previous?.Fact.EpisodeId == declaration.EpisodeId;
                item.Declarations[key] = new(declaration, evidence,
                    sameEpisode ? previous!.EpisodeStartedAtUtc ?? previous.Fact.RecordedAtUtc : declaration.RecordedAtUtc,
                    sameEpisode ? previous!.EpisodeEvidence ?? previous.Evidence : evidence);
            } else if (declaration.RecordedAtUtc == previous.Fact.RecordedAtUtc && declaration != previous.Fact) {
                // Two different facts cannot establish an ordering by their arrival or arbitrary identifiers.
                throw new EventConflictException();
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
