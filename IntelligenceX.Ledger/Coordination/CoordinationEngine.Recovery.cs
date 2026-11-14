using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    private static long ControlRevision(CoordinationGoal goal) => goal.ControlRevision > 0 ? goal.ControlRevision : goal.Revision;
    private static long WorkControlRevision(CoordinationWork work) => work.AuthorizedControlRevision ?? work.Request.ControlRevision;
    private static bool Authorized(CoordinationWork work, CoordinationGoal goal) =>
        WorkControlRevision(work) == ControlRevision(goal) && work.Request.AuthorizationRef == goal.AuthorizationRef;

    /// <summary>Authorizes one exact pending request under the current resumed goal. This cannot clear a goal
    /// hold, reactivate any grant or retry a persisted native send. Metadata checkpoints need no reauthorization.</summary>
    public CoordinationWork Reauthorize(CoordinationReauthorization authorization, CoordinationPrincipal caller) {
        Require(caller.Role == "operator", "reauthorization-role-denied");
        Require(state.Work.TryGetValue(authorization.RequestId, out var work) &&
            work.Generation == authorization.Generation && Id(authorization.EvidenceRef), "reauthorization-request-conflict");
        var goal = state.Goals[work.Request.GoalId];
        Require(!goal.Paused && !goal.Stopped && !goal.Accepted &&
            authorization.ControlRevision == ControlRevision(goal) &&
            work.Request.AuthorizationRef == goal.AuthorizationRef, "reauthorization-goal-held");
        Require(work.State == "queued" && work.Generation == 0 && work.GrantId is null && work.ExecutionId is null ||
            Terminal.Contains(work.State) && state.ResultOutbox.TryGetValue(work.Request.Id, out var result) &&
            result.Generation == work.Generation && result.ConsumedAtUtc is null && result.State is "intent" or "held",
            "reauthorization-native-operation-held");
        Require(WorkControlRevision(work) == authorization.ExpectedControlRevision, "reauthorization-control-conflict");
        Require(authorization.ControlRevision > authorization.ExpectedControlRevision, "reauthorization-not-advanced");
        Put(work with { AuthorizedControlRevision = authorization.ControlRevision }, "reauthorized", caller.Id,
            authorization.EvidenceRef);
        Schedule();
        return state.Work[work.Request.Id];
    }

    /// <summary>Closes one stranded grant only on a fresh authenticated negative native observation. Never
    /// requeues or launches work. Disposable native sessions keep their independent retirement job and holds.
    /// Explicitly reused personal sessions preserve their binding and result without creating retirement.</summary>
    public CoordinationWork Reconcile(CoordinationReconciliation reconciliation, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(reconciliation.RequestId, out var work) && caller.Role == "host" &&
            caller.MachineId == work.Machine && work.Generation == reconciliation.Generation &&
            work.GrantId == reconciliation.GrantId && Id(reconciliation.GrantId) &&
            work.BootId == reconciliation.OriginalBootId && Id(reconciliation.OriginalBootId) &&
            work.ExecutionId == reconciliation.ExecutionId, "reconciliation-grant-conflict");
        Require(reconciliation.NativeExclusionVerified && Id(reconciliation.EvidenceRef) && Id(reconciliation.ResultRef) &&
            reconciliation.Disposition is "never-created" or "execution-excluded", "reconciliation-exclusion-required");
        if (state.Reconciliations.TryGetValue(work.Request.Id, out var prior)) {
            Require(Same(prior, reconciliation), "reconciliation-receipt-conflict");
            return work;
        }
        Require(state.Hosts.TryGetValue(work.Machine!, out var host) && host.BootId == reconciliation.CurrentBootId &&
            Fresh(work.Machine!) && reconciliation.ObservedAtUtc >= host.ObservedAtUtc &&
            reconciliation.ObservedAtUtc <= now.AddSeconds(10) && now - reconciliation.ObservedAtUtc <= TimeSpan.FromSeconds(30),
            "reconciliation-host-conflict");
        var goal = state.Goals[work.Request.GoalId];
        Require(Occupied.Contains(work.State) && (work.State == "reconcile" || now > work.RenewByUtc ||
            host.BootId != work.BootId || goal.Paused || goal.Stopped || !Authorized(work, goal)),
            "reconciliation-not-required");
        state.Bindings.TryGetValue(work.Request.Id, out var binding);
        if (reconciliation.Disposition == "never-created") {
            Require(work.Request.Kind is "help" or "review" && work.Request.WorkerId is null && work.Provider is not null &&
                work.Assignee is null && work.ExecutionId is null && binding is null && reconciliation.Binding is null,
                "reconciliation-native-exists");
        } else if (work.Request.Kind is "help" or "review" || binding is not null) {
            var recovered = reconciliation.Binding;
            Require(recovered is not null && recovered.RequestId == work.Request.Id && recovered.Generation == work.Generation &&
                recovered.GrantId == work.GrantId && recovered.MachineId == work.Machine && recovered.Provider == work.Provider &&
                recovered.Candidate is not null && SameCandidate(recovered.Candidate, work.Request.Checkpoint) &&
                recovered.WorkerId != work.Request.ReturnTo?.WorkerId &&
                (work.Assignee is null || recovered.WorkerId == work.Assignee), "reconciliation-binding-conflict");
            if (binding is not null) Require(Same(binding, recovered), "reconciliation-binding-conflict");
            else {
                Require(state.Workers.TryGetValue(recovered.WorkerId, out var worker) && worker.MachineId == recovered.MachineId &&
                    worker.Provider == recovered.Provider && Id(recovered.SessionId) && recovered.SessionId == worker.SessionId &&
                    Id(recovered.WorkspaceId) && recovered.WorkspaceId == worker.WorkspaceId &&
                    !state.Bindings.Values.Any(b => (b.WorkerId == recovered.WorkerId || b.MachineId == recovered.MachineId &&
                        b.Provider == recovered.Provider && b.SessionId == recovered.SessionId) &&
                        !ReusableAssignmentComplete(b)) &&
                    !state.Work.Values.Any(other => other.Request.Id != work.Request.Id && Occupied.Contains(other.State) &&
                        other.Assignee == recovered.WorkerId), "reconciliation-worker-conflict");
                binding = recovered;
            }
        } else Require(reconciliation.Binding is null, "reconciliation-binding-conflict");

        if (binding is not null) Require(state.Workers.TryGetValue(binding.WorkerId, out var owner) &&
            CanUseSession(work.Request, owner), "reconciliation-worker-not-temporary");
        if (binding is not null && !state.Workers[binding.WorkerId].TemporaryWorker)
            Require(state.Workers[binding.WorkerId].MachineId == binding.MachineId &&
                state.Workers[binding.WorkerId].Provider == binding.Provider &&
                state.Workers[binding.WorkerId].SessionId == binding.SessionId &&
                state.Workers[binding.WorkerId].WorkspaceId == binding.WorkspaceId, "reconciliation-worker-conflict");

        // All guards precede projection writes, including historical binding recovery after a lost create response.
        if (binding is not null) state.Bindings[work.Request.Id] = binding;
        state.Reconciliations.Add(work.Request.Id, reconciliation);
        Put(work with { State = reconciliation.Disposition == "never-created" ? "cancelled" : "failed",
            Assignee = binding?.WorkerId ?? work.Assignee, ResultRef = reconciliation.ResultRef, RenewByUtc = null,
            Reason = "reconciled-" + reconciliation.Disposition }, "reconciled", caller.Id, reconciliation.EvidenceRef);
        PreserveTerminalLifecycle(state.Work[work.Request.Id], caller.Id);
        Schedule();
        return state.Work[work.Request.Id];
    }
}
