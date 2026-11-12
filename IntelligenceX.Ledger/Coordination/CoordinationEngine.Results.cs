using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    private CoordinationResultOutbox Result(string id, long generation) {
        Require(state.ResultOutbox.TryGetValue(id, out var result) && result.Generation == generation &&
            state.Work.TryGetValue(id, out var work) && work.Generation == generation &&
            work.ResultRef == result.ResultRef && SameCandidate(work.Request.Checkpoint, result.Candidate),
            "result-generation-conflict");
        return result;
    }

    private void DeliveryHost(CoordinationResultOutbox result, CoordinationPrincipal caller) =>
        Require(caller.Role == "host" && caller.MachineId == result.ReturnTo.MachineId, "result-host-conflict");

    /// <summary>Persists the exclusive send intent before a destination host invokes its supported client.
    /// A persisted sending/uncertain attempt cannot be retried, including after restart. A new attempt id is
    /// permitted only after explicit not-submitted reconciliation, or release of a pre-submission hold.</summary>
    public CoordinationResultOutbox BeginResultDelivery(string id, long generation, string attemptId,
        CoordinationPrincipal caller) {
        var result = Result(id, generation);
        DeliveryHost(result, caller);
        Require(Id(attemptId) && result.ConsumedAtUtc is null && result.State is "intent" or "held" && result.AttemptId != attemptId,
            "delivery-needs-reconciliation");
        var attempts = state.ResultDeliveryAttempts?.GetValueOrDefault(id);
        Require(attempts is null || attempts.Generation != generation || !attempts.AttemptIds.Contains(attemptId),
            "delivery-needs-reconciliation");
        var work = state.Work[id];
        var goal = state.Goals[work.Request.GoalId];
        Require(!goal.Stopped && !goal.Paused && Authorized(work, goal) && RequesterReady(work) &&
            MatchesRecipient(result.ReturnTo, out var recipient, RootRequesterRecipient(work.Requester, result.ReturnTo)) &&
            Available(recipient), "result-recipient-held");
        if (result.ReturnTo.ParentRequestId is { } parentId) {
            Require(state.Work.TryGetValue(parentId, out var parent) &&
                parent.Generation == result.ReturnTo.ParentGeneration && !Terminal.Contains(parent.State) &&
                SameCandidate(parent.Request.Checkpoint, result.Candidate), "result-parent-held");
        }
        state.ResultDeliveryAttempts ??= [];
        if (attempts is null || attempts.Generation != generation)
            state.ResultDeliveryAttempts[id] = attempts = new(generation, []);
        attempts.AttemptIds.Add(attemptId);
        result = result with { State = "sending", AttemptId = attemptId, DeliveryHost = caller.Id, EvidenceRef = null, HeldOn = null };
        state.ResultOutbox[id] = result;
        Note(id, "result-sending", caller.Id, generation, attemptId);
        return result;
    }

    /// <summary>Records transport evidence without accepting the result. Held is allowed only before a send;
    /// submitted and uncertain refer to the existing attempt. Not-submitted is an explicit negative observation,
    /// never a timeout. Duplicate receipts are idempotent and do not create another actionable continuation.</summary>
    public CoordinationResultOutbox RecordResultDelivery(string id, long generation, CoordinationDeliveryReceipt receipt,
        CoordinationPrincipal caller) {
        var result = Result(id, generation);
        DeliveryHost(result, caller);
        Require(Id(receipt.EvidenceRef) && Id(receipt.AttemptId), "invalid-delivery-receipt");
        if (result.EvidenceRef == receipt.EvidenceRef &&
            (receipt.Stage == "held" && result.State == "held" ||
             result.AttemptId == receipt.AttemptId && result.DeliveryHost == caller.Id &&
             (result.State == receipt.Stage || receipt.Stage == "not-submitted" && result.State == "intent")))
            return result;
        if (receipt.Stage == "held") {
            Require(result.State is "intent" or "held", "delivery-needs-reconciliation");
            result = result with { State = "held", HeldOn = receipt.EvidenceRef, EvidenceRef = receipt.EvidenceRef };
        } else {
            Require(result.AttemptId == receipt.AttemptId && result.DeliveryHost == caller.Id &&
                result.State is "sending" or "uncertain", "delivery-attempt-conflict");
            Require(receipt.Stage is "submitted" or "uncertain" or "not-submitted", "invalid-delivery-stage");
            result = result with { State = receipt.Stage == "not-submitted" ? "intent" : receipt.Stage,
                EvidenceRef = receipt.EvidenceRef };
        }
        state.ResultOutbox[id] = result;
        Note(id, "result-" + receipt.Stage, caller.Id, generation, receipt.EvidenceRef);
        return result;
    }

    /// <summary>Records substantive consumption by the original enrolled requester for this exact result and
    /// candidate. A transport host cannot acknowledge on its behalf. Consumption can reconcile an uncertain send;
    /// it neither completes retirement nor clears goal controls.</summary>
    public CoordinationResultOutbox ConsumeResult(string id, long generation, string resultRef, WorkCheckpoint candidate,
        CoordinationPrincipal caller) {
        var result = Result(id, generation);
        Require(caller.Role == "client" && state.Work[id].Requester == caller.Id &&
            caller.WorkerId == result.ReturnTo.WorkerId && caller.MachineId == result.ReturnTo.MachineId &&
            MatchesRecipient(result.ReturnTo, out _, RootRequesterRecipient(state.Work[id].Requester, result.ReturnTo)) &&
            (caller.RequesterId is not null || caller.Projects.Contains(result.Candidate.Project)) &&
            resultRef == result.ResultRef && candidate is not null && SameCandidate(candidate, result.Candidate),
            "result-consumption-conflict");
        if (result.ConsumedAtUtc is not null) return result;
        result = result with { ConsumedAtUtc = now };
        state.ResultOutbox[id] = result;
        Put(state.Work[id], "accepted", caller.Id, resultRef);
        Note(id, "result-consumed", caller.Id, generation, resultRef);
        Schedule();
        return result;
    }
}
