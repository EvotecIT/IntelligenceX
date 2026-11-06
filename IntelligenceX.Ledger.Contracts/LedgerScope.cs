using System.Security.Cryptography;
using System.Text;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Shared metadata identity rules for declaration producers and the service.</summary>
public static class LedgerScope {
    /// <summary>Canonical session key shared by watcher and explicit out-of-process declarations.</summary>
    public static string SessionItemId(string machine, string provider, string session) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(machine + "\n" + provider + "\n" + session))).ToLowerInvariant();

    public static string InputItemId(string parentItemId, string conditionRef) => Id(parentItemId, "input", conditionRef);

    public static bool SameTask(TaskScope? fact, TaskScope? current) => fact is not null && current is not null &&
        fact.TaskId == current.TaskId && fact.Generation == current.Generation;

    public static bool Applies(TaskScope? fact, TaskScope? current, string? turnId) => SameTask(fact, current) &&
        (fact!.TurnId is null || fact.TurnId == turnId);

    /// <summary>A condition retains separate retry episodes within its task, turn and candidate.</summary>
    public static string DeclarationCondition(WorkerDeclaration fact) => Id(fact.Scope.TaskId,
        fact.Scope.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), fact.Scope.TurnId!,
        fact.CandidateRef ?? "", fact.ConditionRef);

    /// <summary>Checkpoint arrival and recording times cannot reactivate an older retry.</summary>
    public static IEnumerable<DeclarationObservation> CurrentDeclarations(IEnumerable<DeclarationObservation> observations) =>
        observations.GroupBy(d => DeclarationCondition(d.Fact))
            .Select(group => group.MaxBy(d => d.EpisodeStartedAtUtc ?? d.Fact.RecordedAtUtc)!);

    /// <summary>Opaque identifiers avoid copying provider paths, titles or tool arguments into events.</summary>
    public static string Id(params string[] values) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\0", values)))).ToLowerInvariant();
}
