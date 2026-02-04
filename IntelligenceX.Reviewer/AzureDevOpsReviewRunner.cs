using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal static class AzureDevOpsReviewRunner {
    public static async Task<int> RunAsync(ReviewSettings settings, CancellationToken cancellationToken) {
        var options = ResolveOptions(settings);
        if (!options.Success || options.BaseUri is null || options.Project is null || options.Token is null) {
            Console.Error.WriteLine(options.Error ?? "Azure DevOps configuration is incomplete.");
            return 1;
        }

        if (settings.TriageOnly) {
            Console.Error.WriteLine("Triage-only mode is not yet supported for Azure DevOps.");
            return 1;
        }
        if (settings.ProgressUpdates) {
            Console.WriteLine("Progress updates are not supported for Azure DevOps; skipping.");
        }

        var baseUri = options.BaseUri;
        var project = options.Project;
        var token = options.Token;

        using var client = new AzureDevOpsClient(baseUri, token, options.AuthScheme);
        var pr = await client.GetPullRequestAsync(project, options.PullRequestId, cancellationToken).ConfigureAwait(false);

        if (settings.SkipDraft && pr.IsDraft) {
            Console.WriteLine("Skipping draft pull request.");
            return 0;
        }

        var repositoryId = !string.IsNullOrWhiteSpace(settings.AzureRepository)
            ? settings.AzureRepository!
            : pr.RepositoryId;
        if (string.IsNullOrWhiteSpace(repositoryId)) {
            Console.Error.WriteLine("Azure DevOps repository id is missing. Set review.azureRepo or ensure the PR payload includes repository info.");
            return 1;
        }

        var iterationIds = await client.GetIterationIdsAsync(project, repositoryId, pr.PullRequestId, cancellationToken)
            .ConfigureAwait(false);
        var files = await client.GetPullRequestChangesAsync(project, repositoryId, pr.PullRequestId, cancellationToken)
            .ConfigureAwait(false);
        if (files.Count == 0) {
            Console.WriteLine("No files to review.");
            return 0;
        }

        if (ShouldSkipByPaths(files, settings.SkipPaths)) {
            Console.WriteLine("Skipping pull request due to path filter.");
            return 0;
        }

        var filtered = ReviewerApp.FilterFilesByPaths(files, settings.IncludePaths, settings.ExcludePaths);
        if (filtered.Count == 0) {
            Console.WriteLine("No files matched include/exclude filters.");
            return 0;
        }

        var (limited, budgetNote) = LimitFiles(filtered, settings.MaxFiles, settings.MaxPatchChars);
        if (!settings.ReviewBudgetSummary) {
            budgetNote = string.Empty;
        }
        var repoFullName = $"{pr.Project}/{pr.RepositoryName}";
        var context = new PullRequestContext(repoFullName, pr.Project, pr.RepositoryName,
            pr.PullRequestId, pr.Title, pr.Description, pr.IsDraft, pr.SourceCommit, pr.TargetCommit, Array.Empty<string>(),
            repoFullName, false, null);
        var diffNote = BuildDiffNote(iterationIds);
        var prompt = PromptBuilder.Build(context, limited, settings, diffNote, null, inlineSupported: false);
        if (settings.RedactPii) {
            prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
        }

        var runner = new ReviewRunner(settings);
        var reviewBody = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        var commentBody = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: false,
            inlineSuppressed: false, autoResolveNote: string.Empty, budgetNote, usageLine: string.Empty, findingsBlock: string.Empty);

        await client.CreatePullRequestThreadAsync(project, repositoryId, pr.PullRequestId, commentBody, cancellationToken)
            .ConfigureAwait(false);
        Console.WriteLine("Posted Azure DevOps review comment.");
        return 0;
    }

    private static bool ShouldSkipByPaths(IReadOnlyList<PullRequestFile> files, IReadOnlyList<string> skipPaths) {
        if (files.Count == 0 || skipPaths.Count == 0) {
            return false;
        }
        foreach (var file in files) {
            var matches = skipPaths.Any(pattern => GlobMatcher.IsMatch(pattern, file.Filename));
            if (!matches) {
                return false;
            }
        }
        return true;
    }

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) LimitFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        if (maxFiles <= 0 || files.Count <= maxFiles) {
            return (files, ReviewerApp.BuildBudgetNote(files.Count, files.Count, 0, maxPatchChars));
        }
        var limited = files.Take(maxFiles).ToList();
        var note = ReviewerApp.BuildBudgetNote(files.Count, limited.Count, 0, maxPatchChars);
        return (limited, note);
    }

    internal static string BuildDiffNote(IReadOnlyList<int> iterationIds) {
        if (iterationIds.Count == 0) {
            return "pull request changes";
        }
        var minIterationId = iterationIds.Min();
        var maxIterationId = iterationIds.Max();
        return iterationIds.Count == 1
            ? $"iteration {maxIterationId}"
            : $"iterations {minIterationId}-{maxIterationId} (count {iterationIds.Count})";
    }

    private static AzureDevOpsOptions ResolveOptions(ReviewSettings settings) {
        var baseUrl = settings.AzureBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            baseUrl = Environment.GetEnvironmentVariable("SYSTEM_COLLECTIONURI");
        }
        if (string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(settings.AzureOrganization)) {
            baseUrl = $"https://dev.azure.com/{settings.AzureOrganization}";
        }

        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)) {
            return AzureDevOpsOptions.Fail("Azure DevOps base URL is missing or invalid. Set review.azureBaseUrl or SYSTEM_COLLECTIONURI.");
        }

        var project = settings.AzureProject ?? Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECT");
        if (string.IsNullOrWhiteSpace(project)) {
            return AzureDevOpsOptions.Fail("Azure DevOps project is missing. Set review.azureProject or SYSTEM_TEAMPROJECT.");
        }

        var pullRequestId = ResolvePullRequestId();
        if (!pullRequestId.HasValue) {
            return AzureDevOpsOptions.Fail("Azure DevOps pull request id is missing. Set SYSTEM_PULLREQUEST_PULLREQUESTID.");
        }

        var tokenEnv = settings.AzureTokenEnv;
        if (string.IsNullOrWhiteSpace(tokenEnv)) {
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN"))) {
                tokenEnv = "SYSTEM_ACCESSTOKEN";
            } else if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AZURE_DEVOPS_TOKEN"))) {
                tokenEnv = "AZURE_DEVOPS_TOKEN";
            }
        }
        if (string.IsNullOrWhiteSpace(tokenEnv)) {
            return AzureDevOpsOptions.Fail("Azure DevOps token env is missing. Set review.azureTokenEnv or SYSTEM_ACCESSTOKEN.");
        }
        var token = Environment.GetEnvironmentVariable(tokenEnv);
        if (string.IsNullOrWhiteSpace(token)) {
            return AzureDevOpsOptions.Fail($"Azure DevOps token is empty. Environment variable '{tokenEnv}' is not set.");
        }

        var scheme = ResolveAuthScheme(settings, tokenEnv, token);
        return AzureDevOpsOptions.Ok(baseUri, project, pullRequestId.Value, token, scheme);
    }

    private static AzureDevOpsAuthScheme ResolveAuthScheme(ReviewSettings settings, string tokenEnv, string token) {
        if (settings.AzureAuthSchemeSpecified) {
            return settings.AzureAuthScheme;
        }
        if (string.Equals(tokenEnv, "SYSTEM_ACCESSTOKEN", StringComparison.OrdinalIgnoreCase)) {
            return AzureDevOpsAuthScheme.Bearer;
        }
        return LooksLikeJwt(token) ? AzureDevOpsAuthScheme.Bearer : AzureDevOpsAuthScheme.Basic;
    }

    private static bool LooksLikeJwt(string token) {
        if (string.IsNullOrWhiteSpace(token)) {
            return false;
        }
        // Heuristic: most JWTs have at least two dots; users can override via azureAuthScheme.
        var dotCount = token.Count(ch => ch == '.');
        return dotCount >= 2;
    }

    private static int? ResolvePullRequestId() {
        var raw = Environment.GetEnvironmentVariable("SYSTEM_PULLREQUEST_PULLREQUESTID")
                  ?? Environment.GetEnvironmentVariable("AZURE_DEVOPS_PR_ID")
                  ?? Environment.GetEnvironmentVariable("PULL_REQUEST_ID");
        if (int.TryParse(raw, out var parsed) && parsed > 0) {
            return parsed;
        }
        return null;
    }

    private sealed record AzureDevOpsOptions(bool Success, Uri? BaseUri, string? Project, int PullRequestId,
        string? Token, AzureDevOpsAuthScheme AuthScheme, string? Error) {
        public static AzureDevOpsOptions Ok(Uri baseUri, string project, int pullRequestId, string token,
            AzureDevOpsAuthScheme authScheme)
            => new(true, baseUri, project, pullRequestId, token, authScheme, null);

        public static AzureDevOpsOptions Fail(string error)
            => new(false, null, null, 0, null, AzureDevOpsAuthScheme.Bearer, error);
    }
}
