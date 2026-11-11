using IntelligenceX.Ledger.Contracts;
using static IntelligenceX.Ledger.Coordination.CoordinationValidation;

namespace IntelligenceX.Ledger.Coordination;

public sealed partial class CoordinationEngine {
    private bool DescendantsResolved(CoordinationSessionBinding binding) =>
        state.Work.Values.Where(w => w.Request.ReturnTo?.ParentRequestId == binding.RequestId &&
            w.Request.ReturnTo.ParentGeneration == binding.Generation).All(w => AcceptedUnadmittedCancellation(w) ||
                Terminal.Contains(w.State) &&
                state.ResultOutbox.TryGetValue(w.Request.Id, out var result) && result.ConsumedAtUtc is not null &&
                (!state.Bindings.TryGetValue(w.Request.Id, out var childBinding) ||
                 (state.Retirements.TryGetValue(w.Request.Id, out var child) && child.CompletedAtUtc is not null ||
                  ReusableAssignmentComplete(childBinding) && DescendantsResolved(childBinding))));

    /// <summary>Records independently verified preservation, archive, descendant, process and workspace receipts
    /// for one terminal helper. Waiting requires a named external condition; only a qualified resume receipt clears it.
    /// Final verification rechecks descendants and source safety. This helper's own result consumption is independent.
    /// Child results must be consumed before their return destination retires.</summary>
    public CoordinationRetirement Retire(CoordinationRetirementReceipt receipt, CoordinationPrincipal caller) {
        var binding = receipt.Binding;
        Require(state.Retirements.TryGetValue(binding.RequestId, out var job) &&
            Same(job.Binding, binding) && caller.Role == "host" && caller.MachineId == binding.MachineId &&
            state.Work.TryGetValue(binding.RequestId, out var work) && work.Generation == binding.Generation &&
            work.GrantId == binding.GrantId && Terminal.Contains(work.State) && work.ResultRef == job.ResultRef &&
            Id(receipt.EvidenceRef), "retirement-owner-conflict");
        RequireTemporaryWorker(binding);
        Require(receipt.Stage is "archived" or "source-preserved" or "descendants-resolved" or "processes-released" or
            "workspace-released" or "verified" or "waiting" or "resume", "invalid-retirement-stage");
        if (receipt.Stage is "source-preserved" or "verified")
            Require(!receipt.Dirty && !receipt.Unpublished, "retirement-source-unsafe");
        if (job.Evidence.TryGetValue(receipt.Stage, out var evidence) && evidence == receipt.EvidenceRef &&
            receipt.Stage is not ("waiting" or "resume")) return job;
        Require(job.CompletedAtUtc is null, "retirement-already-complete");
        var steps = new Dictionary<string, string>(job.Evidence);
        if (receipt.Stage == "waiting") {
            Require(Id(receipt.WaitingOn), "retirement-wait-condition-required");
            job = job with { WaitingOn = receipt.WaitingOn };
        } else if (receipt.Stage == "resume") {
            Require(job.WaitingOn is not null && receipt.WaitingOn == job.WaitingOn, "retirement-resume-condition-conflict");
            job = job with { WaitingOn = null };
        } else {
            Require(job.WaitingOn is null || receipt.Stage is "archived" or "source-preserved" or "descendants-resolved" or
                "processes-released", "retirement-waiting");
            if (receipt.Stage is "descendants-resolved" or "workspace-released" or "verified")
                Require(DescendantsResolved(binding), "retirement-descendants-unresolved");
            if (receipt.Stage is "workspace-released" or "verified")
                Require(steps.ContainsKey("source-preserved") && steps.ContainsKey("descendants-resolved") &&
                    steps.ContainsKey("processes-released"), "retirement-release-incomplete");
            if (receipt.Stage == "verified") {
                Require(steps.ContainsKey("archived") && steps.ContainsKey("workspace-released"), "retirement-verification-incomplete");
                job = job with { CompletedAtUtc = now };
            }
        }
        steps[receipt.Stage] = receipt.EvidenceRef;
        job = job with { Evidence = steps };
        state.Retirements[binding.RequestId] = job;
        Note(binding.RequestId, "retirement-" + receipt.Stage, caller.Id, binding.Generation, receipt.EvidenceRef);
        Schedule();
        return job;
    }
}
