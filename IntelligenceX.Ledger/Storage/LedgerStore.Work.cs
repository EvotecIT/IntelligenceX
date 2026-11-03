using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static void ObserveCondition(LedgerItem item, string condition, bool present, DateTimeOffset received) {
        if (present) item.ConditionFirstObservedAtUtc.TryAdd(condition, received);
        else item.ConditionFirstObservedAtUtc.Remove(condition);
    }

    private static LedgerItem MergeWork(LedgerItem item, WorkFact fact, EvidenceRef evidence) {
        var work = item.ReportedWork ?? new WorkFact();
        bool New(string dimension, bool supplied, DateTimeOffset? time = null, DateTimeOffset? previousTime = null) {
            if (!supplied) return false;
            if (item.WorkDimensionEvidence.TryGetValue(dimension, out var previous) &&
                (time ?? evidence.ObservedAtUtc) < (previousTime ?? previous.ObservedAtUtc)) return false;
            item.WorkDimensionEvidence[dimension] = evidence;
            return true;
        }
        if (New("claim", fact.ActiveClaim.HasValue && fact.ClaimsAtUtc.HasValue, fact.ClaimsAtUtc, work.ClaimsAtUtc))
            work = work with { ActiveClaim = fact.ActiveClaim, ClaimsAtUtc = fact.ClaimsAtUtc };
        if (New("pr", fact.PullRequestsKnown == true && fact.PullRequestsAtUtc.HasValue, fact.PullRequestsAtUtc, work.PullRequestsAtUtc))
            work = work with { PullRequestsKnown = true, PullRequestsAtUtc = fact.PullRequestsAtUtc, OpenPullRequests = fact.OpenPullRequests };
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
        if (fact.Wait is { } wait && (!wait.Resolved || work.Wait is null || wait.ConditionId == work.Wait.ConditionId && wait.Kind == work.Wait.Kind) && New("wait", true)) {
            work = work with { Wait = wait.Resolved ? null : work.Wait?.ConditionId == wait.ConditionId && work.Wait.Kind == wait.Kind ? work.Wait : wait };
        }
        work = work with { HadPullRequests = item.Work?.HadPullRequests == true || work.HadPullRequests || fact.HadPullRequests };
        return item with { Work = work, ReportedWork = work,
            WorkEvidence = item.WorkEvidence is null || evidence.ObservedAtUtc >= item.WorkEvidence.ObservedAtUtc ? evidence : item.WorkEvidence };
    }
}
