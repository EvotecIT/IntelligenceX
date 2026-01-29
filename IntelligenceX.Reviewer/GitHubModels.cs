using System.Collections.Generic;

namespace IntelligenceX.Reviewer;

internal sealed class PullRequestContext {
    public PullRequestContext(string repoFullName, string owner, string repo, int number, string title, string? body,
        bool draft, string? headSha, IReadOnlyList<string> labels) {
        RepoFullName = repoFullName;
        Owner = owner;
        Repo = repo;
        Number = number;
        Title = title;
        Body = body;
        Draft = draft;
        HeadSha = headSha;
        Labels = labels;
    }

    public string RepoFullName { get; }
    public string Owner { get; }
    public string Repo { get; }
    public int Number { get; }
    public string Title { get; }
    public string? Body { get; }
    public bool Draft { get; }
    public string? HeadSha { get; }
    public IReadOnlyList<string> Labels { get; }
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
    public IssueComment(long id, string body, string? author = null) {
        Id = id;
        Body = body;
        Author = author;
    }

    public long Id { get; }
    public string Body { get; }
    public string? Author { get; }
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
    public PullRequestReviewThread(bool isResolved, bool isOutdated, IReadOnlyList<PullRequestReviewThreadComment> comments) {
        IsResolved = isResolved;
        IsOutdated = isOutdated;
        Comments = comments;
    }

    public bool IsResolved { get; }
    public bool IsOutdated { get; }
    public IReadOnlyList<PullRequestReviewThreadComment> Comments { get; }
}

internal sealed class PullRequestReviewThreadComment {
    public PullRequestReviewThreadComment(string body, string? author, string? path, int? line) {
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
