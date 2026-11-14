using System;
using System.Collections.Generic;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>Read-only readiness assessment of an observed pull request head.</summary>
public enum GitHubReadinessVerdict {
    /// <summary>The observed evidence satisfies the reported gates.</summary>
    Ready,
    /// <summary>An observed failure or merge restriction requires action.</summary>
    Blocked,
    /// <summary>Checks, reviews, or mergeability information are pending.</summary>
    Waiting
}

/// <summary>Classification of a check or the combined head checks.</summary>
public enum GitHubReadinessCheckState {
    /// <summary>Successful, neutral, skipped, or no checks required.</summary>
    Green,
    /// <summary>A completed check failed.</summary>
    Failing,
    /// <summary>Execution or a required result is pending.</summary>
    Pending,
    /// <summary>The returned state cannot establish success.</summary>
    Unknown
}

/// <summary>One check reported against the exact head commit.</summary>
/// <param name="Name">Check run name or status context.</param>
/// <param name="State">Normalized check state.</param>
/// <param name="Status">Original GitHub execution state.</param>
/// <param name="Conclusion">Original check-run conclusion, when present.</param>
/// <param name="IsRequired">GitHub's required-check evidence, when present.</param>
public sealed record GitHubReadinessCheck(string Name, GitHubReadinessCheckState State, string Status,
    string? Conclusion, bool? IsRequired);

/// <summary>Small preview of an unresolved thread, including outdated threads.</summary>
/// <param name="Path">Repository-relative source path.</param>
/// <param name="Text">Whitespace-normalized first comment, limited to 240 characters.</param>
/// <param name="IsOutdated">Whether GitHub marks the diff location outdated.</param>
public sealed record GitHubReadinessThread(string Path, string Text, bool IsOutdated);

/// <summary>Observable pending-review evidence.</summary>
/// <param name="Actor">Login or qualified team slug, when available.</param>
/// <param name="Source">review-request, pending-review, or eyes-reaction.</param>
/// <param name="SinceUtc">GitHub timestamp, when exposed by the source.</param>
public sealed record GitHubReadinessPendingReviewer(string Actor, string Source, DateTimeOffset? SinceUtc);

/// <summary>Latest submitted review evidence for one actor.</summary>
/// <param name="Actor">Review author login.</param>
/// <param name="State">GitHub review state.</param>
/// <param name="SubmittedAtUtc">Submission time.</param>
/// <param name="CommitSha">Commit associated with the review.</param>
/// <param name="IsCurrentHead">Whether that commit matches the observed head.</param>
public sealed record GitHubReadinessReview(string Actor, string State, DateTimeOffset SubmittedAtUtc,
    string? CommitSha, bool IsCurrentHead);

/// <summary>Classic base-branch protection evidence. Repository rulesets can impose additional gates.</summary>
public sealed record GitHubReadinessBranchProtection {
    /// <summary>Whether approving reviews are required.</summary>
    public bool RequiresApprovingReviews { get; init; }
    /// <summary>Configured minimum approving reviews.</summary>
    public int RequiredApprovingReviewCount { get; init; }
    /// <summary>Whether code-owner approval is required.</summary>
    public bool RequiresCodeOwnerReviews { get; init; }
    /// <summary>Whether the latest push needs approval.</summary>
    public bool RequireLastPushApproval { get; init; }
    /// <summary>Whether pushes dismiss stale approvals.</summary>
    public bool DismissesStaleReviews { get; init; }
    /// <summary>Whether conversations must be resolved.</summary>
    public bool RequiresConversationResolution { get; init; }
    /// <summary>Whether status checks are required.</summary>
    public bool RequiresStatusChecks { get; init; }
    /// <summary>Whether checks require an up-to-date branch.</summary>
    public bool RequiresStrictStatusChecks { get; init; }
    /// <summary>Required status context names exposed by the rule.</summary>
    public IReadOnlyList<string> RequiredStatusCheckContexts { get; init; } = Array.Empty<string>();
}

/// <summary>Compact evidence report for a bounded, uncached GitHub readiness read.</summary>
/// <remarks>Reviews identify their commit explicitly. Push-time availability is reported in EvidenceGaps.
/// Ready is an observation, with GitHub's aggregate merge state covering additional server-side gates.</remarks>
public sealed record GitHubPullRequestReadiness {
    /// <summary>Repository in owner/name form.</summary>
    public string Repository { get; init; } = string.Empty;
    /// <summary>Pull request number.</summary>
    public int Number { get; init; }
    /// <summary>Head SHA to which the checks and review comparisons belong.</summary>
    public string HeadSha { get; init; } = string.Empty;
    /// <summary>Source branch name used to match local checkouts.</summary>
    public string HeadBranch { get; init; } = string.Empty;
    /// <summary>Head SHA at the final consistency read.</summary>
    public string ObservedHeadSha { get; init; } = string.Empty;
    /// <summary>Completion time of the observation.</summary>
    public DateTimeOffset ObservedAtUtc { get; init; }
    /// <summary>Committer timestamp of the observed head commit.</summary>
    public DateTimeOffset? HeadCommittedAtUtc { get; init; }
    /// <summary>OPEN, CLOSED, or MERGED.</summary>
    public string State { get; init; } = string.Empty;
    /// <summary>Whether the pull request is a draft.</summary>
    public bool IsDraft { get; init; }
    /// <summary>GitHub's mergeability value.</summary>
    public string Mergeable { get; init; } = string.Empty;
    /// <summary>GitHub's combined merge-gate state.</summary>
    public string MergeStateStatus { get; init; } = string.Empty;
    /// <summary>GitHub's required-review decision, when applicable.</summary>
    public string? ReviewDecision { get; init; }
    /// <summary>Classic rule evidence, null when GitHub returns no matching rule.</summary>
    public GitHubReadinessBranchProtection? BranchProtection { get; init; }
    /// <summary>Combined check state for the observed head.</summary>
    public GitHubReadinessCheckState ChecksState { get; init; }
    /// <summary>Named check results, including missing configured required contexts.</summary>
    public IReadOnlyList<GitHubReadinessCheck> Checks { get; init; } = Array.Empty<GitHubReadinessCheck>();
    /// <summary>Total unresolved threads across every fetched page.</summary>
    public int UnresolvedThreadCount { get; init; }
    /// <summary>First five unresolved thread previews.</summary>
    public IReadOnlyList<GitHubReadinessThread> UnresolvedThreads { get; init; } = Array.Empty<GitHubReadinessThread>();
    /// <summary>Pending requests, unpublished reviews, and unsuperseded EYES reactions.</summary>
    public IReadOnlyList<GitHubReadinessPendingReviewer> PendingReviewers { get; init; } = Array.Empty<GitHubReadinessPendingReviewer>();
    /// <summary>Latest submitted review per actor with current-head evidence.</summary>
    public IReadOnlyList<GitHubReadinessReview> Reviews { get; init; } = Array.Empty<GitHubReadinessReview>();
    /// <summary>Readiness decision.</summary>
    public GitHubReadinessVerdict Verdict { get; init; }
    /// <summary>Stable reason codes for observed blockers and waiting conditions.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    /// <summary>Limits of the available evidence, separate from observed gates.</summary>
    public IReadOnlyList<string> EvidenceGaps { get; init; } = Array.Empty<string>();
}
