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

    /// <summary>Opaque identifiers avoid copying provider paths, titles or tool arguments into events.</summary>
    public static string Id(params string[] values) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\0", values)))).ToLowerInvariant();
}
