using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

internal static class CleanupService {
    public static async Task<PullRequestContext> RunAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var cleanup = settings.Cleanup;
        if (!cleanup.Enabled || !cleanup.AllowsPr) {
            return context;
        }
        if (cleanup.RequiresLabel && !cleanup.HasLabel(context.Labels)) {
            return context;
        }

        var prompt = CleanupPromptBuilder.Build(context, cleanup);
        var runner = new ReviewRunner(settings);
        var response = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        var result = CleanupResult.TryParse(response);
        if (result is null || !result.NeedsCleanup) {
            return context;
        }

        var normalizedTitle = Normalize(result.Title);
        var normalizedBody = Normalize(result.Body);
        if (!cleanup.AllowsTitleEdit) {
            normalizedTitle = context.Title;
        }
        if (!cleanup.AllowsBodyEdit) {
            normalizedBody = context.Body ?? string.Empty;
        }

        var newTitle = string.IsNullOrWhiteSpace(normalizedTitle) ? context.Title : normalizedTitle!;
        var newBody = normalizedBody ?? context.Body ?? string.Empty;

        var titleChanged = !string.Equals(context.Title, newTitle, StringComparison.Ordinal);
        var bodyChanged = !string.Equals(context.Body ?? string.Empty, newBody, StringComparison.Ordinal);
        if (!titleChanged && !bodyChanged) {
            return context;
        }

        switch (cleanup.Mode) {
            case CleanupMode.Comment:
                await PostSuggestionAsync(github, context, result, cancellationToken).ConfigureAwait(false);
                return context;
            case CleanupMode.Edit:
                if (result.Confidence >= cleanup.MinConfidence) {
                    await ApplyEditAsync(github, context, result, newTitle, newBody, cleanup, cancellationToken)
                        .ConfigureAwait(false);
                    return new PullRequestContext(context.RepoFullName, context.Owner, context.Repo, context.Number,
                        newTitle, newBody, context.Draft, context.HeadSha, context.Labels);
                }
                await PostSuggestionAsync(github, context, result, cancellationToken).ConfigureAwait(false);
                return context;
            case CleanupMode.Hybrid:
                if (result.Confidence >= cleanup.MinConfidence) {
                    await ApplyEditAsync(github, context, result, newTitle, newBody, cleanup, cancellationToken)
                        .ConfigureAwait(false);
                    return new PullRequestContext(context.RepoFullName, context.Owner, context.Repo, context.Number,
                        newTitle, newBody, context.Draft, context.HeadSha, context.Labels);
                }
                await PostSuggestionAsync(github, context, result, cancellationToken).ConfigureAwait(false);
                return context;
            default:
                return context;
        }
    }

    private static async Task PostSuggestionAsync(GitHubClient github, PullRequestContext context, CleanupResult result,
        CancellationToken cancellationToken) {
        var body = CleanupFormatter.BuildSuggestionComment(context, result);
        await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ApplyEditAsync(GitHubClient github, PullRequestContext context, CleanupResult result,
        string title, string body, CleanupSettings settings, CancellationToken cancellationToken) {
        await github.UpdatePullRequestAsync(context.Owner, context.Repo, context.Number, title, body, cancellationToken)
            .ConfigureAwait(false);

        if (settings.PostEditComment) {
            var comment = CleanupFormatter.BuildEditComment(context, result);
            await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, comment, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string? Normalize(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return value.Trim();
    }
}
