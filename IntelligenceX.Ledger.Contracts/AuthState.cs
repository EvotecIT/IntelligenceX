namespace IntelligenceX.Ledger.Contracts;

/// <summary>
/// One provider's sign-in and permission facts on one machine, as its official CLI and files show them. State is
/// signed-in, signed-out, unknown or not-installed. Facts hold the rest as short text: the account, token scopes and
/// source, environment variables that override the stored sign-in, binaries and versions, and when the configuration
/// and credential files were last written. Facts never hold a token or a credential file's contents.
/// </summary>
public sealed record AuthFact(string Provider, string State, DateTimeOffset CheckedAtUtc) {
    public Dictionary<string, string>? Facts { get; init; }
}

/// <summary>One change in a machine's sign-in facts: a state flip, shrunk scopes, a new binary, a rewritten file.</summary>
public sealed record AuthChange(string Machine, string Provider, DateTimeOffset AtUtc, string Field, string? Before, string? After);

/// <summary>One machine: its reporter, the facts its reporter last sent and the agent service's sign-in states.</summary>
public sealed record AuthMachine(string Machine, string Reporter, DateTimeOffset? LastSeenUtc, string? SourceVersion,
    AuthFact[] Providers, SignInFact[] SignIn);

/// <summary>Every enrolled machine's sign-in facts and the latest changes, newest first.</summary>
public sealed record AuthStateView(DateTimeOffset GeneratedAtUtc, AuthMachine[] Machines, AuthChange[] Changes, string[] Coverage);

/// <summary>The bounds a reporter's facts must fit, checked by the service and kept by the collector.</summary>
public static class AuthStateValidation {
    public const int MaximumProviders = 8, MaximumFacts = 24, MaximumKey = 48, MaximumValue = 1024;
    public static readonly string[] States = ["signed-in", "signed-out", "unknown", "not-installed"];

    public static bool Valid(AuthFact[]? facts, DateTimeOffset now) => facts is null ||
        facts.Length <= MaximumProviders && facts.Select(f => f?.Provider).Distinct(StringComparer.Ordinal).Count() == facts.Length &&
        facts.All(f => f is not null && Key(f.Provider) && States.Contains(f.State) && f.CheckedAtUtc <= now.AddMinutes(2) &&
            (f.Facts is null || f.Facts.Count <= MaximumFacts && f.Facts.All(pair => Key(pair.Key) && pair.Value is { Length: <= MaximumValue })));

    private static bool Key(string? value) => value is { Length: > 0 and <= MaximumKey } &&
        value.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '.');
}

/// <summary>The service's health answer: ok or stalled with a reason, when it started and the commit it was built from.</summary>
public sealed record LedgerHealth(string Status, string? Reason, DateTimeOffset? StartedAtUtc) {
    public string? Commit { get; init; }
}
