using static IntelligenceX.Ledger.Coordination.CoordinationValidation;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Everything the live state held for one finished request, kept in the archive table once it leaves.</summary>
public sealed record CoordinationArchiveEntry(string RequestId, DateTimeOffset FinishedAtUtc, CoordinationWork Work,
    CoordinationSessionBinding? Binding, CoordinationResultOutbox? Result, CoordinationRetirement? Retirement,
    CoordinationReconciliation? Reconciliation, CoordinationQuestion[] Questions, CoordinationUsage[] Usage, CoordinationGoal? Goal);

public sealed partial class CoordinationEngine {
    private const int MaximumArchivedPerCommand = 200;
    private bool _archiveChecked;

    /// <summary>Requests moved out of the live state by this command, for the store to write to its archive table.</summary>
    public List<CoordinationArchiveEntry> Archived { get; } = [];

    /// <summary>When a request last changed: its latest stage, consumption or retirement.</summary>
    private DateTimeOffset FinishedAt(CoordinationWork work) => new[] {
        work.Stages?.Values.DefaultIfEmpty(work.RequestedAtUtc).Max() ?? work.RequestedAtUtc,
        state.ResultOutbox.GetValueOrDefault(work.Request.Id)?.ConsumedAtUtc ?? DateTimeOffset.MinValue,
        state.Retirements.GetValueOrDefault(work.Request.Id)?.CompletedAtUtc ?? DateTimeOffset.MinValue
    }.Max();

    private bool Finished(CoordinationWork work) => Terminal.Contains(work.State) && work.Stages?.ContainsKey("accepted") == true &&
        (!state.ResultOutbox.TryGetValue(work.Request.Id, out var result) || result.ConsumedAtUtc is not null) &&
        (!state.Retirements.TryGetValue(work.Request.Id, out var retirement) || retirement.CompletedAtUtc is not null);

    /// <summary>Moves requests that finished more than the policy's retention ago out of the live state, so each command
    /// rewrites only current work. A request another live request still names (dependency, predecessor or parent) stays.
    /// Workers last observed before the cutoff that nothing references go too. At most 200 requests move per command.</summary>
    private void ArchiveFinished() {
        if (_archiveChecked) return;
        _archiveChecked = true;
        var cutoff = now - TimeSpan.FromDays(state.Policy.RetainFinishedDays);
        for (var pass = 0; pass < 4 && Archived.Count < MaximumArchivedPerCommand; pass++) {
            var referenced = state.Work.Values.SelectMany(w => w.Request.Dependencies
                .Append(w.Request.Checkpoint.PredecessorRequest).Append(w.Request.ReturnTo?.ParentRequestId))
                .OfType<string>().ToHashSet(StringComparer.Ordinal);
            var due = state.Work.Values.Where(w => !referenced.Contains(w.Request.Id) && Finished(w) && FinishedAt(w) < cutoff)
                .OrderBy(w => w.Order).Take(MaximumArchivedPerCommand - Archived.Count).ToArray();
            if (due.Length == 0) break;
            foreach (var work in due) Archive(work);
        }
        var workersInUse = state.Bindings.Values.Select(b => b.WorkerId)
            .Concat(state.Work.Values.SelectMany(w => new[] { w.Assignee, w.Request.WorkerId, w.Request.ReturnTo?.WorkerId }))
            .Concat(state.Requesters.Values.Select(r => r.WorkerId)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var worker in state.Workers.Values.Where(w => w.ObservedAtUtc < cutoff && !workersInUse.Contains(w.Id)).ToArray())
            state.Workers.Remove(worker.Id);
    }

    private void Archive(CoordinationWork work) {
        var id = work.Request.Id;
        var questions = state.Questions.Where(pair => pair.Value.RequestId == id).ToArray();
        state.Usage.TryGetValue(id, out var usage);
        state.Goals.TryGetValue(work.Request.GoalId, out var goal);
        state.Work.Remove(id);
        // An ask's own goal goes with it; any other goal stays while it exists.
        var goalLeaves = goal is not null && goal.Id == "ask-" + id && !state.Work.Values.Any(w => w.Request.GoalId == goal.Id);
        if (goalLeaves) state.Goals.Remove(goal!.Id);
        Archived.Add(new(id, FinishedAt(work), work, Take(state.Bindings, id), Take(state.ResultOutbox, id), Take(state.Retirements, id),
            Take(state.Reconciliations, id), questions.Select(pair => pair.Value).OrderBy(q => q.Number).ToArray(),
            usage?.ToArray() ?? [], goalLeaves ? goal : null));
        foreach (var pair in questions) state.Questions.Remove(pair.Key);
        state.Usage.Remove(id);
        state.ResultDeliveryAttempts?.Remove(id);
    }

    /// <summary>Records a request the calling host finished in its local store while the Ledger was unreachable, as an
    /// archived request under the same id. It is never scheduled. Repeating an identical import returns the same answer;
    /// an id the Ledger already has with anything else is a conflict, so local work can never replace or rerun Ledger work.</summary>
    public CoordinationAcknowledgement Import(CoordinationArchiveEntry entry, CoordinationPrincipal caller, CoordinationArchiveEntry? archived) {
        Require(caller.Role == "host" && caller.MachineId is not null, "import-role-denied");
        var id = entry.RequestId;
        Require(CoordinationValidation.Id(id) && entry.Work.Request.Id == id, "invalid-import");
        // A local store admits only its own machine; a request it never admitted has no machine yet.
        Require(entry.Work.Machine is null || entry.Work.Machine == caller.MachineId, "import-machine-conflict");
        Require(entry.Work.Request.MachineId is null || entry.Work.Request.MachineId == caller.MachineId, "import-machine-conflict");
        // The same project scope as a submission: a host can't write history for a project its sessions may not ask about.
        Require(caller.Projects.Contains(entry.Work.Request.Checkpoint.Project, StringComparer.Ordinal), "project-not-authorized");
        Require(Terminal.Contains(entry.Work.State), "import-not-finished");
        Require(!state.Work.ContainsKey(id), "import-conflict");
        if (archived is not null) {
            Require(System.Text.Json.JsonSerializer.Serialize(archived) == System.Text.Json.JsonSerializer.Serialize(entry), "import-conflict");
            return new(true);
        }
        Archived.Add(entry);
        Note(id, "imported", caller.Id, entry.Work.Generation, "local:" + caller.MachineId);
        return new(true);
    }

    private static T? Take<T>(Dictionary<string, T> values, string id) where T : class =>
        values.Remove(id, out var value) ? value : null;
}
