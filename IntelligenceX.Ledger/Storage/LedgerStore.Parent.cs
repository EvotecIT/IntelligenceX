using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static LedgerItem ApplyParent(LedgerItem item, LedgerItem? parent) {
        if (item.Kind != "input" || parent is null || item.MachineId != parent.MachineId || item.Provider != parent.Provider) return item;
        if (item.Scope is { } scope && parent.Scope is { } current && current.Generation > scope.Generation && parent.ScopeEvidence is { } scopeEvidence)
            return item.Supersession is not null ? item : item with {
                Supersession = new(current, scopeEvidence.ReceivedAtUtc, "first-observation", scopeEvidence, parent.Session?.TurnId)
            };

        var parentTurn = parent.Session?.TurnId;
        if (parentTurn is not null) {
            var declaration = item.Declarations.Values.Where(d => d.Fact.Scope.TurnId == parentTurn &&
                (parent.Scope is null || LedgerScope.SameTask(d.Fact.Scope, parent.Scope))).MaxBy(d => d.Fact.RecordedAtUtc);
            if (declaration is not null && item.Scope?.TurnId != parentTurn) item = item with {
                Scope = declaration.Fact.Scope, ScopeEvidence = declaration.Evidence, Supersession = null,
                Session = item.Session?.TurnId == parentTurn ? item.Session : null
            };
            var childTurn = item.Scope?.TurnId ?? item.Session?.TurnId;
            var boundary = item.Declarations.Values.Select(d => (DateTimeOffset?)(d.EpisodeStartedAtUtc ?? d.Fact.RecordedAtUtc)).Min()
                ?? item.Session?.QuestionAtUtc;
            if (childTurn is not null && childTurn != parentTurn && boundary.HasValue &&
                parent.Session?.TurnStartedAtUtc is { } started && started > boundary && parent.SessionEvidence is { } turnEvidence)
                return item.Supersession is not null ? item : item with {
                    Supersession = new(parent.Scope, started, "source-event", turnEvidence, parentTurn)
                };
        }
        if (LedgerScope.SameTask(item.Scope, parent.Scope) && parent.Control is { } control &&
            LedgerScope.SameTask(control.Scope, parent.Scope))
            item = item with { Control = control, ControlEvidence = parent.ControlEvidence };
        return item;
    }
}
