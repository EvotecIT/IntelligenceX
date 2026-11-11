using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Coordination;

/// <summary>Durable scheduler projection, updated only within the store's command transaction.</summary>
public sealed record CoordinationState {
    public CoordinationPolicy Policy { get; set; } = new(1, []);
    public long Sequence { get; set; }
    public Dictionary<string, CoordinationGoal> Goals { get; init; } = [];
    public Dictionary<string, CoordinationWork> Work { get; init; } = [];
    public Dictionary<string, CoordinationHost> Hosts { get; init; } = [];
    public Dictionary<string, CoordinationWorker> Workers { get; init; } = [];
    /// <summary>Immutable native assignments keyed by child request; generation is verified on every command.</summary>
    public Dictionary<string, CoordinationSessionBinding> Bindings { get; init; } = [];
    /// <summary>Terminal result intents and independent return receipts, atomically persisted with work.</summary>
    public Dictionary<string, CoordinationResultOutbox> ResultOutbox { get; init; } = [];
    /// <summary>Independent terminal-helper cleanup jobs, retained after worker capacity releases.</summary>
    public Dictionary<string, CoordinationRetirement> Retirements { get; init; } = [];
    /// <summary>Exact negative exclusion receipts, retained independently from native retirement evidence.</summary>
    public Dictionary<string, CoordinationReconciliation> Reconciliations { get; init; } = [];
}

/// <summary>An invalid command conflicts with ownership, revision or its immutable intent.</summary>
public sealed class CoordinationConflictException(string reason) : Exception(reason);

/// <summary>Authenticated local enrollment. Tokens remain outside event and command bodies.</summary>
public sealed record CoordinationPrincipal(string Id, string Role, string Token, string[] Projects,
    string? MachineId = null, string? WorkerId = null);

/// <summary>Separate opt-in command credentials; Ledger observation credentials grant no command authority.</summary>
public sealed record CoordinationConfiguration {
    public required CoordinationPrincipal[] Principals { get; init; }
    public required CoordinationPolicy InitialPolicy { get; init; }
}
