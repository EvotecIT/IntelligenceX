using System;
using System.Linq;
using System.Text;

namespace IntelligenceX.Reviewer;

internal sealed class ReviewAutoApprovalDecision {
    public bool Enabled { get; init; }
    public bool DryRun { get; init; }
    public bool DisplayReadiness { get; init; }
    public bool ShouldApprove { get; init; }
    public IReadOnlyList<string> PassedGates { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();
    public ReviewCheckSnapshot? CheckSnapshot { get; init; }
    public ReviewCheckSnapshot? EffectiveCheckSnapshot { get; init; }
    public bool HasCheckData => (EffectiveCheckSnapshot ?? CheckSnapshot)?.HasData == true;
}

internal static partial class ReviewAutoApproval {
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

        var labelAllowed = AddLabelGate(context, auto, blockers, passed);
        var authorAllowed = AddAuthorGate(context, auto, blockers, passed);
        AddReviewGate(auto, reviewFailed, hasMergeBlockers, blockers, passed);
        AddThreadGate(auto, history, reviewThreadsUnavailable, blockers, passed);

        var effectiveChecks = checks is null ? null : FilterChecks(checks, auto.IgnoredCheckNames);
        AddCheckGate(auto, checks, effectiveChecks, blockers, passed);

        return new ReviewAutoApprovalDecision {
            Enabled = true,
            DryRun = auto.DryRun,
            // Display the readiness table after operator-controlled gates pass; later gates still decide approval.
            DisplayReadiness = allowWrites && labelAllowed && authorAllowed,
            ShouldApprove = blockers.Count == 0,
            PassedGates = passed,
            Blockers = blockers,
            CheckSnapshot = checks,
            EffectiveCheckSnapshot = effectiveChecks
        };
    }

    public static string BuildCommentBlock(ReviewAutoApprovalDecision decision) {
        if (!decision.Enabled || !decision.DisplayReadiness) {
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
        } else if (decision.ShouldApprove) {
            sb.AppendLine();
            sb.AppendLine(
                "> Auto-approval submission runs after this sticky summary is posted or updated; final submitted/skipped/failed status is recorded in workflow logs.");
        }
        return sb.ToString().TrimEnd();
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
