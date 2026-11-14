using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Compact session lookup. Nullable observations retain their unknown state.</summary>
public sealed record SessionStatus {
    public required string State { get; init; }
    public string? Id { get; init; }
    public string? Provider { get; init; }
    public string? Machine { get; init; }
    public string? Title { get; init; }
    public string? Verdict { get; init; }
    public string? Reason { get; init; }
    public bool LastKnown { get; init; }
    public bool? TurnOpen { get; init; }
    public DateTimeOffset? TurnOpenSinceUtc { get; init; }
    public DateTimeOffset? LastActivityAtUtc { get; init; }
    public bool? QuestionWaiting { get; init; }
    public bool? UsageLimited { get; init; }
    public bool? Archived { get; init; }
    public string? Repo { get; init; }
    public string? Branch { get; init; }
    public string[] PullRequests { get; init; } = [];
    public Lease[] Leases { get; init; } = [];
    public int TotalLeases { get; init; }
    public string Origin { get; init; } = "unknown";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OriginEvidence { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SessionMatch[]? Matches { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FreshAdvice { get; init; }
    public bool Truncated { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LeaseTakeover[]? Takeovers { get; init; }
}
public sealed record SessionMatch(string Id, string Machine, string Provider, string? Title);
public sealed record LeaseTakeover(string Key, string Holder, string Machine, DateTimeOffset AtUtc);
