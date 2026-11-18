namespace IntelligenceX.Ledger.Contracts;

/// <summary>A session asking to be woken once, with its own prompt: when the checks on a pull request finish (Repo and
/// Pr), or after a delay (AfterMinutes). The session must be on the caller's machine. SessionAddress is how a Claude
/// Code session is reached by name, as ListAgents shows it.</summary>
public sealed record CoordinationWakeRequest(string Id, string Prompt, string SessionProvider, string SessionId,
    string? SessionAddress = null, string? Repo = null, int? Pr = null, double? AfterMinutes = null);

/// <summary>One wake. State is waiting, due, delivered, uncertain or cancelled. Outcome says why it became due (the
/// checks' result, the time, or that it expired waiting). Delivery is the host's send into the session, never repeated
/// once sending, submitted or uncertain.</summary>
public sealed record CoordinationWake(string Id, string Owner, string MachineId, string Provider, string SessionId,
    string? Address, string Prompt, string? Repo, int? Pr, DateTimeOffset CreatedAtUtc, DateTimeOffset? DueAtUtc,
    DateTimeOffset ExpiresAtUtc, string State = "waiting", string? Outcome = null, CoordinationDelivery? Delivery = null,
    long Order = 0, DateTimeOffset? ClosedAtUtc = null, double? AfterMinutes = null);

/// <summary>Due wakes a host has to deliver, oldest first.</summary>
public sealed record CoordinationWakePage(CoordinationWake[] Items, long? NextOrder);

/// <summary>Starts (sending), holds or records (submitted, uncertain, not-submitted) one wake delivery.</summary>
public sealed record CoordinationWakeDeliveryCommand(string AttemptId, string Stage, string? EvidenceRef = null);
