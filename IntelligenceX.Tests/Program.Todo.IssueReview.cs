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

    private static void TestIssueReviewAssessIssueForApplicabilityRequiresConsecutiveCandidatesForAutoClose() {
        var now = DateTimeOffset.UtcNow;
        var issue = new IntelligenceX.Cli.Todo.IssueReviewRunner.IssueReviewCandidateIssue(
            Number: 362,
            Title: "Infra blocker: PR #359 checks failing with no space left on device",
            Body: "Tracking infra incident.",
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/362",
            UpdatedAtUtc: now.AddDays(-1),
            Labels: new[] { "infra-blocker" });
        var pullRequests = new Dictionary<int, IntelligenceX.Cli.Todo.IssueReviewRunner.PullRequestReference> {
            [359] = new(
                Number: 359,
                Title: "Runner disk cleanup",
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/359",
                State: "MERGED",
                MergedAtUtc: now.AddDays(-1),
                ClosedAtUtc: now.AddDays(-1))
        };
        var policy = IntelligenceX.Cli.Todo.IssueReviewRunner.BuildPolicy(Array.Empty<string>(), Array.Empty<string>());

        var firstAssessment = IntelligenceX.Cli.Todo.IssueReviewRunner.AssessIssueForApplicability(
            issue,
            "EvotecIT/IntelligenceX",
            pullRequests,
            now,
            staleDays: 14,
            policy,
            previousCandidateStreak: 0,
            minConsecutiveCandidatesForClose: 2);
        var secondAssessment = IntelligenceX.Cli.Todo.IssueReviewRunner.AssessIssueForApplicability(
            issue,
            "EvotecIT/IntelligenceX",
            pullRequests,
            now,
            staleDays: 14,
            policy,
            previousCandidateStreak: 1,
            minConsecutiveCandidatesForClose: 2);

        AssertEqual("no-longer-applicable", firstAssessment.Classification, "first classification");
        AssertEqual(false, firstAssessment.EligibleForAutoClose, "first eligibility");
        AssertEqual(1, firstAssessment.CandidateStreak, "first streak");
        AssertEqual(true, firstAssessment.Reason.Contains("1/2", StringComparison.OrdinalIgnoreCase), "first streak reason");
        AssertEqual(true, secondAssessment.EligibleForAutoClose, "second eligibility");
        AssertEqual(2, secondAssessment.CandidateStreak, "second streak");
    }

    private static void TestIssueReviewAssessIssueForApplicabilityRespectsAllowAndDenyLabelPolicy() {
        var now = DateTimeOffset.UtcNow;
        var issueMissingAllow = new IntelligenceX.Cli.Todo.IssueReviewRunner.IssueReviewCandidateIssue(
            Number: 440,
            Title: "Infra blocker: PR #438 checks stuck in Setup .NET",
            Body: "Tracking after retries.",
            Url: "https://github.com/EvotecIT/IntelligenceX/issues/440",
            UpdatedAtUtc: now.AddDays(-2),
            Labels: new[] { "infra-blocker" });
        var issueDenied = issueMissingAllow with {
            Number = 441,
            Url = "https://github.com/EvotecIT/IntelligenceX/issues/441",
            Labels = new[] { "infra-blocker", "ops-hold" }
        };
        var pullRequests = new Dictionary<int, IntelligenceX.Cli.Todo.IssueReviewRunner.PullRequestReference> {
            [438] = new(
                Number: 438,
                Title: "Fix setup checks",
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/438",
                State: "MERGED",
                MergedAtUtc: now.AddDays(-1),
                ClosedAtUtc: now.AddDays(-1))
        };
        var policy = IntelligenceX.Cli.Todo.IssueReviewRunner.BuildPolicy(
            new[] { "close-approved" },
            new[] { "ops-hold" });

        var missingAllowAssessment = IntelligenceX.Cli.Todo.IssueReviewRunner.AssessIssueForApplicability(
            issueMissingAllow,
            "EvotecIT/IntelligenceX",
            pullRequests,
            now,
            staleDays: 14,
            policy,
            previousCandidateStreak: 3,
            minConsecutiveCandidatesForClose: 2);
        var deniedAssessment = IntelligenceX.Cli.Todo.IssueReviewRunner.AssessIssueForApplicability(
            issueDenied,
            "EvotecIT/IntelligenceX",
            pullRequests,
            now,
            staleDays: 14,
            policy,
            previousCandidateStreak: 3,
            minConsecutiveCandidatesForClose: 2);

        AssertEqual("no-longer-applicable", missingAllowAssessment.Classification, "missing allow classification");
        AssertEqual(false, missingAllowAssessment.EligibleForAutoClose, "missing allow eligibility");
        AssertEqual(true, missingAllowAssessment.Reason.Contains("Missing allow label", StringComparison.OrdinalIgnoreCase), "missing allow reason");
        AssertEqual("needs-review", deniedAssessment.Classification, "denied classification");
        AssertEqual(false, deniedAssessment.EligibleForAutoClose, "denied eligibility");
        AssertEqual(true, deniedAssessment.Reason.Contains("Denied/protected", StringComparison.OrdinalIgnoreCase), "denied reason");
    }
#endif
}
