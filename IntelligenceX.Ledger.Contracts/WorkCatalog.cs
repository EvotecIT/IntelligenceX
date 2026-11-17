using System.Text.Json.Serialization;

namespace IntelligenceX.Ledger.Contracts;

/// <summary>Task verdict values. Exactly one applies to each task; session verdicts are evidence, not replaced.</summary>
public static class TaskVerdicts {
    public const string NotStarted = "not-started";
    public const string InProgress = "in-progress";
    public const string WaitingOnYou = "waiting-on-you";
    public const string DoneClosable = "done-closable";
    public const string Stale = "stale";
    public const string NeedsReplacement = "needs-replacement";
    public const string Archived = "archived";

    /// <summary>Every verdict, in the order a reader should act on them.</summary>
    public static readonly string[] All = [WaitingOnYou, NeedsReplacement, DoneClosable, Stale, InProgress, NotStarted, Archived];
}

/// <summary>One task (board item or addressed issue) with its verdict, the evidence behind it and what was missing.</summary>
public sealed record TaskVerdictRow {
    /// <summary>Stable identity: <c>board:&lt;item&gt;</c> or <c>issue:&lt;owner/repo#number&gt;</c>.</summary>
    public required string Id { get; init; }
    /// <summary><c>board</c>, <c>issue</c> or <c>board+issue</c> when a board item points at the issue.</summary>
    public required string Source { get; init; }
    public required string Title { get; init; }
    public string? Url { get; init; }
    public string? Repo { get; init; }
    public required string Verdict { get; init; }
    public required string Reason { get; init; }
    /// <summary>Where the work is, when someone has it: machine, provider and checkout.</summary>
    public string? Where { get; init; }
    public string? BoardStatus { get; init; }
    public string? Agent { get; init; }
    public string? Machine { get; init; }
    public string? SessionItemId { get; init; }
    public string? SessionVerdict { get; init; }
    public string[] PullRequests { get; init; } = [];
    public string? Branch { get; init; }
    public DateTimeOffset? UpdatedAtUtc { get; init; }
    public string[] Evidence { get; init; } = [];
    public string[] MissingFacts { get; init; } = [];
    /// <summary>Writes the Ledger would make; nothing is applied until an automatic route is approved.</summary>
    public string[] SuggestedActions { get; init; } = [];
}

/// <summary>Task verdicts across every catalogued project.</summary>
public sealed record WorkReconcileView(DateTimeOffset AsOfUtc, DateTimeOffset? CatalogAtUtc, Dictionary<string, int> Counts,
    TaskVerdictRow[] Tasks, string[] Coverage, bool Truncated);

/// <summary>One repository's open issues, PRs, observed worktrees and task verdict counts.</summary>
public sealed record ProjectStatusRow(string Repo, int OpenIssues, int OpenPullRequests, int DraftPullRequests,
    int Worktrees, int LiveWorktrees, Dictionary<string, int> Tasks, DateTimeOffset? PushedAtUtc) {
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Archived { get; init; }
    /// <summary>Filled for a single-repository read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CatalogWorktree[]? WorktreeRows { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CatalogPullRequest[]? PullRequestRows { get; init; }
}

/// <summary>Projects ordered by what needs attention first.</summary>
public sealed record ProjectStatusView(DateTimeOffset AsOfUtc, DateTimeOffset? CatalogAtUtc, int Repositories,
    Dictionary<string, int> TaskCounts, ProjectStatusRow[] Projects, string[] Coverage, bool Truncated);

/// <summary>A checkout observed on a machine through a session that used it.</summary>
public sealed record CatalogWorktree(string Machine, string? Path, string? Branch, bool Live, DateTimeOffset? LastActivityAtUtc,
    string[] PullRequests, int Sessions);

/// <summary>Open PR metadata. Bodies and comments are never read.</summary>
public sealed record CatalogPullRequest(int Number, string Title, string? HeadBranch, bool Draft, DateTimeOffset UpdatedAtUtc);
