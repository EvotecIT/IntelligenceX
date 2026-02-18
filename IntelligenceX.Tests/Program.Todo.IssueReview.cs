namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestIssueReviewExtractPullRequestReferencesParsesMultipleForms() {
        var refs = IntelligenceX.Cli.Todo.IssueReviewRunner.ExtractPullRequestReferences(
            "EvotecIT/IntelligenceX",
            "Infra blocker: PR #438 checks stuck. Also see https://github.com/EvotecIT/IntelligenceX/pull/359 and PR EvotecIT/IntelligenceX#438.");

        AssertEqual(2, refs.Count, "unique refs count");
        AssertEqual(359, refs[0], "first ref number");
        AssertEqual(438, refs[1], "second ref number");
    }

    private static void TestIssueReviewAssessIssueForApplicabilityMarksResolvedInfraBlockerAsNoLongerApplicable() {
        var now = DateTimeOffset.UtcNow;
        var issue = new IntelligenceX.Cli.Todo.IssueReviewRunner.IssueReviewCandidateIssue(
            Number: 440,
            Title: "Infra blocker: PR #438 checks stuck in Setup .NET",
            Body: "Tracking after retries.",
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/440",
            UpdatedAtUtc: now.AddDays(-2),
            Labels: new[] { "infra-blocker" });
        var pullRequests = new Dictionary<int, IntelligenceX.Cli.Todo.IssueReviewRunner.PullRequestReference> {
            [438] = new(
                Number: 438,
                Title: "Fix setup checks",
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/438",
                State: "MERGED",
                MergedAtUtc: now.AddDays(-1),
                ClosedAtUtc: now.AddDays(-1))
        };

        var assessment = IntelligenceX.Cli.Todo.IssueReviewRunner.AssessIssueForApplicability(
            issue,
            "EvotecIT/IntelligenceX",
            pullRequests,
            now,
            staleDays: 14);

        AssertEqual(true, assessment.IsInfraBlocker, "infra blocker detection");
        AssertEqual("no-longer-applicable", assessment.Classification, "classification");
        AssertEqual(true, assessment.EligibleForAutoClose, "eligible for auto close");
        AssertEqual(true, assessment.Reason.Contains("resolved", StringComparison.OrdinalIgnoreCase), "resolved reason text");
    }
#endif
}
