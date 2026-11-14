using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Versioned local policy. Units describe admitted work, not unobserved system load.</summary>
public sealed record CoordinationPolicy(long Revision, MachineCapacity[] Machines, int MaximumQueued = 2000,
    int GrantSeconds = 120, bool Enabled = true) {
    public SessionPolicy Session { get; set; } = new();
    public ResourceLeasePolicy Lease { get; set; } = new();
    public BenchmarkPolicy Bench { get; set; } = new();
    public MachineLoadPolicy Load { get; set; } = new();
    public Dictionary<string, string[]> Routing { get; set; } = new(StringComparer.Ordinal) {
        ["engine"] = ["claude", "codex"], ["visual"] = ["claude"],
        ["ciLoop"] = ["codex"], ["mergeLoop"] = ["codex"], ["review"] = ["claude", "codex"]
    };
    public Dictionary<string, string> Priority { get; set; } = new(StringComparer.Ordinal) {
        ["benchmark"] = "normal", ["build"] = "normal", ["test"] = "normal"
    };
}

/// <summary>Distinct machine reservation capacity; processor identifiers come from qualified machine observations.
/// AllowedCreateProviders explicitly permits reserving native worker creation; null or empty disables creation.</summary>
public sealed record MachineCapacity(string Id, int AgentSlots, int BuildSlots, int CpuUnits, long MemoryMiB,
    string[] Capabilities, int[]? BenchmarkProcessors = null, bool Enabled = true, string[]? AllowedCreateProviders = null) {
    /// <summary>Test capacity defaults to the configured build capacity.</summary>
    public int? TestSlots { get; init; }
}

/// <summary>Requested admission; an exclusive benchmark occupies the admitted host until verified release.</summary>
public sealed record ResourceDemand(int AgentSlots = 0, int BuildSlots = 0, int CpuUnits = 0, long MemoryMiB = 0,
    bool Exclusive = false, int[]? Processors = null);

/// <summary>Portable source and recovery references; credentials and native transcripts never belong here.</summary>
public sealed record WorkCheckpoint(string Project, string Branch, string Commit, string? PullRequest,
    string CheckpointRef, string NextActionRef, string? PredecessorRequest = null);

/// <summary>One durable intent with an immutable caller-supplied identity for acknowledgement loss.
/// ReuseSession selects an enrolled existing session with no creation fallback. Help requires WorkerId and MachineId.
/// Read-only review permits optional WorkerId and MachineId selectors; RequiredProvider is mandatory when either is absent.
/// The admitted Assignee and Machine identify the selected session. Non-temporary sessions keep their ordinary workflow
/// and have no retirement job.
/// Another assignment is eligible only after the previous assignment is terminal and its result consumed.
/// The default false retains disposable-helper assignment and historical ownership safeguards.
/// TaskBrief optionally supplies plain task instructions for help/review, bounded to 8192 UTF-8 bytes
/// and containing non-whitespace when supplied. Null preserves legacy requests.</summary>
public sealed record CoordinationRequest(string Id, string GoalId, string Kind, string Capability,
    string? MachineId, string? WorkerId, ResourceDemand Resources, WorkCheckpoint Checkpoint,
    string[] Dependencies, long ControlRevision, string AuthorizationRef, string? RequiredProvider = null,
    bool ReadOnly = false, CoordinationReturnBinding? ReturnTo = null, bool ReuseSession = false,
    string? TaskBrief = null);

/// <summary>Service-owned goal controls survive restarts. Revision is the metadata compare-and-swap version;
/// ControlRevision is the authorization generation used by requests. Ordinary checkpoints do not change it.
/// Zero denotes a legacy projection whose control generation is its Revision.</summary>
public sealed record CoordinationGoal(string Id, string Owner, long Revision, bool Paused, bool Stopped,
    string AuthorizationRef, string[] AcceptanceCriteria, string? ResultRef = null, bool Accepted = false,
    long ControlRevision = 0);

/// <summary>Current request and independent delivery receipts. A lease deadline alone never releases occupancy.
/// Provider records the selected route, including an admitted creation grant whose Assignee remains unset until binding.
/// AuthorizedControlRevision retains an explicit operator reauthorization without changing the original request.</summary>
public sealed record CoordinationWork(CoordinationRequest Request, string Requester, long Order,
    string State, DateTimeOffset RequestedAtUtc, long Generation = 0, string? Machine = null,
    string? Assignee = null, string? GrantId = null, DateTimeOffset? RenewByUtc = null,
    string? BootId = null, string? ExecutionId = null, string? ResultRef = null,
    string? Reason = null, Dictionary<string, DateTimeOffset>? Stages = null, string? Provider = null,
    long? AuthorizedControlRevision = null);

/// <summary>Current host's qualified negative native observation for one stranded grant. OriginalBootId names
/// the grant; CurrentBootId must match a fresh host enrollment. NativeExclusionVerified must explicitly be true;
/// timeout, reboot alone, missing response and unknown evidence are insufficient. Disposition is never-created
/// only for an untouched unbound creation grant, or execution-excluded. A created native worker requires its
/// exact Binding even if ordinary binding expired; exclusion never asserts source/workspace/process retirement.
/// ResultRef identifies a substantive recovery result separately from the exclusion EvidenceRef.</summary>
public sealed record CoordinationReconciliation(string RequestId, long Generation, string GrantId,
    string OriginalBootId, string CurrentBootId, DateTimeOffset ObservedAtUtc, string Disposition,
    string EvidenceRef, string ResultRef, bool NativeExclusionVerified = false,
    CoordinationSessionBinding? Binding = null, string? ExecutionId = null);

