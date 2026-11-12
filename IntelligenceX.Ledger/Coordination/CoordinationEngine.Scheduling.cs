using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    private bool Fresh(string machine) => state.Hosts.TryGetValue(machine, out var host) &&
        now - host.ObservedAtUtc <= TimeSpan.FromSeconds(state.Policy.GrantSeconds);
    private ResourceDemand Used(string machine) {
        var occupied = state.Work.Values.Where(w => w.Machine == machine && Occupied.Contains(w.State)).Select(w => w.Request.Resources)
            .Concat(state.Retirements.Values.Where(j => j.Binding.MachineId == machine &&
                !j.Evidence.ContainsKey("processes-released")).Select(j =>
                    state.Work[j.Binding.RequestId].Request.Resources with { AgentSlots = 0 })).ToArray();
        return new(occupied.Sum(d => d.AgentSlots), occupied.Sum(d => d.BuildSlots), occupied.Sum(d => d.CpuUnits),
            occupied.Sum(d => d.MemoryMiB), occupied.Any(d => d.Exclusive), occupied.SelectMany(d => d.Processors ?? []).ToArray());
    }
    private static bool Fits(ResourceDemand demand, MachineCapacity m, ResourceDemand used) =>
        !used.Exclusive && (!demand.Exclusive || used.AgentSlots + used.BuildSlots + used.CpuUnits + used.MemoryMiB == 0) &&
        (long)demand.AgentSlots + used.AgentSlots <= m.AgentSlots && (long)demand.BuildSlots + used.BuildSlots <= m.BuildSlots &&
        (long)demand.CpuUnits + used.CpuUnits <= m.CpuUnits && demand.MemoryMiB <= m.MemoryMiB - used.MemoryMiB &&
        (demand.Processors is null || demand.Processors.All(p => m.BenchmarkProcessors?.Contains(p) == true && used.Processors?.Contains(p) != true));

    private static bool AwaitingCreatedWorker(CoordinationWork work) => Occupied.Contains(work.State) &&
        work.Request.Kind is "help" or "review" && work.Assignee is null && work.Provider is not null;

    private static string? CreationProvider(CoordinationRequest request, MachineCapacity machine, CoordinationHost host) =>
        !request.ReuseSession && request.Kind is "help" or "review" && request.WorkerId is null
            ? (machine.AllowedCreateProviders ?? []).Where(p => host.CreatableProviders?.Contains(p, StringComparer.Ordinal) == true &&
                (request.RequiredProvider is null || request.RequiredProvider == p)).OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
            : null;

    public void Schedule() {
        foreach (var work in state.Work.Values.Where(w => Occupied.Contains(w.State) && w.State != "reconcile").ToArray()) {
            var goal = state.Goals[work.Request.GoalId];
            var bootChanged = state.Hosts.TryGetValue(work.Machine!, out var host) && work.BootId is not null && work.BootId != host.BootId;
            var sourceChanged = state.Bindings.TryGetValue(work.Request.Id, out var currentBinding) &&
                (!state.Workers.TryGetValue(currentBinding.WorkerId, out var currentWorker) || currentWorker.Candidate is null ||
                 !SameCandidate(currentBinding.Candidate, currentWorker.Candidate));
            var requesterHeld = !RequesterReady(work);
            if (now > work.RenewByUtc || bootChanged || sourceChanged || requesterHeld || goal.Stopped || goal.Paused || !Authorized(work, goal))
                Put(work with { State = "reconcile", Reason = bootChanged ? "boot-changed" : sourceChanged ? "worker-source-changed" : requesterHeld ? "requester-held" : goal.Stopped || goal.Paused ? "goal-held" :
                    !Authorized(work, goal) ? "control-changed" : "grant-overdue" }, "reconciliation-required", "service");
        }
        if (!state.Policy.Enabled) return;
        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var work in state.Work.Values.Where(w => w.State == "queued").OrderBy(w => w.Order).ToArray()) {
            var r = work.Request;
            var goal = state.Goals[r.GoalId];
            if (!RequesterReady(work)) {
                state.Work[r.Id] = work with { Reason = "requester-held" }; continue;
            }
            if (goal.Paused || goal.Stopped || goal.Accepted || !Authorized(work, goal)) {
                state.Work[r.Id] = work with { Reason = "goal-held-or-changed" }; continue;
            }
            if (r.Dependencies.Any(id => state.Work[id].State != "succeeded" || state.Work[id].Stages?.ContainsKey("accepted") != true)) {
                state.Work[r.Id] = work with { Reason = "dependency-not-accepted" }; continue;
            }
            var native = r.Kind is "help" or "review";
            var needsWorker = native || r.WorkerId is not null || r.RequiredProvider is not null;
            var workers = state.Workers.Values.Where(w => Available(w) &&
                (r.WorkerId is null || w.Id == r.WorkerId) && (r.RequiredProvider is null || w.Provider == r.RequiredProvider) &&
                (!native || CanUseSession(r, w) && w.SessionId is not null && w.WorkspaceId is not null && w.Candidate is not null &&
                    SameCandidate(w.Candidate, r.Checkpoint)) && w.Id != r.ReturnTo?.WorkerId &&
                !state.Work.Values.Any(other => Occupied.Contains(other.State) && other.Assignee == w.Id) &&
                !state.Work.Values.Any(other => AwaitingCreatedWorker(other) && other.Machine == w.MachineId &&
                    other.Provider == w.Provider && w.Candidate is not null && SameCandidate(other.Request.Checkpoint, w.Candidate)) &&
                !state.Bindings.Values.Any(b => b.WorkerId == w.Id && !ReusableAssignmentComplete(b)))
                .OrderBy(w => w.Id, StringComparer.Ordinal).ToArray();
            if (!r.ReadOnly && state.Work.Values.Any(other => !other.Request.ReadOnly &&
                (Occupied.Contains(other.State) || state.Retirements.TryGetValue(other.Request.Id, out var job) &&
                    !job.Evidence.ContainsKey("workspace-released")) &&
                other.Request.Checkpoint.Project == r.Checkpoint.Project && other.Request.Checkpoint.Branch == r.Checkpoint.Branch)) {
                state.Work[r.Id] = work with { Reason = "branch-occupied" }; continue;
            }
            var candidates = state.Policy.Machines.Where(m => m.Enabled && (r.MachineId is null || m.Id == r.MachineId) &&
                m.Capabilities.Contains(r.Capability) && Fits(r.Resources, m, new()) &&
                Fresh(m.Id) && state.Hosts[m.Id].Capabilities.Contains(r.Capability) &&
                (r.Resources.Processors is null || r.Resources.Processors.All(p => state.Hosts[m.Id].Processors?.Contains(p) == true)) &&
                (!needsWorker || workers.Any(w => w.MachineId == m.Id) || CreationProvider(r, m, state.Hosts[m.Id]) is not null))
                .OrderBy(m => m.Id, StringComparer.Ordinal).ToArray();
            var ready = candidates.Where(m => !blocked.Contains(m.Id) && Fits(r.Resources, m, Used(m.Id))).ToArray();
            var machine = ready.FirstOrDefault(m => !needsWorker || workers.Any(w => w.MachineId == m.Id)) ?? ready.FirstOrDefault();
            if (machine is null) {
                state.Work[r.Id] = work with { Reason = candidates.Length == 0
                    ? needsWorker && workers.Length == 0 ? "worker-unavailable" : "machine-unavailable" : "capacity" };
                // Ready work cannot repeatedly bypass an earlier conflicting request, including an exclusive benchmark.
                foreach (var candidate in candidates) blocked.Add(candidate.Id);
                continue;
            }
            var assignee = needsWorker ? workers.FirstOrDefault(w => w.MachineId == machine.Id) : null;
            var provider = assignee?.Provider ?? CreationProvider(r, machine, state.Hosts[machine.Id]);
            Put(work with { State = "admitted", Machine = machine.Id, Generation = work.Generation + 1,
                GrantId = Guid.NewGuid().ToString("N"), RenewByUtc = now.AddSeconds(state.Policy.GrantSeconds),
                BootId = state.Hosts[machine.Id].BootId,
                Assignee = assignee?.Id, Provider = provider,
                Reason = needsWorker && assignee is null ? "worker-creation-required" : null }, "admitted", "service");
        }
    }

    /// <summary>Records one independently observed execution stage. A verified terminal result can establish
    /// execution identity while earlier submission and start receipts remain unknown.</summary>
    public CoordinationWork Receipt(string id, string stage, ExecutionReceipt receipt, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work), "request-not-found");
        Require(caller.Role == "host" && work!.Machine == caller.MachineId && receipt.GrantId == work.GrantId &&
            receipt.Generation == work.Generation && Id(receipt.ExecutionId) && Id(receipt.BootId) &&
            receipt.BootId == work.BootId && (receipt.ResultRef is null || Id(receipt.ResultRef)), "stale-or-foreign-grant");
        Require(work.ExecutionId is null || work.ExecutionId == receipt.ExecutionId, "execution-identity-conflict");
        Require(state.Hosts.TryGetValue(work.Machine!, out var host) && host.BootId == receipt.BootId, "host-boot-conflict");
        Require(work.Request.Kind is not ("help" or "review") || state.Bindings.TryGetValue(id, out var binding) &&
            binding.Generation == work.Generation && binding.GrantId == work.GrantId &&
            state.Workers.TryGetValue(binding.WorkerId, out var nativeWorker) && CanUseSession(work.Request, nativeWorker) && nativeWorker.Candidate is not null &&
            SameCandidate(binding.Candidate, nativeWorker.Candidate), "native-binding-required");
        if (state.Bindings.TryGetValue(id, out var boundSession)) RequireExecutionWorker(work, boundSession);
        if (Terminal.Contains(work.State)) {
            Require(stage == "terminal" && work.ResultRef == receipt.ResultRef &&
                work.State == (receipt.Succeeded == true ? "succeeded" : "failed"), "terminal-receipt-conflict");
            return work;
        }
        if (stage == "terminal") {
            Require(receipt.Succeeded is not null && receipt.ResultRef is not null &&
                receipt.ExclusionVerified, "terminal-exclusion-required");
            Put(work with { State = receipt.Succeeded.Value ? "succeeded" : "failed", ResultRef = receipt.ResultRef,
                ExecutionId = receipt.ExecutionId, RenewByUtc = null, Reason = null }, "terminal", caller.Id, receipt.ResultRef);
            PreserveTerminalLifecycle(state.Work[id], caller.Id);
        } else {
            Require(stage is "submitted" or "started" or "renew", "invalid-execution-stage");
            var goal = state.Goals[work.Request.GoalId];
            Require(!goal.Stopped && !goal.Paused && Authorized(work, goal) && RequesterReady(work) && work.State != "reconcile" &&
                now <= work.RenewByUtc && Fresh(work.Machine!), "grant-needs-reconciliation");
            Require(stage switch {
                "submitted" => work.State is "admitted" or "submitted",
                "started" => work.State is "submitted" or "started",
                _ => work.State is "admitted" or "submitted" or "started"
            }, "delivery-stage-order");
            Put(work with { State = stage == "renew" ? work.State : stage, ExecutionId = receipt.ExecutionId,
                RenewByUtc = now.AddSeconds(state.Policy.GrantSeconds) }, stage, caller.Id);
        }
        Schedule();
        return state.Work[id];
    }

    public CoordinationWork Accept(string id, string resultRef, CoordinationPrincipal caller) {
        Require(state.Work.TryGetValue(id, out var work) && work.Requester == caller.Id && caller.Role == "client", "accept-owner-conflict");
        Require(Terminal.Contains(work!.State) && work.ResultRef == resultRef, "accept-result-conflict");
        Require(work.Request.ReturnTo is null, "use-result-consumption");
        if (work.Stages?.ContainsKey("accepted") != true) Put(work, "accepted", caller.Id, resultRef);
        Schedule();
        return state.Work[id];
    }
}
