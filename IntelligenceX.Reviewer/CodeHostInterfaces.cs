using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal readonly struct CodeHostCompareFilesResult {
    public CodeHostCompareFilesResult(IReadOnlyList<PullRequestFile> files, bool isTruncated) {
        Files = files;
        IsTruncated = isTruncated;
    }

    public IReadOnlyList<PullRequestFile> Files { get; }
    public bool IsTruncated { get; }
}

internal interface IReviewCodeHostReader {
    Task<PullRequestContext> GetPullRequestAsync(string repositoryOrProject, int pullRequestNumber, CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(PullRequestContext context, CancellationToken cancellationToken);
    Task<CodeHostCompareFilesResult> GetCompareFilesAsync(PullRequestContext context, string baseSha, string headSha,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<IssueComment>> ListIssueCommentsAsync(PullRequestContext context, int maxResults,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestReviewComment>> ListPullRequestReviewCommentsAsync(PullRequestContext context, int maxResults,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PullRequestReviewThread>> ListPullRequestReviewThreadsAsync(PullRequestContext context, int maxThreads,
        int maxComments, CancellationToken cancellationToken);
}

internal sealed class GitHubCodeHostReader : IReviewCodeHostReader {
    private readonly GitHubClient _github;

    public GitHubCodeHostReader(GitHubClient github) {
        _github = github;
    }

    public async Task<PullRequestContext> GetPullRequestAsync(string repositoryOrProject, int pullRequestNumber,
        CancellationToken cancellationToken) {
        var (owner, repo) = SplitRepo(repositoryOrProject);
        return await _github.GetPullRequestAsync(owner, repo, pullRequestNumber, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(PullRequestContext context,
        CancellationToken cancellationToken) {
        return _github.GetPullRequestFilesAsync(context.Owner, context.Repo, context.Number, cancellationToken);
    }

    public async Task<CodeHostCompareFilesResult> GetCompareFilesAsync(PullRequestContext context, string baseSha, string headSha,
        CancellationToken cancellationToken) {
        var result = await _github.GetCompareFilesAsync(context.Owner, context.Repo, baseSha, headSha, cancellationToken)
            .ConfigureAwait(false);
        return new CodeHostCompareFilesResult(result.Files, result.IsTruncated);
    }

    public Task<IReadOnlyList<IssueComment>> ListIssueCommentsAsync(PullRequestContext context, int maxResults,
        CancellationToken cancellationToken) {
        return _github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, maxResults, cancellationToken);
    }

    public Task<IReadOnlyList<PullRequestReviewComment>> ListPullRequestReviewCommentsAsync(PullRequestContext context,
        int maxResults, CancellationToken cancellationToken) {
        return _github.ListPullRequestReviewCommentsAsync(context.Owner, context.Repo, context.Number, maxResults, cancellationToken);
    }

    public Task<IReadOnlyList<PullRequestReviewThread>> ListPullRequestReviewThreadsAsync(PullRequestContext context,
        int maxThreads, int maxComments, CancellationToken cancellationToken) {
        return _github.ListPullRequestReviewThreadsAsync(context.Owner, context.Repo, context.Number, maxThreads, maxComments,
            cancellationToken);
    }

    private static (string Owner, string Repo) SplitRepo(string value) {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repository format '{value}'. Expected owner/repo.");
        }
        return (parts[0], parts[1]);
    }
}

internal sealed class AzureDevOpsCodeHostReader : IReviewCodeHostReader {
    private readonly AzureDevOpsClient _azure;
    private readonly string _project;
    private readonly string _repositoryId;

    public AzureDevOpsCodeHostReader(AzureDevOpsClient azure, string project, string repositoryId) {
        _azure = azure;
        _project = project;
        _repositoryId = repositoryId;
    }

    public async Task<PullRequestContext> GetPullRequestAsync(string repositoryOrProject, int pullRequestNumber,
        CancellationToken cancellationToken) {
        var project = string.IsNullOrWhiteSpace(repositoryOrProject) ? _project : repositoryOrProject;
        var pr = await _azure.GetPullRequestAsync(project, pullRequestNumber, cancellationToken).ConfigureAwait(false);
        var repoFullName = $"{pr.Project}/{pr.RepositoryName}";
        return new PullRequestContext(repoFullName, pr.Project, pr.RepositoryName, pr.PullRequestId, pr.Title, pr.Description, pr.IsDraft,
            pr.SourceCommit, pr.TargetCommit, Array.Empty<string>(), repoFullName, false, null);
    }

    public Task<IReadOnlyList<PullRequestFile>> GetPullRequestFilesAsync(PullRequestContext context,
        CancellationToken cancellationToken) {
        return _azure.GetPullRequestChangesAsync(_project, _repositoryId, context.Number, cancellationToken);
    }

    public Task<CodeHostCompareFilesResult> GetCompareFilesAsync(PullRequestContext context, string baseSha, string headSha,
        CancellationToken cancellationToken) {
        return Task.FromResult(new CodeHostCompareFilesResult(Array.Empty<PullRequestFile>(), false));
    }

    public Task<IReadOnlyList<IssueComment>> ListIssueCommentsAsync(PullRequestContext context, int maxResults,
        CancellationToken cancellationToken) {
        return Task.FromResult<IReadOnlyList<IssueComment>>(Array.Empty<IssueComment>());
    }

    public Task<IReadOnlyList<PullRequestReviewComment>> ListPullRequestReviewCommentsAsync(PullRequestContext context,
        int maxResults, CancellationToken cancellationToken) {
        return Task.FromResult<IReadOnlyList<PullRequestReviewComment>>(Array.Empty<PullRequestReviewComment>());
    }

    public Task<IReadOnlyList<PullRequestReviewThread>> ListPullRequestReviewThreadsAsync(PullRequestContext context,
        int maxThreads, int maxComments, CancellationToken cancellationToken) {
        return Task.FromResult<IReadOnlyList<PullRequestReviewThread>>(Array.Empty<PullRequestReviewThread>());
    }
}
