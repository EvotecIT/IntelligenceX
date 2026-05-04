using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewAutoApprovalDecision {
    public bool Enabled { get; init; }
    public bool DryRun { get; init; }
    public bool ShouldApprove { get; init; }
    public IReadOnlyList<string> PassedGates { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    public ReviewCheckSnapshot? CheckSnapshot { get; init; }
    public ReviewCheckSnapshot? EffectiveCheckSnapshot { get; init; }
    public bool HasCheckData => CheckSnapshot?.HasData == true;
}

internal static class ReviewAutoApproval {
    private static readonly ReviewAutoApprovalDecision DisabledDecision = new() {
        Enabled = false,
        PassedGates = new[] { "auto-approval disabled" }
    };

    public static ReviewAutoApprovalDecision Evaluate(
        PullRequestContext context,
        ReviewSettings settings,
        bool reviewFailed,
        bool hasMergeBlockers,
        ReviewHistorySnapshot? history,
        bool? requiresConversationResolution,
        bool allowWrites,
        ReviewCheckSnapshot? checks,
        bool reviewThreadsUnavailable = false) {
        var auto = settings.AutoApprove;
        if (!auto.Enabled) {
            return DisabledDecision;
        }

        var blockers = new List<string>();
        var passed = new List<string>();

        if (allowWrites) {
            passed.Add("GitHub writes allowed");
        } else {
            blockers.Add("GitHub writes disabled");
        }

        AddLabelGate(context, auto, blockers, passed);
        AddAuthorGate(context, auto, blockers, passed);
        AddReviewGate(auto, reviewFailed, hasMergeBlockers, blockers, passed);
        AddThreadGate(auto, history, requiresConversationResolution, reviewThreadsUnavailable, blockers, passed);

        var effectiveChecks = checks is null ? null : FilterChecks(checks, auto.IgnoredCheckNames);
        AddCheckGate(auto, checks, effectiveChecks, blockers, passed);

        return new ReviewAutoApprovalDecision {
            Enabled = true,
            DryRun = auto.DryRun,
            ShouldApprove = blockers.Count == 0,
            PassedGates = passed,
            Blockers = blockers,
            CheckSnapshot = checks,
            EffectiveCheckSnapshot = effectiveChecks
        };
    }

    public static string BuildCommentBlock(ReviewAutoApprovalDecision decision) {
        if (!decision.Enabled) {
            return string.Empty;
        }

        var status = decision.ShouldApprove
            ? decision.DryRun
                ? "Eligible (dry run)"
                : "Eligible"
            : "Not eligible";
        var gates = decision.PassedGates.Count == 0 ? "none" : string.Join("<br>", decision.PassedGates.Select(EscapeCell));
        var blockers = decision.Blockers.Count == 0 ? "none" : string.Join("<br>", decision.Blockers.Select(EscapeCell));
        var checks = FormatChecks(decision.EffectiveCheckSnapshot ?? decision.CheckSnapshot);

        var sb = new StringBuilder();
        sb.AppendLine("## Auto-Approval Readiness 🤝");
        sb.AppendLine();
        sb.AppendLine("| Status | Checks | Passed gates | Blockers |");
        sb.AppendLine("| --- | --- | --- | --- |");
        sb.Append("| ");
        sb.Append(EscapeCell(status));
        sb.Append(" | ");
        sb.Append(EscapeCell(checks));
        sb.Append(" | ");
        sb.Append(gates);
        sb.Append(" | ");
        sb.Append(blockers);
        sb.AppendLine(" |");
        if (decision.ShouldApprove && decision.DryRun) {
            sb.AppendLine();
            sb.AppendLine("> Auto-approval dry run is enabled; no approving review was submitted.");
        }
        return sb.ToString().TrimEnd();
    }

    private static void AddLabelGate(PullRequestContext context, ReviewAutoApproveSettings auto,
        List<string> blockers, List<string> passed) {
        var labels = context.Labels ?? Array.Empty<string>();
        if (auto.BlockedLabels.Count > 0 &&
            labels.Any(label => auto.BlockedLabels.Contains(label, StringComparer.OrdinalIgnoreCase))) {
            blockers.Add("blocked by label");
            return;
        }

        if (auto.RequiredLabels.Count == 0) {
            passed.Add("label gate disabled");
            return;
        }

        if (labels.Any(label => auto.RequiredLabels.Contains(label, StringComparer.OrdinalIgnoreCase))) {
            passed.Add("required label present");
        } else {
            blockers.Add($"missing required label: {string.Join(", ", auto.RequiredLabels)}");
        }
    }

    private static void AddAuthorGate(PullRequestContext context, ReviewAutoApproveSettings auto,
        List<string> blockers, List<string> passed) {
        if (auto.AllowedAuthors.Count == 0) {
            passed.Add("author gate disabled");
            return;
        }

        var author = context.AuthorLogin;
        if (!string.IsNullOrWhiteSpace(author) &&
            auto.AllowedAuthors.Contains(author, StringComparer.OrdinalIgnoreCase)) {
            passed.Add("author allowed");
        } else {
            blockers.Add("author not allowed");
        }
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
        bool? requiresConversationResolution, bool reviewThreadsUnavailable, List<string> blockers, List<string> passed) {
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

    private static string FormatChecks(ReviewCheckSnapshot? snapshot) {
        if (snapshot?.HasData != true) {
            return "unavailable";
        }

        return $"{snapshot.PassedCount} passed, {snapshot.FailedCount} failed, {snapshot.PendingCount} pending";
    }

    private static string EscapeCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}
