using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Deterministic admission and lifecycle rules shared by every command surface.</summary>
public sealed partial class CoordinationEngine(CoordinationState state, DateTimeOffset now,
    IReadOnlyCollection<CoordinationPrincipal>? configuredPrincipals = null) {
    public List<CoordinationChange> Changes { get; } = [];
    private static readonly HashSet<string> Occupied = ["admitted", "submitted", "started", "reconcile"];
    private static readonly HashSet<string> Terminal = ["succeeded", "failed", "cancelled"];
    private static bool Same<T>(T left, T right) => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private void Note(string id, string stage, string principal, long generation, string? evidence = null) =>
        Changes.Add(new(++state.Sequence, id, stage, now, principal, generation, evidence));
    private void Put(CoordinationWork work, string stage, string principal, string? evidence = null) {
        var stages = new Dictionary<string, DateTimeOffset>(work.Stages ?? []);
        stages.TryAdd(stage, now);
        state.Work[work.Request.Id] = work with { Stages = stages };
        Note(work.Request.Id, stage, principal, work.Generation, evidence);
    }

    public CoordinationGoal SetGoal(CoordinationGoal goal, CoordinationPrincipal caller, long expectedRevision) {
        Require(caller.Role is "client" or "operator", "goal-role-denied");
        RequireRequesterScope(caller, goal.Id, goal.AuthorizationRef, requireDispatch:
            !state.Goals.ContainsKey(goal.Id) || !goal.Accepted && !goal.Paused && !goal.Stopped);
        Require(Id(goal.Id) && Id(goal.AuthorizationRef) && goal.AcceptanceCriteria is { Length: > 0 and <= 64 } &&
            goal.AcceptanceCriteria.All(Id) && (goal.ResultRef is null || Id(goal.ResultRef)), "invalid-goal");
        state.Goals.TryGetValue(goal.Id, out var prior);
        Require((prior?.Revision ?? 0) == expectedRevision, "goal-revision-conflict");
        Require(prior is null || prior.Owner == caller.Id || caller.Role == "operator", "goal-owner-conflict");
        Require(prior is null || caller.Role == "operator" || (!prior.Stopped || goal.Stopped) && (!prior.Paused || goal.Paused),
            "retained-goal-control");
        Require(!goal.Accepted || goal.ResultRef is not null && state.Work.Values.Where(w => w.Request.GoalId == goal.Id)
            .All(w => Terminal.Contains(w.State) && w.Stages?.ContainsKey("accepted") == true),
            "goal-acceptance-incomplete");
        var controlsChanged = prior is not null && (goal.Paused != prior.Paused || goal.Stopped != prior.Stopped ||
            goal.AuthorizationRef != prior.AuthorizationRef || goal.Accepted != prior.Accepted ||
            !goal.AcceptanceCriteria.SequenceEqual(prior.AcceptanceCriteria, StringComparer.Ordinal));
        var next = goal with { Owner = prior?.Owner ?? caller.Id, Revision = expectedRevision + 1,
            ControlRevision = prior is null ? 1 : ControlRevision(prior) + (controlsChanged ? 1 : 0) };
        state.Goals[goal.Id] = next;
        Note(goal.Id, goal.Stopped ? "goal-stopped" : goal.Paused ? "goal-paused" : "goal-updated", caller.Id, next.Revision);
        Schedule();
        return next;
    }

    public CoordinationWork Submit(CoordinationRequest request, CoordinationPrincipal caller) {
        Require(caller.Role == "client", "submit-role-denied");
        Request(request, caller);
        if (state.Work.TryGetValue(request.Id, out var existing)) {
            Require(existing.Requester == caller.Id && Same(existing.Request, request), "request-id-conflict");
            return existing;
        }
        RequireRequesterScope(caller, request.GoalId, request.AuthorizationRef, request.Checkpoint);
        Require(state.Goals.TryGetValue(request.GoalId, out var goal) &&
            ControlRevision(goal) == request.ControlRevision && !goal.Stopped && !goal.Paused && !goal.Accepted &&
            goal.AuthorizationRef == request.AuthorizationRef, "goal-control-conflict");
        ValidateReturnBinding(request, caller);
        Require(goal.Owner == caller.Id || request.ReturnTo?.ParentRequestId is not null, "goal-owner-conflict");
        Require(request.Dependencies.All(state.Work.ContainsKey), "dependency-not-found");
        Require(request.Dependencies.All(id => state.Work[id].Request.Checkpoint.Project == request.Checkpoint.Project), "dependency-project-conflict");
        Require(state.Work.Values.Count(w => w.State == "queued") < state.Policy.MaximumQueued, "queue-full");
        if (request.Checkpoint.PredecessorRequest is { } predecessor) {
            Require(state.Work.TryGetValue(predecessor, out var previous) && Terminal.Contains(previous.State) &&
                previous.Requester == caller.Id && previous.Request.Checkpoint.Project == request.Checkpoint.Project &&
                previous.Request.Checkpoint.Branch == request.Checkpoint.Branch && previous.Request.Checkpoint.Commit == request.Checkpoint.Commit,
                "predecessor-not-reconciled");
        }
        Require(state.Policy.Machines.Any(m => (request.MachineId is null || request.MachineId == m.Id) &&
            m.Capabilities.Contains(request.Capability) && Fits(request.Resources, m, new())), "no-capable-machine");
        var work = new CoordinationWork(request, caller.Id, state.Sequence + 1, "queued", now);
        Put(work, "requested", caller.Id);
        Schedule();
        return state.Work[request.Id];
    }

    public void ObserveHost(CoordinationHost host, CoordinationPrincipal caller) {
        Require(caller.Role == "host" && caller.MachineId == host.MachineId && Id(host.BootId) &&
            state.Policy.Machines.Any(m => m.Id == host.MachineId) && host.Capabilities is { Length: <= 64 } && host.Capabilities.All(Id) &&
            Providers(host.CreatableProviders) &&
            host.ObservedAtUtc <= now.AddSeconds(10) && now - host.ObservedAtUtc <= TimeSpan.FromSeconds(30) &&
            (host.Processors is null || host.Processors.Length <= 4096 && host.Processors.All(p => p >= 0)), "invalid-host-observation");
        if (state.Hosts.TryGetValue(host.MachineId, out var previous)) {
            Require(host.ObservedAtUtc > previous.ObservedAtUtc, "host-observation-out-of-order");
            if (previous.BootId != host.BootId) {
                foreach (var worker in state.Workers.Values.Where(w => w.MachineId == host.MachineId).ToArray())
                    state.Workers[worker.Id] = worker with { Eligible = false, UnavailableReason = "host-restarted" };
            }
        }
        state.Hosts[host.MachineId] = host;
        Schedule();
    }

    /// <summary>Publishes host evidence. Guarded preparation atomically rejects source replacement while
    /// an occupied or unconsumed assignment still owns the worker's current candidate.</summary>
    public void ObserveWorker(CoordinationWorker worker, CoordinationPrincipal caller, bool requireUnassigned = false) {
        ValidateWorkerObservation(worker, caller);
        if (requireUnassigned && state.Workers.TryGetValue(worker.Id, out var prior) && !Same(prior.Candidate, worker.Candidate))
            Require(WorkerAssignment(worker.Id) is null, "worker-assignment-active");
        state.Workers[worker.Id] = worker;
        Schedule();
    }

    private void ValidateWorkerObservation(CoordinationWorker worker, CoordinationPrincipal caller, bool allowDuplicate = false) {
        Require(caller.Role == "host" && caller.MachineId == worker.MachineId && Id(worker.Id) && Id(worker.Provider) &&
            state.Policy.Machines.Any(m => m.Id == worker.MachineId) &&
            (worker.SessionId is null || Id(worker.SessionId)) && (worker.WorkspaceId is null || Id(worker.WorkspaceId)) &&
            (worker.BootId is null || Id(worker.BootId)) &&
            (worker.UnavailableReason is null || Id(worker.UnavailableReason)) && (!worker.Eligible || worker.UnavailableReason is null) &&
            worker.ObservedAtUtc <= now.AddSeconds(10) && now - worker.ObservedAtUtc <= TimeSpan.FromSeconds(30), "invalid-worker-observation");
        Require(!worker.Eligible || HasCurrentHostBoot(worker), "worker-boot-conflict");
        if (worker.Candidate is not null) Checkpoint(worker.Candidate);
        if (state.Workers.TryGetValue(worker.Id, out var previous)) Require(previous.MachineId == worker.MachineId &&
            previous.Provider == worker.Provider && previous.SessionId == worker.SessionId && previous.WorkspaceId == worker.WorkspaceId &&
            previous.TemporaryWorker == worker.TemporaryWorker &&
            (worker.ObservedAtUtc > previous.ObservedAtUtc || allowDuplicate && Same(previous, worker)), "worker-identity-conflict");
        Require(worker.SessionId is null || !state.Workers.Values.Any(w => w.Id != worker.Id &&
            w.MachineId == worker.MachineId && w.Provider == worker.Provider && w.SessionId == worker.SessionId),
            "session-already-enrolled");
    }

    public void ReplacePolicy(CoordinationPolicy policy, CoordinationPrincipal caller, long expectedRevision) {
        Require(caller.Role == "operator", "policy-role-denied");
        Policy(policy);
        Require(expectedRevision == state.Policy.Revision && policy.Revision == expectedRevision + 1, "policy-revision-conflict");
        Require(state.Work.Values.Where(w => Occupied.Contains(w.State)).All(w => policy.Machines.Any(m => m.Id == w.Machine)) &&
            state.Retirements.Values.Where(j => j.CompletedAtUtc is null).All(j => policy.Machines.Any(m => m.Id == j.Binding.MachineId)),
            "occupied-machine-removal");
        state.Policy = policy;
        Note("policy", "policy-updated", caller.Id, policy.Revision);
        Schedule(); // A reduced ceiling blocks new grants; it never terminates existing work.
    }

    /// <summary>Builds one scoped work overview. Machine occupancy retains all admitted reservations.</summary>
    public CoordinationSummary Summary(string? project, int limit = 8, Func<CoordinationWork, bool>? include = null) {
        var work = state.Work.Values.Where(w => (project is null || w.Request.Checkpoint.Project == project) &&
            (include is null || include(w))).OrderBy(w => w.Order).ToArray();
        bool Pending(CoordinationWork w) => !Terminal.Contains(w.State) || w.Stages?.ContainsKey("accepted") != true ||
            state.Retirements.TryGetValue(w.Request.Id, out var retirement) && retirement.CompletedAtUtc is null;
        return new(now, state.Policy.Revision, work.GroupBy(w => w.State).ToDictionary(g => g.Key, g => g.Count()),
            state.Policy.Machines.Select(m => new MachineAdmission(m.Id, Used(m.Id), m, Fresh(m.Id))).ToArray(),
            work.Where(Pending).Take(limit).ToArray(), work.Count(Pending) > limit);
    }
}
