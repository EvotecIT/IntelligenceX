using System.Text.Json;
using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Closed, metadata-only event envelope. Authentication and transcript content are excluded.</summary>
public sealed record LedgerEvent {
    public int SchemaVersion { get; init; } = 1;
    public required Guid EventId { get; init; }
    public required string ReporterId { get; init; }
    public required Guid Epoch { get; init; }
    public required long Sequence { get; init; }
    public required string MachineId { get; init; }
    public required string ItemId { get; init; }
    public required string Provider { get; init; }
    public required string Source { get; init; }
    public string SourceVersion { get; init; } = "1";
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public SessionFact? Session { get; init; }
    public WorkFact? Work { get; init; }
    public DeliveryFact? Delivery { get; init; }
    public ReviewMarkFact? ReviewMark { get; init; }
    public GlobalWorkFact? GlobalWork { get; init; }
    public bool Heartbeat { get; init; }
    public string[]? Coverage { get; init; }
}

/// <summary>Latest provider turn evidence, independent of watcher liveness heuristics.</summary>
public sealed record SessionFact {
    public Guid? SourceSessionId { get; init; }
    public bool? TurnOpen { get; init; }
    public DateTimeOffset? TurnStartedAtUtc { get; init; }
    public DateTimeOffset? TurnEndedAtUtc { get; init; }
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public string Outcome { get; init; } = "unknown";
    public bool UsageLimit { get; init; }
    public string? AccountId { get; init; }
    public bool Question { get; init; }
    public bool InputUnconfirmed { get; init; }
    public DateTimeOffset? QuestionAtUtc { get; init; }
    public bool? AppOwned { get; init; }
    public string? RepositoryBranchKey { get; init; }
}

/// <summary>Independent task/claim/wait updates. Null means unobserved; false/zero and a matching resolved wait explicitly clear state.</summary>
public sealed record WorkFact {
    public bool? ActiveClaim { get; init; }
    public DateTimeOffset? ClaimsAtUtc { get; init; }
    public bool? PullRequestsKnown { get; init; }
    public DateTimeOffset? PullRequestsAtUtc { get; init; }
    public string[] OpenPullRequests { get; init; } = [];
    public bool HadPullRequests { get; init; }
    public bool? GoalHeld { get; init; }
    public DateTimeOffset? GoalHeldAtUtc { get; init; }
    public bool? NeedsApp { get; init; }
    public DateTimeOffset? NeedsAppAtUtc { get; init; }
    public int? QueuedInputs { get; init; }
    public DateTimeOffset? OldestQueuedAtUtc { get; init; }
    public bool? Stalled { get; init; }
    public WaitFact? Wait { get; init; }
}

public sealed record WaitFact(string Kind, string ConditionId, DateTimeOffset RecordedAtUtc, DateTimeOffset DueAtUtc, bool Resolved = false);

/// <summary>A single observed delivery stage; later stages do not fill earlier gaps.</summary>
public sealed record DeliveryFact(string DeliveryId, string Stage, DateTimeOffset? AtUtc, string? Outcome = null, string? TargetItemId = null, string? CauseCode = null);
public sealed record ReviewMarkFact(long TransitionId, string Mark);
public sealed record GlobalWorkFact(DateTimeOffset? ClaimsAtUtc, string[] ActiveClaimItems,
    DateTimeOffset? PullRequestsAtUtc, Dictionary<string, string[]> OpenPullRequestsByBranch,
    PullRequestProof[]? PullRequestProofs = null, Dictionary<string, PullRequestLinks>? LinksByItem = null);
public sealed record PullRequestProof(string Id, string State, DateTimeOffset CheckedAtUtc);
public sealed record PullRequestLinks(string[] Ids, bool Complete, DateTimeOffset CheckedAtUtc);

public static class LedgerJson {
    public const int MaximumBatchBytes = 1024 * 1024;
    public const int MaximumBatchEvents = 100;
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16
    };
}
