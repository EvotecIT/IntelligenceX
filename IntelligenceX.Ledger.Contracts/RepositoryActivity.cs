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
public sealed record RepositoryActivity(string Summary, string Repo, RepositoryWorktree[] Worktrees, Lease[] Leases, string[] Coverage) {
    /// <summary>Every checkout of the repository found by each machine's periodic Git scan, with or without a session.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public RepositoryCheckout[]? Checkouts { get; init; }
    /// <summary>When each machine last scanned. A machine listed here with no checkout row has no checkout of the repository.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CheckoutScan[]? CheckoutScans { get; init; }
    /// <summary>Checkout rows left out of the compact answer to keep it small, clean ones first. Details returns them all.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? CheckoutsOmitted { get; init; }
}

/// <summary>
/// One checkout found by a machine's Git scan. Ahead and behind compare with the upstream as of the last fetch. Changed
/// files ignore line-ending-only differences. Stashes and unpushed branches belong to the repository, so they are
/// reported on its main checkout only. An unpushed branch has commits that no remote branch contains.
/// </summary>
public sealed record CheckoutObservation(string Repo, string Path, string State, bool Primary,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Branch = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Commit = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Upstream = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Ahead = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Behind = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ChangedFiles = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? UntrackedFiles = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Stashes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string[]? UnpushedBranches = null);

/// <summary>A machine's complete scan. It replaces that machine's previous scan as a whole.</summary>
public sealed record CheckoutCensus(DateTimeOffset ObservedAtUtc, string[] Roots, CheckoutObservation[] Checkouts, bool Truncated = false);

/// <summary>A checkout as repo_activity returns it.</summary>
public sealed record RepositoryCheckout(string Machine, DateTimeOffset ObservedAtUtc, CheckoutObservation Checkout);

/// <summary>When a machine last scanned its checkouts, and whether the scan stopped at its bound.</summary>
public sealed record CheckoutScan(string Machine, DateTimeOffset ObservedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Truncated = false);
public sealed record OverlapMatch(RepositoryWorktree Worktree, string[] Reasons, string[] SharedFiles, int SharedFileCount) {
    /// <summary>Exact branch evidence can belong to a recent session before the checkout changed branches.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string[]? MatchedBranches { get; init; }
}
public sealed record RepositoryOverlap(string Summary, string Repo, string Verdict, OverlapMatch[] Matches, Lease[] Leases, string[] Coverage);