/// <summary>Operator authorization for exact pending work after Resume. ExpectedControlRevision is the work's
/// effective authorization, ControlRevision is the current goal control generation. Only queued work with no
/// grant, or terminal results whose delivery is still intent/held, qualify. Original requests stay immutable;
/// admitted, started, reconcile, sending and uncertain operations cannot be replayed by this command.</summary>
public sealed record CoordinationReauthorization(string RequestId, long Generation, long ExpectedControlRevision,
    long ControlRevision, string EvidenceRef);

/// <summary>Original-requester cancellation of an unadmitted generation-zero request. ReasonRef is a bounded
/// identifier. Replays retain the same expected generation and reason; the immutable request is preserved.</summary>
public sealed record CoordinationCancellation(
    [property: System.Text.Json.Serialization.JsonRequired] long ExpectedGeneration,
    [property: System.Text.Json.Serialization.JsonRequired] string ReasonRef);

/// <summary>Exact host identity and registered execution. PID without start/boot identity is insufficient.</summary>
public sealed record ExecutionReceipt(string GrantId, long Generation, string BootId, string ExecutionId,
    string? ResultRef = null, bool? Succeeded = null, bool ExclusionVerified = false);

/// <summary>Qualified machine observation. The service does not launch programs or change tool workflows.
/// CreatableProviders lists only currently observed eligible creation routes; unknown/unavailable routes are omitted.</summary>
public sealed record CoordinationHost(string MachineId, string BootId, DateTimeOffset ObservedAtUtc,
    string[] Capabilities, int[]? Processors = null, string[]? CreatableProviders = null);

/// <summary>Host-enrolled worker eligibility for bounded requested execution. Known unavailable or provider-limit
/// conditions must be reported as ineligible with an explicit reason. Native providers enforce their own limits.
/// enrollment does not require quota or authentication probes.
/// Session, workspace and TemporaryWorker category are immutable for this worker id.
/// TemporaryWorker is explicit host attestation that this session is a disposable helper eligible for native
/// assignment, binding and retirement. Requester/personal sessions default false and may receive results or
/// execute explicitly named ReuseSession help/review without becoming disposable.
/// BootId attests the observed host boot and must match its enrollment for eligibility; null means unknown.</summary>
public sealed record CoordinationWorker(string Id, string MachineId, string Provider, bool Eligible,
    string? UnavailableReason, DateTimeOffset ObservedAtUtc, string? SessionId = null, string? WorkspaceId = null,
    WorkCheckpoint? Candidate = null, string? BootId = null, bool TemporaryWorker = false);

/// <summary>Original enrolled requester and exact candidate. Parent request/generation identifies the spawning
/// attempt when the requester is itself a helper; a root requester leaves these unset.</summary>
public sealed record CoordinationReturnBinding(string WorkerId, string MachineId, string Provider, string SessionId,
    WorkCheckpoint Candidate, string? ParentRequestId = null, long ParentGeneration = 0);

/// <summary>Host-attested native session bound to one admitted assignment, never inferred from a transport ACK.
/// WorkspaceId is an opaque managed-workspace identity, not permission to delete a path.</summary>
public sealed record CoordinationSessionBinding(string RequestId, long Generation, string GrantId, string WorkerId,
    string MachineId, string Provider, string SessionId, string WorkspaceId, WorkCheckpoint Candidate);

/// <summary>One substantive result per request generation. Candidate, result and original recipient are immutable.
/// Intent is pending delivery; sending and uncertain require reconciliation before any new submission.
/// ConsumedAtUtc records explicit requester consumption independently of transport submission and worker retirement.</summary>
public sealed record CoordinationResultOutbox(string RequestId, long Generation, string ResultRef,
    WorkCheckpoint Candidate, CoordinationReturnBinding ReturnTo, string State = "intent",
    string? AttemptId = null, string? DeliveryHost = null, string? EvidenceRef = null, string? HeldOn = null,
    DateTimeOffset? ConsumedAtUtc = null);

/// <summary>Receipt for one previously persisted delivery attempt. Not-submitted requires qualified negative
/// reconciliation evidence; a timeout or missing response must instead be recorded as uncertain.</summary>
public sealed record CoordinationDeliveryReceipt(string AttemptId, string Stage, string EvidenceRef);

/// <summary>Independent durable cleanup obligation for the exact terminal helper. Completed steps survive holds
/// and restarts. A protected primary waits on a named condition without retaining an active worker slot.
/// Revision supports conditional receipts when multiple callers can add or clear a hold.</summary>
public sealed record CoordinationRetirement(CoordinationSessionBinding Binding, string ResultRef,
    Dictionary<string, string> Evidence, string? WaitingOn = null, DateTimeOffset? CompletedAtUtc = null,
    long Revision = 0);

/// <summary>Qualified host evidence for one retirement step. Source and final verification fail closed unless
/// both dirty and unpublished flags are explicitly false. ExpectedRevision conditionally records a new step
/// against the observed job. An identical stored stage acknowledgement remains replayable.</summary>
public sealed record CoordinationRetirementReceipt(CoordinationSessionBinding Binding, string Stage,
    string EvidenceRef, bool Dirty = true, bool Unpublished = true, string? WaitingOn = null,
    long? ExpectedRevision = null);

public sealed record CoordinationChange(long Sequence, string RequestId, string Stage, DateTimeOffset AtUtc,
    string Principal, long Generation, string? EvidenceRef = null) {
    /// <summary>Changed policy values retain their path and the original JSON values.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PolicyValueChange[]? PolicyChanges { get; init; }
}

public sealed record MachineAdmission(string Id, ResourceDemand Used, MachineCapacity Capacity, bool Fresh);
public sealed record CoordinationSummary(DateTimeOffset AsOfUtc, long PolicyRevision,
    Dictionary<string, int> Counts, MachineAdmission[] Machines, CoordinationWork[] Items, bool Truncated);
