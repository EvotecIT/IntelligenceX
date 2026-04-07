namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {

    private static void TestAutoResolveMissingInlineGateAllowsEmptySet() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMissingInline = true
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shouldRun = CallShouldAutoResolveMissingInlineThreads(settings, context, keys, inlineCommentsCount: 0);
        AssertEqual(true, shouldRun, "auto resolve missing inline gate empty set");
    }

    private static void TestAutoResolveMissingInlineGateRejectsNull() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMissingInline = true
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var shouldRun = CallShouldAutoResolveMissingInlineThreads(settings, context, null, inlineCommentsCount: 0);
        AssertEqual(false, shouldRun, "auto resolve missing inline gate null set");
    }

    private static void TestAutoResolveMissingInlineGateRejectsEmptyWhenInlineCommentsPresent() {
        var settings = new ReviewSettings {
            ReviewThreadsAutoResolveMissingInline = true
        };
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shouldRun = CallShouldAutoResolveMissingInlineThreads(settings, context, keys, inlineCommentsCount: 1);
        AssertEqual(false, shouldRun, "auto resolve missing inline gate empty mapped keys");
    }

    private static void TestReviewRetryTransient() {
        var attempts = 0;
        var result = ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                attempts++;
                if (attempts < 3) {
                    throw new IOException("transient");
                }
                return Task.FromResult("ok");
            },
            ex => ex is IOException,
            maxAttempts: 3,
            retryDelaySeconds: 1,
            retryMaxDelaySeconds: 1,
            backoffMultiplier: 2,
            retryJitterMinMs: 0,
            retryJitterMaxMs: 0,
            CancellationToken.None,
            describeError: null,
            extraAttempts: 0,
            extraRetryPredicate: null,
            retryState: null).GetAwaiter().GetResult();

        AssertEqual("ok", result, "retry result");
        AssertEqual(3, attempts, "retry attempts");
    }

    private static void TestReviewRetryNonTransient() {
        var attempts = 0;
        var thrown = false;
        try {
            ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                    attempts++;
                    throw new InvalidOperationException("nope");
                },
                ex => ex is IOException,
                maxAttempts: 3,
                retryDelaySeconds: 1,
                retryMaxDelaySeconds: 1,
                backoffMultiplier: 2,
                retryJitterMinMs: 0,
                retryJitterMaxMs: 0,
                CancellationToken.None,
                describeError: null,
                extraAttempts: 0,
                extraRetryPredicate: null,
                retryState: null).GetAwaiter().GetResult();
        } catch (InvalidOperationException) {
            thrown = true;
        }

        AssertEqual(true, thrown, "non-transient thrown");
        AssertEqual(1, attempts, "non-transient attempts");
    }

    private static void TestReviewRetryRethrows() {
        var attempts = 0;
        var ex = new IOException("boom");
        try {
            ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                    attempts++;
                    throw ex;
                },
                _ => true,
                maxAttempts: 2,
                retryDelaySeconds: 1,
                retryMaxDelaySeconds: 1,
                backoffMultiplier: 2,
                retryJitterMinMs: 0,
                retryJitterMaxMs: 0,
                CancellationToken.None,
                describeError: null,
                extraAttempts: 0,
                extraRetryPredicate: null,
                retryState: null).GetAwaiter().GetResult();
            throw new InvalidOperationException("Expected exception.");
        } catch (IOException caught) {
            AssertEqual(true, ReferenceEquals(ex, caught), "retry exception instance");
        }

        AssertEqual(2, attempts, "retry attempts rethrow");
    }

    private static void TestReviewRetryExtraAttempt() {
        var attempts = 0;
        var result = ReviewRunner.ReviewRetryPolicy.RunAsync(() => {
                attempts++;
                if (attempts == 1) {
                    throw new IOException("ResponseEnded");
                }
                return Task.FromResult("ok");
            },
            ex => ex is IOException,
            maxAttempts: 1,
            retryDelaySeconds: 1,
            retryMaxDelaySeconds: 1,
            backoffMultiplier: 2,
            retryJitterMinMs: 0,
            retryJitterMaxMs: 0,
            CancellationToken.None,
            describeError: null,
            extraAttempts: 1,
            extraRetryPredicate: ReviewDiagnostics.IsResponseEnded,
            retryState: null).GetAwaiter().GetResult();

        AssertEqual("ok", result, "retry extra result");
        AssertEqual(2, attempts, "retry extra attempts");
    }

    private static void TestReviewFailureMarker() {
        var settings = new ReviewSettings { Diagnostics = true };
        var body = ReviewDiagnostics.BuildFailureBody(new IOException("ResponseEnded"), settings, null, null);
        AssertEqual(true, ReviewDiagnostics.IsFailureBody(body), "failure marker");
    }

    private static void TestReviewFailureBodyRedactsErrors() {
        var settings = new ReviewSettings { Diagnostics = true };
        var body = ReviewDiagnostics.BuildFailureBody(new InvalidOperationException("Sensitive info"), settings, null, null);
        if (body.Contains("Sensitive info", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected failure body to omit raw exception details.");
        }
    }

    private static void TestReviewFailureBodyIncludesSafeAuthRefreshDetail() {
        var settings = new ReviewSettings { Diagnostics = true };
        var ex = new InvalidOperationException("OAuth token request failed (401): refresh_token_reused. Your refresh token has already been used to generate a new access token. Please try signing in again.");
        var classification = ReviewDiagnostics.Classify(ex);
        AssertEqual(ReviewDiagnostics.ReviewErrorCategory.Auth, classification.Category, "auth refresh classification");
        AssertEqual("OpenAI auth refresh token was already used; sign in again", classification.Summary, "auth refresh classification summary");

        var body = ReviewDiagnostics.BuildFailureBody(ex, settings, null, null, "owner/repo");
        AssertContainsText(body, "- Detail: OpenAI auth refresh token was already used; sign in again", "auth refresh failure detail");
        AssertContainsText(body, "intelligencex auth login --set-github-secret --repo owner/repo", "auth refresh remediation command");
        if (body.Contains("refresh_token_reused", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Your refresh token has already been used to generate a new access token", StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException("Expected failure body to keep raw provider auth payload out of the PR summary.");
        }
    }

    private static void TestBuildAuthRemediationCommandQuotesRepoWhenNeeded() {
        var command = ReviewDiagnostics.BuildAuthRemediationCommand("owner/repo with space");
        AssertContainsText(command, "--repo \"owner/repo with space\"", "quoted auth remediation repo");
    }

    private static void TestWorkflowFailOpenLogClassificationUsesAuthRefreshLabel() {
        var failure = ReviewDiagnostics.ClassifyWorkflowFailureLog(
            "OAuth token request failed (401): refresh_token_reused. Your refresh token has already been used.");
        AssertEqual("openai-auth-refresh-reused", failure.Kind, "workflow failure kind");
        AssertEqual("OpenAI auth refresh token was already used", failure.Label, "workflow failure label");
        AssertEqual(true, failure.RequiresAuthRemediation, "workflow failure remediation flag");
    }

    private static void TestWorkflowFailOpenSummaryBodyUsesRuntimeGuidance() {
        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Workflow hardening", null, false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var failure = ReviewDiagnostics.ClassifyWorkflowFailureLog("Unhandled exception: reviewer-runtime");
        var body = ReviewDiagnostics.BuildWorkflowFailOpenSummaryBody(context, "source", "owner/repo", failure);

        AssertContainsText(body, "## IntelligenceX Review (failed open)", "workflow fail-open heading");
        AssertContainsText(body, "Reviewing this pull request: **Workflow hardening**", "workflow fail-open title");
        AssertContainsText(body, "- Reviewer source: source", "workflow fail-open reviewer source");
        AssertContainsText(body, "Check the `review / review` workflow logs for the runtime failure", "workflow fail-open runtime guidance");
        AssertEqual(false, body.Contains("intelligencex auth login --set-github-secret", StringComparison.OrdinalIgnoreCase),
            "workflow fail-open runtime guidance omits auth remediation");
    }

    private static void TestFailureSummaryCommentUpdate() {
        var commentId = 42L;
        string? body = null;
        var hits = 0;
        using var server = new LocalHttpServer(request => {
            if (!string.Equals(request.Method, "PATCH", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            if (!string.Equals(request.Path, $"/repos/owner/repo/issues/comments/{commentId}", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            hits++;
            body = request.Body;
            return new HttpResponse("{\"id\":42,\"body\":\"ok\"}");
        });

        var context = new PullRequestContext("owner/repo", "owner", "repo", 1, "Title", "Body", false, "head", "base",
            Array.Empty<string>(), "owner/repo", false, null);
        var settings = new ReviewSettings {
            Provider = ReviewProvider.OpenAI,
            OpenAITransport = OpenAITransportKind.Native
        };

        var updated = IntelligenceX.Reviewer.ReviewerApp.TryUpdateFailureSummaryAsync("token", server.BaseUri.ToString().TrimEnd('/'),
                context, settings, commentId, new InvalidOperationException("boom"), false)
            .GetAwaiter().GetResult();
        AssertEqual(true, updated, "failure summary update");
        AssertEqual(1, hits, "failure summary update hits");
        AssertContainsText(body ?? string.Empty, ReviewDiagnostics.FailureMarker, "failure summary marker");
    }

    private static void TestReviewFailOpenTransientOnly() {
        var transient = new HttpRequestException("network");
        var responseEnded = new IOException("ResponseEnded");
        var nonTransient = new InvalidOperationException("logic");
        AssertEqual(true, ReviewRunner.IsTransient(transient), "transient true");
        AssertEqual(true, ReviewRunner.IsTransient(responseEnded), "response ended transient");
        AssertEqual(false, ReviewRunner.IsTransient(nonTransient), "non-transient false");
    }

    private static void TestReviewFailOpenDecision() {
        var transient = new HttpRequestException("network");
        var nonTransient = new InvalidOperationException("logic");
        var settings = new ReviewSettings {
            FailOpen = true,
            FailOpenTransientOnly = true
        };
        AssertEqual(true, ReviewRunner.ShouldFailOpen(settings, transient), "fail-open transient");
        AssertEqual(false, ReviewRunner.ShouldFailOpen(settings, nonTransient), "fail-open non-transient gated");

        settings.FailOpenTransientOnly = false;
        AssertEqual(true, ReviewRunner.ShouldFailOpen(settings, nonTransient), "fail-open non-transient allowed");
    }

    private static void TestPreflightTimeout() {
        using var server = new LocalHttpServer(_ => {
            Thread.Sleep(200);
            return new HttpResponse("{}");
        });
        var options = new OpenAINativeOptions {
            ChatGptApiBaseUrl = server.BaseUri.ToString().TrimEnd('/')
        };
        try {
            CallPreflightNativeConnectivity(options, TimeSpan.FromMilliseconds(50));
            throw new InvalidOperationException("Expected timeout.");
        } catch (TimeoutException) {
            // expected
        }
    }
}
#endif
