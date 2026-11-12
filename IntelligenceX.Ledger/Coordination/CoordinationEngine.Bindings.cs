using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    /// <summary>Compares project, branch and commit identity across request and host checkpoints.
    /// Recovery references and pull-request annotations can differ for the same source candidate.</summary>
    internal static bool SameCandidate(WorkCheckpoint a, WorkCheckpoint b) =>
        a.Project == b.Project && a.Branch == b.Branch && a.Commit == b.Commit;

    private bool HasCurrentHostBoot(CoordinationWorker worker) => Id(worker.BootId) &&
        state.Hosts.TryGetValue(worker.MachineId, out var host) && worker.BootId == host.BootId;

    /// <summary>Requires explicit disposable ownership before exposing retirement to native consumers.
    /// Personal or missing enrollments remain held, including explicitly reused sessions.</summary>
    public void RequireTemporaryWorker(CoordinationSessionBinding binding) =>
        Require(state.Workers.TryGetValue(binding.WorkerId, out var worker) && worker.TemporaryWorker,
            "binding-worker-not-temporary");

    private static bool ExplicitlyReusesSession(CoordinationRequest request, CoordinationWorker worker) =>
        request.ReuseSession && request.Kind is "help" or "review" &&
        request.WorkerId == worker.Id && request.MachineId == worker.MachineId;

    private static bool CanUseSession(CoordinationRequest request, CoordinationWorker worker) =>
        worker.TemporaryWorker || ExplicitlyReusesSession(request, worker);

    private void RequireExecutionWorker(CoordinationWork work, CoordinationSessionBinding binding) =>
        Require(state.Workers.TryGetValue(binding.WorkerId, out var worker) && CanUseSession(work.Request, worker) &&
            (worker.TemporaryWorker || work.Assignee == binding.WorkerId && work.Machine == binding.MachineId &&
                worker.MachineId == binding.MachineId && worker.Provider == binding.Provider &&
                worker.SessionId == binding.SessionId && worker.WorkspaceId == binding.WorkspaceId),
            "binding-worker-not-temporary");

    // Retain historical bindings for receipt identity while releasing only consumed explicit personal assignments.
    private bool ReusableAssignmentComplete(CoordinationSessionBinding binding) =>
        state.Workers.TryGetValue(binding.WorkerId, out var worker) && !worker.TemporaryWorker &&
        state.Work.TryGetValue(binding.RequestId, out var previous) && ExplicitlyReusesSession(previous.Request, worker) &&
        previous.Generation == binding.Generation && previous.GrantId == binding.GrantId &&
        previous.Assignee == binding.WorkerId && previous.Machine == binding.MachineId && Terminal.Contains(previous.State) &&
        state.ResultOutbox.TryGetValue(binding.RequestId, out var result) && result.Generation == binding.Generation &&
        result.ResultRef == previous.ResultRef && result.ConsumedAtUtc is not null;

    private bool FreshWorker(CoordinationWorker worker) => HasCurrentHostBoot(worker) &&
        now - worker.ObservedAtUtc <= TimeSpan.FromSeconds(state.Policy.GrantSeconds) && Fresh(worker.MachineId);

    private bool Available(CoordinationWorker worker) => worker.Eligible && worker.UnavailableReason is null && FreshWorker(worker);

    private bool MatchesRecipient(CoordinationReturnBinding recipient, out CoordinationWorker worker,
        bool allowCommitAdvance = false) =>
        state.Workers.TryGetValue(recipient.WorkerId, out worker!) && worker.MachineId == recipient.MachineId &&
        worker.Provider == recipient.Provider && worker.SessionId == recipient.SessionId &&
        (allowCommitAdvance || worker.Candidate is not null && SameCandidate(worker.Candidate, recipient.Candidate));

    private void ValidateReturnBinding(CoordinationRequest request, CoordinationPrincipal caller) {
        if (request.ReturnTo is not { } recipient) return;
        Require(request.Kind is "help" or "review" && Id(recipient.WorkerId) && Id(recipient.MachineId) &&
            Id(recipient.Provider) && Id(recipient.SessionId) && recipient.Candidate is not null &&
            SameCandidate(recipient.Candidate, request.Checkpoint), "return-candidate-conflict");
        Require(caller.WorkerId == recipient.WorkerId && caller.MachineId == recipient.MachineId &&
            MatchesRecipient(recipient, out var worker, RootRequesterRecipient(caller.Id, recipient)) && Available(worker),
            "return-requester-conflict");
        if (recipient.ParentRequestId is null) {
            Require(recipient.ParentGeneration == 0 && !state.Bindings.Values.Any(b => b.WorkerId == recipient.WorkerId &&
                !ReusableAssignmentComplete(b)),
                "parent-binding-required");
            return;
        }
        Require(state.Work.TryGetValue(recipient.ParentRequestId, out var parent) &&
            state.Bindings.TryGetValue(recipient.ParentRequestId, out var binding) &&
            parent.Generation == recipient.ParentGeneration && binding.Generation == recipient.ParentGeneration &&
            binding.WorkerId == recipient.WorkerId && binding.SessionId == recipient.SessionId &&
            parent.Request.GoalId == request.GoalId && parent.State is "submitted" or "started" &&
            SameCandidate(binding.Candidate, request.Checkpoint), "parent-binding-conflict");
        Require(request.ReadOnly, "write-help-source-handoff-required");
    }

    /// <summary>Binds an admitted assignment to the exact session enrolled by its authenticated local host.
    /// Personal sessions require explicit host-qualified ReuseSession help/review and remain exempt from retirement.
    /// A creation grant assigns its newly observed worker here without replacing the grant or generation.
    /// Repeating the identical binding is safe; another session, candidate or generation conflicts.</summary>
    public CoordinationSessionBinding BindSession(CoordinationSessionBinding binding, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(binding.RequestId, out var work) && caller.Role == "host" &&
            caller.MachineId == binding.MachineId && work.Machine == binding.MachineId &&
            binding.Generation == work.Generation && binding.GrantId == work.GrantId &&
            (work.Provider is null || work.Provider == binding.Provider) && binding.Candidate is not null &&
            SameCandidate(binding.Candidate, work.Request.Checkpoint), "binding-assignment-conflict");
        var creating = work.Assignee is null && work.Reason == "worker-creation-required" &&
            work.Request.WorkerId is null && work.Request.Kind is "help" or "review" && work.Provider == binding.Provider;
        Require(work.Assignee == binding.WorkerId || creating, "binding-assignment-conflict");
        Require(state.Workers.TryGetValue(binding.WorkerId, out var worker) &&
            worker.MachineId == binding.MachineId && worker.Provider == binding.Provider &&
            Id(binding.SessionId) && worker.SessionId == binding.SessionId &&
            Id(binding.WorkspaceId) && worker.WorkspaceId == binding.WorkspaceId && worker.Candidate is not null &&
            SameCandidate(worker.Candidate, binding.Candidate), "binding-worker-conflict");
        Require(CanUseSession(work.Request, worker), "binding-worker-not-temporary");
        if (state.Bindings.TryGetValue(binding.RequestId, out var prior)) {
            Require(Same(prior, binding), "binding-identity-conflict");
            return prior;
        }
        var goal = state.Goals[work.Request.GoalId];
        Require(work.State == "admitted" && now <= work.RenewByUtc && Available(worker) &&
            binding.WorkerId != work.Request.ReturnTo?.WorkerId &&
            (!creating || work.Stages?.TryGetValue("admitted", out var admittedAt) == true && worker.ObservedAtUtc >= admittedAt) &&
            state.Hosts[binding.MachineId].BootId == work.BootId &&
            !goal.Stopped && !goal.Paused && Authorized(work, goal) && RequesterReady(work) &&
            !state.Work.Values.Any(other => other.Request.Id != binding.RequestId && Occupied.Contains(other.State) &&
                other.Assignee == binding.WorkerId) &&
            !state.Bindings.Values.Any(b => (b.WorkerId == binding.WorkerId ||
                b.MachineId == binding.MachineId && b.Provider == binding.Provider && b.SessionId == binding.SessionId) &&
                !ReusableAssignmentComplete(b)),
            "binding-not-admissible");
        state.Bindings.Add(binding.RequestId, binding);
        Put(work with { Assignee = binding.WorkerId, Reason = null }, "session-bound", caller.Id);
        return binding;
    }

    private void PreserveTerminalLifecycle(CoordinationWork work, string principal) {
        state.Bindings.TryGetValue(work.Request.Id, out var binding);
        if (binding is not null) RequireExecutionWorker(work, binding);
        if (work.Request.ReturnTo is { } recipient && !state.ResultOutbox.ContainsKey(work.Request.Id)) {
            state.ResultOutbox.Add(work.Request.Id, new(work.Request.Id, work.Generation, work.ResultRef!,
                work.Request.Checkpoint, recipient));
            Note(work.Request.Id, "result-intent", principal, work.Generation, work.ResultRef);
        }
        if (binding is not null && state.Workers[binding.WorkerId].TemporaryWorker && !state.Retirements.ContainsKey(work.Request.Id)) {
            state.Retirements.Add(work.Request.Id, new(binding, work.ResultRef!, []));
            Note(work.Request.Id, "retirement-required", principal, work.Generation, work.ResultRef);
        }
    }
}
