using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Reviewer;

internal sealed class PullRequestContext {
    public PullRequestContext(string repoFullName, string owner, string repo, int number, string title, string? body,
        bool draft, string? headSha, string? baseSha, IReadOnlyList<string> labels, string? headRepoFullName,
        bool isFork, string? authorAssociation, bool headRepositoryKnown = true, string? baseRefName = null,
        string? authorLogin = null) {
        RepoFullName = repoFullName;
        Owner = owner;
        Repo = repo;
        Number = number;
        Title = title;
        Body = body;
        Draft = draft;
        HeadSha = headSha;
        BaseSha = baseSha;
        Labels = labels;
        HeadRepoFullName = headRepoFullName;
        IsFork = isFork;
        AuthorAssociation = authorAssociation;
        HeadRepositoryKnown = headRepositoryKnown;
        BaseRefName = baseRefName;
        AuthorLogin = authorLogin;
    }

    public string RepoFullName { get; }
    public string Owner { get; }
    public string Repo { get; }
    public int Number { get; }
    public string Title { get; }
    public string? Body { get; }
    public bool Draft { get; }
    public string? HeadSha { get; }
    public string? BaseSha { get; }
    public string? BaseRefName { get; }
    public IReadOnlyList<string> Labels { get; }
    public string? HeadRepoFullName { get; }
    public bool IsFork { get; }
    public string? AuthorAssociation { get; }
    public string? AuthorLogin { get; }
    public bool HeadRepositoryKnown { get; }
    public bool IsFromFork =>
        !HeadRepositoryKnown ||
        IsFork ||
        (!string.IsNullOrWhiteSpace(HeadRepoFullName) &&
         !string.Equals(HeadRepoFullName, RepoFullName, StringComparison.OrdinalIgnoreCase));
}

internal sealed class PullRequestFile {
    public PullRequestFile(string filename, string status, string? patch) {
        Filename = filename;
        Status = status;
        Patch = patch;
    }

    public string Filename { get; }
    public string Status { get; }
    public string? Patch { get; }
}

internal sealed class IssueComment {
    public IssueComment(long id, string body, string? author = null, DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null) {
        Id = id;
        Body = body;
        Author = author;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    public long Id { get; }
    public string Body { get; }
    public string? Author { get; }
    public DateTimeOffset? CreatedAt { get; }
    public DateTimeOffset? UpdatedAt { get; }
}

internal sealed class PullRequestReviewComment {
    public PullRequestReviewComment(string body, string? author, string? path, int? line) {
        Body = body;
        Author = author;
        Path = path;
        Line = line;
    }

    public string Body { get; }
    public string? Author { get; }
    public string? Path { get; }
    public int? Line { get; }
}

internal sealed class RelatedPullRequest {
    public RelatedPullRequest(string title, string url, string repoFullName, int number) {
        Title = title;
        Url = url;
        RepoFullName = repoFullName;
        Number = number;
    }

    public string Title { get; }
    public string Url { get; }
    public string RepoFullName { get; }
    public int Number { get; }
}

internal sealed class PullRequestReviewThread {
    public PullRequestReviewThread(string id, bool isResolved, bool isOutdated, int totalComments,
        IReadOnlyList<PullRequestReviewThreadComment> comments) {
        Id = id;
        IsResolved = isResolved;
        IsOutdated = isOutdated;
        TotalComments = totalComments;
        Comments = comments;
    }

    public string Id { get; }
    public bool IsResolved { get; }
    public bool IsOutdated { get; }
    public int TotalComments { get; }
    public IReadOnlyList<PullRequestReviewThreadComment> Comments { get; }
}

internal sealed class PullRequestReviewThreadComment {
    public PullRequestReviewThreadComment(long? databaseId, DateTimeOffset? createdAt, string body, string? author, string? path, int? line) {
        DatabaseId = databaseId;
        CreatedAt = createdAt;
        Body = body;
        Author = author;
        Path = path;
        Line = line;
    }

    public long? DatabaseId { get; }
    public DateTimeOffset? CreatedAt { get; }
    public string Body { get; }
    public string? Author { get; }
    public string? Path { get; }
    public int? Line { get; }
}

internal sealed class ReviewCheckRun {
    public ReviewCheckRun(string name, string status, string? conclusion, string? detailsUrl) {
        Name = name;
        Status = status;
        Conclusion = conclusion;
        DetailsUrl = detailsUrl;
    }

    public string Name { get; }
    public string Status { get; }
    public string? Conclusion { get; }
    public string? DetailsUrl { get; }
}

internal sealed class ReviewCheckSnapshot {
    public ReviewCheckSnapshot(int passedCount, int failedCount, int pendingCount, IReadOnlyList<ReviewCheckRun> failedChecks) {
        Runs = Array.Empty<ReviewCheckRun>();
        PassedCount = passedCount;
        FailedCount = failedCount;
        PendingCount = pendingCount;
        FailedChecks = failedChecks;
    }

    public ReviewCheckSnapshot(IReadOnlyList<ReviewCheckRun> runs) {
        Runs = runs;
        PassedCount = runs.Count(IsPassed);
        FailedCount = runs.Count(IsFailed);
        PendingCount = runs.Count(IsPending);
        FailedChecks = runs
            .Where(IsFailed)
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Conclusion ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ReviewCheckRun> Runs { get; }
    public int PassedCount { get; }
    public int FailedCount { get; }
    public int PendingCount { get; }
    public IReadOnlyList<ReviewCheckRun> FailedChecks { get; }
    public bool HasData => Runs.Count > 0 || PassedCount > 0 || FailedCount > 0 || PendingCount > 0 || FailedChecks.Count > 0;

    private static bool IsPending(ReviewCheckRun check) =>
        !string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(ReviewCheckRun check) =>
        string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
        IsFailedConclusion(check.Conclusion);

    private static bool IsPassed(ReviewCheckRun check) =>
        string.Equals(check.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
        IsPassedConclusion(check.Conclusion);

    private static bool IsFailedConclusion(string? conclusion) {
        return conclusion is not null && (
            conclusion.Equals("failure", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("timed_out", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("cancelled", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("action_required", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("startup_failure", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("stale", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPassedConclusion(string? conclusion) {
        return conclusion is not null && (
            conclusion.Equals("success", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("neutral", StringComparison.OrdinalIgnoreCase) ||
            conclusion.Equals("skipped", StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class ReviewWorkflowRun {
    public ReviewWorkflowRun(string? runId, string name, string status, string? conclusion, string? url) {
        RunId = runId;
        Name = name;
        Status = status;
        Conclusion = conclusion;
        Url = url;
    }

    public string? RunId { get; }
    public string Name { get; }
    public string Status { get; }
    public string? Conclusion { get; }
    public string? Url { get; }
}
