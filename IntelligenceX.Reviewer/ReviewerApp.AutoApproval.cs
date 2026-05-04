using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    private static async Task<ReviewAutoApprovalDecision> BuildAutoApprovalDecisionAsync(
        GitHubClient github,
        GitHubClient? fallbackGithub,
        PullRequestContext context,
        ReviewSettings settings,
        bool reviewFailed,
        bool hasMergeBlockers,
        ReviewHistorySnapshot? history,
        bool? requiresConversationResolution,
        bool allowWrites,
        CancellationToken cancellationToken) {
        ReviewCheckSnapshot? checks = null;
        if (settings.AutoApprove.Enabled && settings.AutoApprove.RequireChecksPass) {
            try {
                var readGithub = fallbackGithub ?? github;
                checks = await readGithub.GetCheckSnapshotAsync(context.Owner, context.Repo, context.HeadSha, cancellationToken)
                    .ConfigureAwait(false);
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                if (settings.Diagnostics) {
                    Console.Error.WriteLine($"Auto-approval check-status lookup failed open: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        return ReviewAutoApproval.Evaluate(context, settings, reviewFailed, hasMergeBlockers, history,
            requiresConversationResolution, allowWrites, checks);
    }

    private static async Task SubmitAutoApprovalIfEligibleAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, ReviewAutoApprovalDecision decision, CancellationToken cancellationToken) {
        if (!decision.Enabled || !decision.ShouldApprove) {
            return;
        }

        if (decision.DryRun) {
            Console.WriteLine("Auto-approval dry run: approving review was not submitted.");
            return;
        }

        try {
            await github.CreatePullRequestReviewAsync(context.Owner, context.Repo, context.Number,
                    settings.AutoApprove.Body, "APPROVE", cancellationToken)
                .ConfigureAwait(false);
            Console.WriteLine("Submitted approving pull request review.");
        } catch (OperationCanceledException) {
            throw;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Auto-approval submission failed open: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string CombineCommentBlocks(params string?[] blocks) {
        var values = blocks
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .Select(block => block!.Trim())
            .ToArray();
        return values.Length == 0 ? string.Empty : string.Join("\n\n", values);
    }
}
