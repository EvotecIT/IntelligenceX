namespace IntelligenceX.Ledger.Contracts;

/// <summary>One project and branch authorized by the requester's trusted local host.</summary>
public sealed record RequesterSourceScope(string Project, string Branch);

/// <summary>Host-qualified requester identity and current native goal controls. Only the SHA-256 digest of
/// the private client token crosses this contract. Worker enrollment supplies machine, provider and session.
/// Identity, goal, source, authorization and token digest are immutable. A renewal strictly advances the
/// observation and can update source scopes, boot and dispatch qualification. WorkerObservation optionally supplies
/// the same host's qualified worker observation for one atomic source switch, before admission runs.
/// Lost tokens require explicit recovery.</summary>
public sealed record RequesterEnrollment(string Id, string WorkerId, string GoalId, string SourceRef,
    string AuthorizationRef, RequesterSourceScope[] Sources, string TokenSha256, string BootId,
    DateTimeOffset ObservedAtUtc, bool DispatchAllowed,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    CoordinationWorker? WorkerObservation = null);

/// <summary>Host-only lookup of the existing session enrollment on the authenticated machine.
/// An explicit null worker establishes absence. Assignment retains an occupied grant or an unconsumed
/// session binding so host refreshes preserve the source until its assignment is released.</summary>
public sealed record CoordinationWorkerStatus(
    [property: System.Text.Json.Serialization.JsonRequired] CoordinationWorker? Worker,
    CoordinationWork? Assignment = null);
