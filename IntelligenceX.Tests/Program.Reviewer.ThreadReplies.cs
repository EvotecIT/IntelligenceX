namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static void TestThreadAssessmentCandidatesSkipStaticAnalysisInlineThreads() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveBotsOnly = true,
            ReviewThreadsAutoResolveMax = 10
        };
        var staticAnalysisThread = new PullRequestReviewThread("thread-static", false, false, 1, new[] {
            new PullRequestReviewThreadComment(
                1,
                null,
                $"{ReviewFormatter.InlineMarker}\nStatic analysis (warning): File has 2799 lines (limit 700). Split into smaller units. (rule IXLOC001)",
                "intelligencex-review",
                "src/Foo.cs",
                10)
        });
        var actionableThread = new PullRequestReviewThread("thread-real", false, false, 1, new[] {
            new PullRequestReviewThreadComment(2, null, "Please add a null guard.", "intelligencex-review", "src/Bar.cs", 12)
        });

        var candidates = CallSelectAssessmentCandidates(new[] { staticAnalysisThread, actionableThread }, settings);
        AssertEqual(1, candidates.Count, "thread assessment skips static analysis inline thread");
        AssertEqual("thread-real", candidates[0].Id, "thread assessment keeps actionable thread");
    }

    private static void TestThreadAssessmentCandidatesScanAllInlineCommentsForStaticAnalysisMarker() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveBotsOnly = true,
            ReviewThreadsAutoResolveMax = 10
        };
        var mixedInlineThread = new PullRequestReviewThread("thread-static", false, false, 2, new[] {
            new PullRequestReviewThreadComment(
                1,
                null,
                $"{ReviewFormatter.InlineMarker}\nPlease add a null guard.",
                "intelligencex-review",
                "src/Foo.cs",
                10),
            new PullRequestReviewThreadComment(
                2,
                null,
                $"{ReviewFormatter.InlineMarker}\n{ReviewFormatter.StaticAnalysisInlineMarker}\nStatic analysis (warning): File has 2799 lines (limit 700). Split into smaller units. (rule IXLOC001)",
                "intelligencex-review",
                "src/Foo.cs",
                10)
        });
        var actionableThread = new PullRequestReviewThread("thread-real", false, false, 1, new[] {
            new PullRequestReviewThreadComment(3, null, "Please add a null guard.", "intelligencex-review", "src/Bar.cs", 12)
        });

        var candidates = CallSelectAssessmentCandidates(new[] { mixedInlineThread, actionableThread }, settings);
        AssertEqual(1, candidates.Count, "thread assessment scans all inline comments for marker");
        AssertEqual("thread-real", candidates[0].Id, "thread assessment keeps actionable thread when mixed thread is skipped");
    }

    private static void TestThreadAssessmentCandidatesLegacyStaticAnalysisRequiresTrustedAuthor() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveBotsOnly = false,
            ReviewThreadsAutoResolveMax = 10
        };
        var untrustedLegacyThread = new PullRequestReviewThread("thread-untrusted", false, false, 1, new[] {
            new PullRequestReviewThreadComment(
                1,
                null,
                $"{ReviewFormatter.InlineMarker}\nStatic analysis (warning): File has 2799 lines (limit 700). Split into smaller units. (rule IXLOC001)",
                "alice",
                "src/Foo.cs",
                10)
        });
        var actionableThread = new PullRequestReviewThread("thread-real", false, false, 1, new[] {
            new PullRequestReviewThreadComment(2, null, "Please add a null guard.", "intelligencex-review", "src/Bar.cs", 12)
        });

        var candidates = CallSelectAssessmentCandidates(new[] { untrustedLegacyThread, actionableThread }, settings);
        AssertEqual(2, candidates.Count, "legacy static analysis fallback requires trusted author");
    }

    private static void TestReplyToKeptThreadsSkipsStaticAnalysisInlineThreads() {
        var replyRequests = 0;
        using var server = new LocalHttpServer(request => {
            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ||
                !request.Path.Equals("/repos/owner/repo/pulls/1/comments", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }

            replyRequests++;
            return new HttpResponse("{\"id\":123}", null, 201, "Created");
        });
        using var github = new GitHubClient("token", server.BaseUri.ToString().TrimEnd('/'));

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "headsha",
            "basesha", Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings { ReviewThreadsAutoResolveMax = 10 };
        var staticAnalysisThread = new PullRequestReviewThread("thread-static", false, false, 2, new[] {
            new PullRequestReviewThreadComment(
                41,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                $"{ReviewFormatter.InlineMarker}\nPlease add a null guard.",
                "intelligencex-review",
                "src/Foo.cs",
                10),
            new PullRequestReviewThreadComment(
                42,
                DateTimeOffset.UtcNow,
                $"{ReviewFormatter.InlineMarker}\n{ReviewFormatter.StaticAnalysisInlineMarker}\nStatic analysis (warning): File has 2799 lines (limit 700). Split into smaller units. (rule IXLOC001)",
                "intelligencex-review",
                "src/Foo.cs",
                10)
        });
        var assessments = new Dictionary<string, ReviewerApp.ThreadAssessment>(StringComparer.OrdinalIgnoreCase) {
            ["thread-static"] = new("thread-static", "keep", "still applicable", string.Empty)
        };

        CallReplyToKeptThreads(github, context, new[] { staticAnalysisThread }, assessments, context.HeadSha, "current PR files",
            settings);
        AssertEqual(0, replyRequests, "reply path skips static analysis inline thread");
    }
}
#endif
