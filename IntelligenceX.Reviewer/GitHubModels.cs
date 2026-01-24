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
    public IssueComment(long id, string body) {
        Id = id;
        Body = body;
    }

    public long Id { get; }
    public string Body { get; }
}
