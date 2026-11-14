using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Workspace observation. Null file names mean Git state was unavailable.</summary>
public sealed record RepositoryObservation(string Repo, string? Branch, string? Worktree, string? Title,
    string[] PullRequests, string[]? Files, bool FilesCapped, string? BaseRef, string GitState, bool? PullRequestsKnown = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? FileCount = null,
    bool? Dirty = null, bool? Unpushed = null, string? Commit = null);
/// <summary>Stored session observation used to build repository queries.</summary>
public sealed record RepositorySession(string AgentId, string Machine, string Provider, string? Branch, string? Worktree,
    string? Title, string[] PullRequests, DateTimeOffset? LastActivityAtUtc, string[]? Files, bool FilesCapped, string? BaseRef, string GitState, bool Live,
    bool? PullRequestsKnown = null, int? FileCount = null, DateTimeOffset? ObservedAtUtc = null,
    string Origin = "unknown", string? OriginEvidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FreshAdvice = null,
    int? TurnCount = null, DateTimeOffset? CreatedAtUtc = null);
/// <summary>Compact session identity and activity within a shared checkout.</summary>
public sealed record RepositoryWorktreeSession(string AgentId, string Provider, string? Title, DateTimeOffset? LastActivityAtUtc, bool Live,
    string Origin = "unknown", [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FreshAdvice = null);
/// <summary>Files describe the checkout. A null count means the full changed-file count was unavailable.</summary>
public sealed record RepositoryWorktree(string Machine, string? Worktree, string? Branch, RepositoryWorktreeSession[] Sessions,
    string[] PullRequests, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? Files,
    int? FileCount, bool FilesCapped,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? BaseRef,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GitState,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? PullRequestsKnown) {
    /// <summary>Completed/recent membership is summarized by default. Details returns those full session rows.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? RecentSessionCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? RecentTitles { get; init; }
    // Overlap evaluates the stored evidence before limiting the response to five names.
    [JsonIgnore] public string[]? ComparisonFiles { get; init; }
    [JsonIgnore] public RepositorySession[]? ComparisonSessions { get; init; }
}
public sealed record RepositoryActivity(string Summary, string Repo, RepositoryWorktree[] Worktrees, Lease[] Leases, string[] Coverage);
public sealed record OverlapMatch(RepositoryWorktree Worktree, string[] Reasons, string[] SharedFiles, int SharedFileCount) {
    /// <summary>Exact branch evidence can belong to a recent session before the checkout changed branches.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? MatchedBranches { get; init; }
}
public sealed record RepositoryOverlap(string Summary, string Repo, string Verdict, OverlapMatch[] Matches, Lease[] Leases, string[] Coverage);
