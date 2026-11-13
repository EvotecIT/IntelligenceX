namespace IntelligenceX.Ledger.Contracts;

/// <summary>Workspace observation. Null file names mean Git state was unavailable.</summary>
public sealed record RepositoryObservation(string Repo, string? Branch, string? Worktree, string? Title,
    string[] PullRequests, string[]? Files, bool FilesCapped, string? BaseRef, string GitState, bool? PullRequestsKnown = null);
public sealed record RepositorySession(string AgentId, string Machine, string Provider, string? Branch, string? Worktree,
    string? Title, string[] PullRequests, DateTimeOffset? LastActivityAtUtc, string[]? Files, bool FilesCapped, string? BaseRef, string GitState, bool Live, bool? PullRequestsKnown = null);
public sealed record RepositoryActivity(string Repo, RepositorySession[] Sessions, Lease[] Leases, string[] Coverage);
public sealed record OverlapMatch(RepositorySession Session, string[] Reasons, string[] SharedFiles);
public sealed record RepositoryOverlap(string Repo, string Verdict, OverlapMatch[] Matches, Lease[] Leases, string[] Coverage);
