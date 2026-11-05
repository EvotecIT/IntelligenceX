using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static readonly string[] SessionConditions = ["question", "input-unconfirmed", "usage-limit", "complete", "idle", "active", "quiet"];

    private static void ObserveCondition(LedgerItem item, string condition, bool present, DateTimeOffset received) {
        if (present) item.ConditionFirstObservedAtUtc.TryAdd(condition, received);
        else item.ConditionFirstObservedAtUtc.Remove(condition);
    }

    private static void ObserveSessionConditions(LedgerItem item, SessionFact session, DateTimeOffset received) {
        var previous = item.Session;
        var newTurn = previous is not null &&
            (previous.TurnId is not null && session.TurnId is not null && previous.TurnId != session.TurnId ||
             previous.TurnStartedAtUtc.HasValue && session.TurnStartedAtUtc > previous.TurnStartedAtUtc);
        if (newTurn) {
            foreach (var condition in SessionConditions) item.ConditionFirstObservedAtUtc.Remove(condition);
        } else if (previous?.InputId is not null && session.InputId is not null && previous.InputId != session.InputId) {
            item.ConditionFirstObservedAtUtc.Remove("question");
            item.ConditionFirstObservedAtUtc.Remove("input-unconfirmed");
        }
        if (previous?.AccountId is not null && session.AccountId is not null && previous.AccountId != session.AccountId)
            item.ConditionFirstObservedAtUtc.Remove("usage-limit");
        ObserveCondition(item, "question", session.Question, received);
        ObserveCondition(item, "input-unconfirmed", session.InputUnconfirmed, received);
        ObserveCondition(item, "usage-limit", session.UsageLimit, received);
        ObserveCondition(item, "complete", session.Outcome == "completed", received);
        ObserveCondition(item, "idle", session.Outcome == "completed", received);
        if (session.TurnOpen.HasValue) {
            ObserveCondition(item, "active", session.TurnOpen == true, received);
            ObserveCondition(item, "quiet", session.TurnOpen == true, received);
        }
    }

    private static LedgerItem MergeWork(LedgerItem item, WorkFact fact, EvidenceRef evidence) {
        var work = item.ReportedWork ?? new WorkFact();
        var priorWaitScope = work.Wait?.Scope ?? item.WorkDimensionScopes.GetValueOrDefault("wait") ?? work.Scope;
        if (fact.Scope is not null && !LedgerScope.SameTask(fact.Scope, item.Scope)) return item;
        if (fact.Scope is not null) work = work with { Scope = fact.Scope };
        bool New(string dimension, bool supplied, DateTimeOffset? time = null, DateTimeOffset? previousTime = null) {
            if (!supplied) return false;
            var taskDimension = dimension != "claim";
            var priorScope = item.WorkDimensionScopes.GetValueOrDefault(dimension);
            var incomingQualified = LedgerScope.SameTask(fact.Scope, item.Scope);
            var priorQualified = LedgerScope.SameTask(priorScope, item.Scope);
            if (taskDimension && item.Scope is not null && priorQualified && !incomingQualified) return false;
            if (item.WorkDimensionEvidence.TryGetValue(dimension, out var previous) &&
                !(taskDimension && incomingQualified && !priorQualified) &&
                (time ?? evidence.ObservedAtUtc) < (previousTime ?? previous.ObservedAtUtc)) return false;
            item.WorkDimensionEvidence[dimension] = evidence;
            item.WorkDimensionScopes[dimension] = fact.Scope;
            return true;
        }
        if (New("claim", fact.ActiveClaim.HasValue && fact.ClaimsAtUtc.HasValue, fact.ClaimsAtUtc, work.ClaimsAtUtc))
            work = work with { ActiveClaim = fact.ActiveClaim, ClaimsAtUtc = fact.ClaimsAtUtc };
        var qualified = item.Scope is null || LedgerScope.SameTask(fact.Scope, item.Scope);
        if (New("pr-owner", qualified && fact.OwnedPullRequests.Length > 0))
            work = work with { OwnedPullRequests = work.OwnedPullRequests.Concat(fact.OwnedPullRequests).Distinct(StringComparer.Ordinal).ToArray() };
        if (New("pr", fact.PullRequestsKnown == true && fact.PullRequestsAtUtc.HasValue, fact.PullRequestsAtUtc, work.PullRequestsAtUtc))
            work = work with { PullRequestsKnown = true, PullRequestsAtUtc = fact.PullRequestsAtUtc, OpenPullRequests = fact.OpenPullRequests,
                CancelledPullRequests = fact.CancelledPullRequests };
        if (New("goal", fact.GoalHeld.HasValue)) {
            work = work with { GoalHeld = fact.GoalHeld,
                GoalHeldAtUtc = fact.GoalHeldAtUtc ?? (fact.GoalHeld == work.GoalHeld ? work.GoalHeldAtUtc : null) };
            ObserveCondition(item, "goal-held", fact.GoalHeld == true, evidence.ReceivedAtUtc);
        }
        if (New("app", fact.NeedsApp.HasValue)) {
            work = work with { NeedsApp = fact.NeedsApp,
                NeedsAppAtUtc = fact.NeedsAppAtUtc ?? (fact.NeedsApp == work.NeedsApp ? work.NeedsAppAtUtc : null) };
            ObserveCondition(item, "needs-app", fact.NeedsApp == true, evidence.ReceivedAtUtc);
        }
        if (New("queue", fact.QueuedInputs.HasValue)) {
            work = work with { QueuedInputs = fact.QueuedInputs,
                OldestQueuedAtUtc = fact.OldestQueuedAtUtc ?? (fact.QueuedInputs > 0 && work.QueuedInputs > 0 ? work.OldestQueuedAtUtc : null) };
            ObserveCondition(item, "queue-behind-stall", fact.QueuedInputs > 0, evidence.ReceivedAtUtc);
        }
        if (New("stall", fact.Stalled.HasValue)) work = work with { Stalled = fact.Stalled };
        if (fact.Wait is { } wait && ((wait.Scope ?? fact.Scope) is null ||
                LedgerScope.Applies(wait.Scope ?? fact.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId)) &&
            (!wait.Resolved || work.Wait is null || SameWait(wait, work.Wait, wait.Scope ?? fact.Scope, priorWaitScope)) && New("wait", true)) {
            work = work with { Wait = wait.Resolved ? null : work.Wait is { } previous &&
                SameWait(wait, previous, wait.Scope ?? fact.Scope, priorWaitScope) ? previous : wait };
        }
        work = work with { HadPullRequests = item.Work?.HadPullRequests == true || work.HadPullRequests || qualified && fact.HadPullRequests };
        return item with { Work = work, ReportedWork = work,
            WorkEvidence = item.WorkEvidence is null || evidence.ObservedAtUtc >= item.WorkEvidence.ObservedAtUtc ? evidence : item.WorkEvidence };
    }

    private static bool SameWait(WaitFact left, WaitFact right, TaskScope? leftScope, TaskScope? rightScope) =>
        left.Kind == right.Kind && left.ConditionId == right.ConditionId && leftScope == rightScope && left.CandidateRef == right.CandidateRef;
}
