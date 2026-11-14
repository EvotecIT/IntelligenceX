namespace IntelligenceX.Ledger.Contracts;

/// <summary>Compact read-only merge evidence. Task authorization remains with the caller.</summary>
public sealed record PullRequestReadiness {
    public required string Repo { get; init; }
    public required int Pr { get; init; }
    public string? HeadSha { get; init; }
    public string? ChecksHeadSha { get; init; }
    public string State { get; init; } = "unknown";
    public string Checks { get; init; } = "unknown";
    public string[] FailingChecks { get; init; } = [];
    public string[] PendingChecks { get; init; } = [];
    public int FailingCheckCount { get; init; }
    public int PendingCheckCount { get; init; }
    public int UnresolvedThreads { get; init; }
    public PullRequestThread[] Threads { get; init; } = [];
    public bool ReviewerPending { get; init; }
    public string[] PendingReviewers { get; init; } = [];
    public bool RecheckedAfterObservedHead { get; init; }
    public DateTimeOffset CheckedAtUtc { get; init; }
    public bool? Draft { get; init; }
    public string Mergeable { get; init; } = "unknown";
    public string MergeState { get; init; } = "unknown";
    public string? ReviewDecision { get; init; }
    public PullRequestProtection? Protection { get; init; }
    public string Verdict { get; init; } = "waiting";
    public string[] Reasons { get; init; } = [];
    public PullRequestCheckout[] Cleanup { get; init; } = [];
    public string[] Coverage { get; init; } = [];
    public bool Truncated { get; init; }
}

public sealed record PullRequestThread(string Path, string Text);
public sealed record PullRequestProtection(bool ReviewsRequired, int Approvals, bool CodeOwners,
    bool LatestPushApproval, bool Conversations, bool StatusChecks, bool UpToDate);
public sealed record PullRequestCheckout(string Machine, string Worktree, string? Branch);
