using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Reviewer;

internal static partial class ReviewAutoApproval {
    private static bool AddLabelGate(PullRequestContext context, ReviewAutoApproveSettings auto,
        List<string> blockers, List<string> passed) {
        var labels = context.Labels ?? Array.Empty<string>();
        if (auto.BlockedLabels.Count > 0 &&
            labels.Any(label => auto.BlockedLabels.Contains(label, StringComparer.OrdinalIgnoreCase))) {
            blockers.Add("blocked by label");
            return false;
        }

        if (auto.RequiredLabels.Count == 0) {
            passed.Add("label gate disabled");
            return true;
        }

        if (labels.Any(label => auto.RequiredLabels.Contains(label, StringComparer.OrdinalIgnoreCase))) {
            passed.Add("required label present");
            return true;
        }

        blockers.Add($"missing required label: {string.Join(", ", auto.RequiredLabels)}");
        return false;
    }

    private static bool AddAuthorGate(PullRequestContext context, ReviewAutoApproveSettings auto,
        List<string> blockers, List<string> passed) {
        if (auto.AllowedAuthors.Count == 0) {
            passed.Add("author gate disabled");
            return true;
        }

        var author = context.AuthorLogin;
        if (!string.IsNullOrWhiteSpace(author) &&
            auto.AllowedAuthors.Contains(author, StringComparer.OrdinalIgnoreCase)) {
            passed.Add("author allowed");
            return true;
        }

        blockers.Add("author not allowed");
        return false;
    }

    private static void AddReviewGate(ReviewAutoApproveSettings auto, bool reviewFailed, bool hasMergeBlockers,
        List<string> blockers, List<string> passed) {
        if (auto.RequireReviewSuccess) {
            if (reviewFailed) {
                blockers.Add("review provider returned a failure body");
            } else {
                passed.Add("review succeeded");
            }
        }

        if (auto.RequireNoMergeBlockers) {
            if (hasMergeBlockers) {
                blockers.Add("merge blockers detected");
            } else {
                passed.Add("no merge blockers");
            }
        }
    }

    private static void AddThreadGate(ReviewAutoApproveSettings auto, ReviewHistorySnapshot? history,
        bool reviewThreadsUnavailable, List<string> blockers, List<string> passed) {
        if (!auto.RequireNoActiveReviewThreads) {
            passed.Add("review-thread gate disabled");
            return;
        }

        if (reviewThreadsUnavailable) {
            blockers.Add("review thread state unavailable");
            return;
        }

        var snapshot = history?.ThreadSnapshot;
        if (snapshot is not null) {
            if (snapshot.ActiveCount > 0) {
                blockers.Add($"{snapshot.ActiveCount} active review thread(s)");
            } else {
                passed.Add("no active review threads");
            }
            return;
        }

        blockers.Add("review thread state unavailable");
    }

    private static void AddCheckGate(ReviewAutoApproveSettings auto, ReviewCheckSnapshot? rawChecks,
        ReviewCheckSnapshot? effectiveChecks, List<string> blockers, List<string> passed) {
        if (!auto.RequireChecksPass && !auto.RequireNoPendingChecks) {
            // Only bypass check data when both check-related approval gates are explicitly disabled.
            passed.Add("check gate disabled");
            return;
        }

        if (rawChecks?.HasData != true || effectiveChecks is null) {
            blockers.Add("check status unavailable");
            return;
        }

        if (!effectiveChecks.HasData) {
            blockers.Add("no effective checks after ignored check filtering");
            return;
        }

        if (auto.RequireChecksPass && effectiveChecks.FailedCount > 0) {
            blockers.Add($"{effectiveChecks.FailedCount} failing check(s)");
        }

        if (auto.RequireNoPendingChecks && effectiveChecks.PendingCount > 0) {
            blockers.Add($"{effectiveChecks.PendingCount} pending check(s)");
        }

        if (auto.RequireChecksPass && effectiveChecks.FailedCount == 0) {
            passed.Add("checks passed");
        }

        if (auto.RequireNoPendingChecks && effectiveChecks.PendingCount == 0) {
            passed.Add("no pending checks");
        }
    }

    private static ReviewCheckSnapshot FilterChecks(ReviewCheckSnapshot snapshot, IReadOnlyList<string> ignoredNames) {
        if (snapshot.Runs.Count == 0 || ignoredNames.Count == 0) {
            return snapshot;
        }

        var runs = snapshot.Runs
            .Where(run => !ignoredNames.Contains(run.Name, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return new ReviewCheckSnapshot(runs);
    }
}
