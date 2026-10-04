namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static Task<string> RunAppServerReviewFixture(string scenario, int waitSeconds = 5) {
        var options = new IntelligenceXClientOptions { TransportKind = OpenAITransportKind.AppServer };
        var fixture = AppServerFixtureOptions(scenario);
        options.AppServerOptions.ExecutablePath = fixture.ExecutablePath;
        options.AppServerOptions.Arguments = fixture.Arguments;
        options.AppServerOptions.ConnectRetryCount = 0;
        options.AppServerOptions.ShutdownTimeout = fixture.ShutdownTimeout;
        var runner = new ReviewRunner(new ReviewSettings { WaitSeconds = waitSeconds, IdleSeconds = 1 });
        // Exercise the actual attempt with telemetry disabled and a hermetic process;
        // the outer provider setup otherwise enables the user's persistent usage database.
        var attempt = typeof(ReviewRunner).GetMethod("RunChatOnceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task<string>)attempt.Invoke(runner, new object?[] { options, "Review", null, null, CancellationToken.None, null })!;
    }

    private static void TestAppServerFinalReview() {
        var review = RunAppServerReviewFixture("review-completed");
        AwaitFixtureAsync(review).GetAwaiter().GetResult();
        AssertEqual("Final review", review.Result.Trim(), "final answer excludes progress commentary");
    }

    private static void TestAppServerInterruptedReview() {
        var review = RunAppServerReviewFixture("review-interrupted");
        AssertThrows<InvalidOperationException>(() => AwaitFixtureAsync(review).GetAwaiter().GetResult(), "interrupted review is rejected");
    }

    private static void TestAppServerReviewDeadline() {
        var review = RunAppServerReviewFixture("review-deadline", waitSeconds: 1);
        AssertEqual(true, Task.WhenAny(review, Task.Delay(AppServerFixtureGuard)).GetAwaiter().GetResult() == review,
            "review settles before the fixture guard");
        AssertThrows<TimeoutException>(() => review.GetAwaiter().GetResult(), "configured completion deadline");
    }

    private static void TestAppServerReviewProgressOnly() {
        var review = RunAppServerReviewFixture("review-progress-only");
        AwaitFixtureAsync(review).GetAwaiter().GetResult();
        AssertEqual(string.Empty, review.Result, "progress deltas are not a final review");
    }

    private static void TestAppServerReviewCommentaryOnly() {
        var review = RunAppServerReviewFixture("review-commentary-only");
        AwaitFixtureAsync(review).GetAwaiter().GetResult();
        AssertEqual(string.Empty, review.Result, "explicit commentary is not a final review");
    }

    private static void TestAppServerReviewUnphased() {
        var review = RunAppServerReviewFixture("review-unphased");
        AwaitFixtureAsync(review).GetAwaiter().GetResult();
        AssertEqual("Final review", review.Result, "unphased final items retain compatibility");
    }
}
#endif
