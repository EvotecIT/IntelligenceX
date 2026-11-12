using System.Security.Cryptography;
using System.Text;
using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    private const int MaximumRequesters = 10000;

    /// <summary>Resolves the authoritative worker identity without inventing aliases or observing native state.</summary>
    public CoordinationWorkerStatus WorkerStatus(string provider, string sessionId, CoordinationPrincipal caller) {
        Require(caller.Role == "host" && Id(caller.MachineId), "worker-lookup-role-denied");
        Require(Id(provider) && Id(sessionId), "invalid-worker-lookup");
        var workers = state.Workers.Values.Where(w => w.MachineId == caller.MachineId &&
            w.Provider == provider && w.SessionId == sessionId).Take(2).ToArray();
        Require(workers.Length <= 1, "worker-session-conflict");
        return new(workers.SingleOrDefault());
    }

    /// <summary>Refuses startup or registration when a configured identity would shadow a durable requester.</summary>
    public void ValidateRequesterPrincipals() => Require(configuredPrincipals is null ||
        configuredPrincipals.All(p => !state.Requesters.ContainsKey(p.Id)), "requester-static-id-conflict");

    /// <summary>Registers a hash-only credential for a native requester observed by its explicitly enabled host.
    /// Exact replays are idempotent. Renewals cannot replace the credential or immutable source/goal binding.</summary>
    public RequesterEnrollment EnrollRequester(RequesterEnrollment enrollment, CoordinationPrincipal caller) {
        Require(caller.Role == "host" && caller.AllowRequesterEnrollment, "requester-enrollment-role-denied");
        Require(Id(enrollment.Id) && Id(enrollment.WorkerId) && Id(enrollment.GoalId) && Id(enrollment.SourceRef) &&
            Id(enrollment.AuthorizationRef) && Id(enrollment.BootId) &&
            enrollment.TokenSha256 is { Length: 64 } && enrollment.TokenSha256.All(Uri.IsHexDigit) &&
            enrollment.Sources is { Length: <= 64 } && enrollment.Sources.All(s => s is not null &&
                Id(s.Project) && s.Branch is { Length: > 0 and <= 200 } && !s.Branch.Any(char.IsControl)) &&
            enrollment.Sources.Distinct().Count() == enrollment.Sources.Length, "invalid-requester-enrollment");
        ValidateRequesterPrincipals();
        Require(configuredPrincipals?.Any(p => p.Id == enrollment.Id) != true, "requester-static-id-conflict");
        Require(enrollment.Sources.All(s => caller.Projects.Contains(s.Project, StringComparer.Ordinal)),
            "requester-project-not-authorized");
        var worker = enrollment.WorkerObservation ?? state.Workers.GetValueOrDefault(enrollment.WorkerId);
        Require(worker is not null && worker.Id == enrollment.WorkerId &&
            caller.MachineId == worker.MachineId && Id(worker.SessionId), "requester-worker-conflict");
        Require(RequesterAssignmentMatches(enrollment, worker), "requester-assignment-conflict");
        if (state.Requesters.TryGetValue(enrollment.Id, out var prior)) {
            Require(prior.WorkerId == enrollment.WorkerId && prior.GoalId == enrollment.GoalId &&
                prior.SourceRef == enrollment.SourceRef && prior.AuthorizationRef == enrollment.AuthorizationRef &&
                prior.TokenSha256 == enrollment.TokenSha256, "requester-identity-conflict");
            if (Same(prior, enrollment)) return prior;
            Require(!state.RequesterSuccessors.ContainsKey(enrollment.Id), "requester-source-superseded");
            Require(enrollment.ObservedAtUtc > prior.ObservedAtUtc, "requester-observation-out-of-order");
        } else {
            Require(state.Requesters.Count < MaximumRequesters, "requester-registry-full");
            Require(!state.Requesters.Values.Any(r => string.Equals(r.TokenSha256, enrollment.TokenSha256,
                StringComparison.OrdinalIgnoreCase)), "requester-token-conflict");
            Require(configuredPrincipals?.Any(p => string.Equals(Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(p.Token))), enrollment.TokenSha256, StringComparison.OrdinalIgnoreCase)) != true,
                "requester-token-conflict");
        }
        if (enrollment.WorkerObservation is not null) ValidateWorkerObservation(worker, caller, allowDuplicate: true);
        Require(FreshWorker(worker) && (!enrollment.DispatchAllowed || Available(worker)) && enrollment.BootId == worker.BootId &&
            enrollment.ObservedAtUtc <= now.AddSeconds(10) && now - enrollment.ObservedAtUtc <= TimeSpan.FromSeconds(30),
            "requester-observation-held");
        var siblings = state.Requesters.Values.Where(r => r.Id != enrollment.Id &&
            state.Workers.TryGetValue(r.WorkerId, out var other) && SameNativeSession(worker, other)).ToArray();
        Require(siblings.All(r => r.SourceRef != enrollment.SourceRef), "requester-session-already-enrolled");
        Require(siblings.All(r => r.ObservedAtUtc < enrollment.ObservedAtUtc), "requester-observation-out-of-order");
        if (enrollment.WorkerObservation is not null) state.Workers[worker.Id] = worker;
        foreach (var sibling in siblings.Where(r => !state.RequesterSuccessors.ContainsKey(r.Id))) {
            state.Requesters[sibling.Id] = sibling with { DispatchAllowed = false };
            state.RequesterSuccessors[sibling.Id] = enrollment.Id;
            Note(sibling.Id, "requester-superseded", caller.Id, 0);
        }
        state.Requesters[enrollment.Id] = enrollment;
        Note(enrollment.Id, "requester-enrolled", caller.Id, 0);
        Schedule();
        return enrollment;
    }

    /// <summary>Resolves a scoped client using a constant-time digest comparison. Stale controls retain read access.</summary>
    public CoordinationPrincipal? AuthenticateRequester(string bearer) {
        if (string.IsNullOrEmpty(bearer) || bearer.Length > 4096) return null;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(bearer));
        foreach (var enrollment in state.Requesters.Values) {
            if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(enrollment.TokenSha256))) continue;
            if (!state.Workers.TryGetValue(enrollment.WorkerId, out var worker)) return null;
            return new(enrollment.Id, "client", "", enrollment.Sources.Select(s => s.Project).Distinct(StringComparer.Ordinal).ToArray(),
                worker.MachineId, worker.Id) { RequesterId = enrollment.Id };
        }
        return null;
    }

    private static bool IncludesSource(RequesterEnrollment enrollment, WorkCheckpoint candidate) =>
        enrollment.Sources.Any(s => s.Project == candidate.Project && s.Branch == candidate.Branch);

    private static bool SameNativeSession(CoordinationWorker left, CoordinationWorker right) =>
        left.MachineId == right.MachineId && left.Provider == right.Provider && left.SessionId == right.SessionId;

    private bool RequesterAssignmentMatches(RequesterEnrollment enrollment, CoordinationWorker worker) {
        var sessions = state.Workers.Values.Where(w => SameNativeSession(w, worker)).ToArray();
        var bindings = state.Bindings.Values.Where(b => b.MachineId == worker.MachineId && b.Provider == worker.Provider &&
            b.SessionId == worker.SessionId && !ReusableAssignmentComplete(b)).ToArray();
        if (!sessions.Any(w => w.TemporaryWorker) && bindings.Length == 0) return sessions.All(w => w.Id == worker.Id);
        return bindings.Length == 1 && bindings[0].WorkerId == worker.Id &&
            state.Work.TryGetValue(bindings[0].RequestId, out var parent) && parent.Generation == bindings[0].Generation &&
            parent.GrantId == bindings[0].GrantId && parent.Assignee == worker.Id && parent.Request.GoalId == enrollment.GoalId &&
            parent.State is "submitted" or "started";
    }

    private bool RequesterActive(RequesterEnrollment enrollment) => enrollment.DispatchAllowed &&
        enrollment.Sources.Length > 0 &&
        !state.RequesterSuccessors.ContainsKey(enrollment.Id) &&
        now - enrollment.ObservedAtUtc <= TimeSpan.FromSeconds(state.Policy.GrantSeconds) &&
        state.Workers.TryGetValue(enrollment.WorkerId, out var worker) && Id(worker.SessionId) &&
        worker.BootId == enrollment.BootId && Available(worker) && RequesterAssignmentMatches(enrollment, worker);

    private void RequireRequesterScope(CoordinationPrincipal caller, string goalId, string authorizationRef,
        WorkCheckpoint? candidate = null, bool requireDispatch = true) {
        if (caller.RequesterId is null) return;
        Require(state.Requesters.TryGetValue(caller.RequesterId, out var enrollment) && enrollment.Id == caller.Id &&
            enrollment.GoalId == goalId && enrollment.AuthorizationRef == authorizationRef &&
            (candidate is null || IncludesSource(enrollment, candidate)), "requester-scope-conflict");
        Require(!requireDispatch || RequesterActive(enrollment), "requester-dispatch-held");
    }

    private bool RequesterReady(CoordinationWork work) => !state.Requesters.TryGetValue(work.Requester, out var enrollment) ||
        enrollment.GoalId == work.Request.GoalId && enrollment.AuthorizationRef == work.Request.AuthorizationRef &&
        IncludesSource(enrollment, work.Request.Checkpoint) && RequesterActive(enrollment);

    private bool RootRequesterRecipient(string requester, CoordinationReturnBinding recipient) =>
        recipient.ParentRequestId is null && recipient.ParentGeneration == 0 &&
        state.Requesters.TryGetValue(requester, out var enrollment) && enrollment.WorkerId == recipient.WorkerId &&
        state.Workers.TryGetValue(enrollment.WorkerId, out var worker) && !worker.TemporaryWorker;
}
