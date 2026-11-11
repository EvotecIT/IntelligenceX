using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    private bool Unadmitted(CoordinationWork work) => work.Generation == 0 && work.GrantId is null &&
        work.Machine is null && work.Assignee is null && work.Provider is null && work.RenewByUtc is null &&
        work.BootId is null && work.ExecutionId is null && work.ResultRef is null &&
        !state.Bindings.ContainsKey(work.Request.Id) && !state.ResultOutbox.ContainsKey(work.Request.Id) &&
        !state.Reconciliations.ContainsKey(work.Request.Id) && !state.Retirements.ContainsKey(work.Request.Id) &&
        (work.Stages is null || work.Stages.Keys.All(stage => stage is "requested" or "reauthorized" ||
            work.State == "cancelled" && stage is "cancelled" or "cancellation-accepted" or "accepted"));

    private bool AcceptedUnadmittedCancellation(CoordinationWork work) => work.State == "cancelled" &&
        Id(work.Reason) && work.Stages?.ContainsKey("cancelled") == true &&
        work.Stages.ContainsKey("cancellation-accepted") && work.Stages.ContainsKey("accepted") && Unadmitted(work);

    /// <summary>Cancels only untouched queued work on its original requester's explicit acceptance.
    /// Persists cancellation acceptance independently of execution, delivery and retirement lifecycles.
    /// Owner and generation guards also apply to identical retries after acknowledgement loss.</summary>
    public CoordinationWork Cancel(string id, CoordinationCancellation command, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work) && caller.Role == "client" &&
            work.Requester == caller.Id && caller.Projects.Contains(work.Request.Checkpoint.Project),
            "cancel-owner-conflict");
        Require(command.ExpectedGeneration == 0 && work.Generation == command.ExpectedGeneration,
            "cancel-generation-conflict");
        Require(Id(command.ReasonRef), "invalid-cancellation-reason");
        if (work.State == "cancelled") {
            Require(AcceptedUnadmittedCancellation(work) && work.Reason == command.ReasonRef,
                "cancellation-receipt-conflict");
            return work;
        }
        Require(work.State == "queued" && Unadmitted(work), "cancel-requires-unadmitted-request");
        Put(work with { State = "cancelled", Reason = command.ReasonRef }, "cancelled", caller.Id, command.ReasonRef);
        Put(state.Work[id], "cancellation-accepted", caller.Id, command.ReasonRef);
        Put(state.Work[id], "accepted", caller.Id, command.ReasonRef);
        Schedule();
        return state.Work[id];
    }
}
