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
    /// <summary>Bounded service-owned requester registrations. Contains token digests only.</summary>
    public Dictionary<string, RequesterEnrollment> Requesters { get; init; } = [];
    /// <summary>Superseded requester identities remain readable and cannot resume an older native source.</summary>
    public Dictionary<string, string> RequesterSuccessors { get; init; } = [];
    /// <summary>Immutable native assignments keyed by child request; generation is verified on every command.</summary>
    public Dictionary<string, CoordinationSessionBinding> Bindings { get; init; } = [];
    /// <summary>Terminal result intents and independent return receipts, atomically persisted with work.</summary>
    public Dictionary<string, CoordinationResultOutbox> ResultOutbox { get; init; } = [];
    /// <summary>Used send identities by request and result generation. Null marks a legacy projection
    /// whose complete history must be recovered from the sending journal before commands run.</summary>
    public Dictionary<string, CoordinationResultAttempts>? ResultDeliveryAttempts { get; set; }
    /// <summary>Independent terminal-helper cleanup jobs, retained after worker capacity releases.</summary>
    public Dictionary<string, CoordinationRetirement> Retirements { get; init; } = [];
    /// <summary>Exact negative exclusion receipts, retained independently from native retirement evidence.</summary>
    public Dictionary<string, CoordinationReconciliation> Reconciliations { get; init; } = [];
}

/// <summary>All send identities reserved for one immutable result generation.</summary>
public sealed record CoordinationResultAttempts(long Generation, HashSet<string> AttemptIds);

/// <summary>An invalid command conflicts with ownership, revision or its immutable intent.</summary>
public sealed class CoordinationConflictException(string reason) : Exception(reason);

/// <summary>Authenticated local enrollment. Tokens remain outside event and command bodies.</summary>
public sealed record CoordinationPrincipal(string Id, string Role, string Token, string[] Projects,
    string? MachineId = null, string? WorkerId = null, bool AllowRequesterEnrollment = false) {
    /// <summary>Assigned only by service authentication, never by configuration or a command body.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal string? RequesterId { get; init; }
}

/// <summary>Separate opt-in command credentials; Ledger observation credentials grant no command authority.</summary>
public sealed record CoordinationConfiguration {
    public required CoordinationPrincipal[] Principals { get; init; }
    public required CoordinationPolicy InitialPolicy { get; init; }
}
