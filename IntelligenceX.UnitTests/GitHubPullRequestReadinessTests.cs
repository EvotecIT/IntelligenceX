using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using IntelligenceX.Telemetry.GitHub;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class GitHubPullRequestReadinessTests {
    private const string Head = "1111111111111111111111111111111111111111";
    private const string OtherHead = "2222222222222222222222222222222222222222";

    [Fact]
    public async Task Readiness_PaginatesEveryConnectionAndCountsAllUnresolvedThreads() {
        using var fixture = new Fixture();
        fixture.Page("contexts", null, "checks-2", Check("build", "SUCCESS"));
        fixture.Page("contexts", "checks-2", null, Check("test", "FAILURE"));
        fixture.Page("reviewThreads", null, "threads-2", Thread("src/one.cs", "first\n comment", false), Thread("src/resolved.cs", "done", true));
        fixture.Page("reviewThreads", "threads-2", "threads-3", Enumerable.Range(2, 5).Select(index => Thread("src/" + index + ".cs", new string('x', 300), false, true)).ToArray());
        fixture.Page("reviewThreads", "threads-3", null, Thread("src/last.cs", "last", false));
        fixture.Page("reviews", null, "reviews-2", Review("reviewer-a", "COMMENTED", "2026-01-01T10:00:00Z", OtherHead));
        fixture.Page("reviews", "reviews-2", null, Review("reviewer-a", "APPROVED", "2026-01-01T12:00:00Z", Head));
        fixture.Page("reviewRequests", null, "requests-2");
        fixture.Page("reviewRequests", "requests-2", null, JsonNode.Parse("""{"requestedReviewer":{"__typename":"Team","combinedSlug":"sample/reviewers"}}""")!);
        fixture.Page("reactions", null, "eyes-2", Eyes("reviewer-a", "2026-01-01T11:00:00Z"));
        fixture.Page("reactions", "eyes-2", null, Eyes("reviewer-b", "2026-01-01T13:00:00Z"));

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(Head, report.HeadSha);
        Assert.Equal("feature/sample", report.HeadBranch);
        Assert.Equal(Head, report.ObservedHeadSha);
        Assert.Equal(GitHubReadinessVerdict.Blocked, report.Verdict);
        Assert.Equal(GitHubReadinessCheckState.Failing, report.ChecksState);
        Assert.Equal(new[] { "build", "test" }, report.Checks.Select(check => check.Name));
        Assert.Equal(7, report.UnresolvedThreadCount);
        Assert.Equal(5, report.UnresolvedThreads.Count);
        Assert.Equal("first comment", report.UnresolvedThreads[0].Text);
        Assert.Equal("src/one.cs", report.UnresolvedThreads[0].Path);
        Assert.True(report.UnresolvedThreads[1].IsOutdated);
        Assert.Equal(240, report.UnresolvedThreads[1].Text.Length);
        Assert.Equal(new[] { "reviewer-b", "sample/reviewers" }, report.PendingReviewers.Select(reviewer => reviewer.Actor));
        var review = Assert.Single(report.Reviews);
        Assert.Equal("APPROVED", review.State);
        Assert.True(review.IsCurrentHead);
        Assert.Equal(Head, review.CommitSha);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T13:00:00Z"), report.PendingReviewers[0].SinceUtc);
        Assert.Contains("checks-failing", report.Reasons);
        Assert.Contains("unresolved-review-threads", report.Reasons);
        Assert.All(new[] { "contexts", "reviews", "reviewRequests", "reactions" }, field => Assert.Equal(2, fixture.Reads.Count(read => read.Field == field)));
        Assert.Equal(3, fixture.Reads.Count(read => read.Field == "reviewThreads"));
        Assert.All(fixture.Reads.Where(read => read.Field == "contexts"), read => Assert.Equal(Head, read.Variables["head"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("CheckRun", "COMPLETED", "SUCCESS", GitHubReadinessCheckState.Green, GitHubReadinessVerdict.Ready)]
    [InlineData("CheckRun", "COMPLETED", "NEUTRAL", GitHubReadinessCheckState.Green, GitHubReadinessVerdict.Ready)]
    [InlineData("CheckRun", "COMPLETED", "SKIPPED", GitHubReadinessCheckState.Green, GitHubReadinessVerdict.Ready)]
    [InlineData("CheckRun", "COMPLETED", "TIMED_OUT", GitHubReadinessCheckState.Failing, GitHubReadinessVerdict.Blocked)]
    [InlineData("CheckRun", "COMPLETED", "CANCELLED", GitHubReadinessCheckState.Failing, GitHubReadinessVerdict.Blocked)]
    [InlineData("CheckRun", "COMPLETED", "ACTION_REQUIRED", GitHubReadinessCheckState.Failing, GitHubReadinessVerdict.Blocked)]
    [InlineData("CheckRun", "IN_PROGRESS", null, GitHubReadinessCheckState.Pending, GitHubReadinessVerdict.Waiting)]
    [InlineData("CheckRun", "QUEUED", null, GitHubReadinessCheckState.Pending, GitHubReadinessVerdict.Waiting)]
    [InlineData("CheckRun", "COMPLETED", "STALE", GitHubReadinessCheckState.Pending, GitHubReadinessVerdict.Waiting)]
    [InlineData("CheckRun", "COMPLETED", null, GitHubReadinessCheckState.Unknown, GitHubReadinessVerdict.Waiting)]
    [InlineData("CheckRun", "FUTURE_STATE", null, GitHubReadinessCheckState.Unknown, GitHubReadinessVerdict.Waiting)]
    [InlineData("StatusContext", "ERROR", null, GitHubReadinessCheckState.Failing, GitHubReadinessVerdict.Blocked)]
    [InlineData("StatusContext", "SUCCESS", null, GitHubReadinessCheckState.Green, GitHubReadinessVerdict.Ready)]
    [InlineData("StatusContext", "PENDING", null, GitHubReadinessCheckState.Pending, GitHubReadinessVerdict.Waiting)]
    public async Task Readiness_ClassifiesNamedHeadChecks(string type, string status, string? conclusion,
        GitHubReadinessCheckState expectedCheck, GitHubReadinessVerdict expectedVerdict) {
        using var fixture = new Fixture();
        var check = type == "CheckRun" ? Check("validation", conclusion, status)
            : JsonNode.Parse("""{"__typename":"StatusContext","context":"validation","isRequired":true}""")!;
        if (type == "StatusContext") check["state"] = status;
        fixture.Page("contexts", null, null, check);

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(expectedVerdict, report.Verdict);
        Assert.Equal(expectedCheck, report.ChecksState);
        var result = Assert.Single(report.Checks);
        Assert.Equal("validation", result.Name);
        Assert.Equal(expectedCheck, result.State);
        Assert.True(result.IsRequired);
    }

    [Theory]
    [InlineData("draft", GitHubReadinessVerdict.Blocked, "draft")]
    [InlineData("closed", GitHubReadinessVerdict.Blocked, "pull-request-not-open")]
    [InlineData("conflict", GitHubReadinessVerdict.Blocked, "merge-conflicts")]
    [InlineData("gate", GitHubReadinessVerdict.Blocked, "github-merge-gate-blocked")]
    [InlineData("approval", GitHubReadinessVerdict.Waiting, "required-approval-pending")]
    [InlineData("unknown", GitHubReadinessVerdict.Waiting, "mergeability-unknown")]
    [InlineData("behind", GitHubReadinessVerdict.Blocked, "branch-behind")]
    public async Task Readiness_UsesServerMergeAndApprovalGates(string scenario, GitHubReadinessVerdict verdict, string reason) {
        using var fixture = new Fixture();
        switch (scenario) {
            case "draft": fixture.Metadata["isDraft"] = true; break;
            case "closed": fixture.Metadata["state"] = "CLOSED"; break;
            case "conflict": fixture.Metadata["mergeable"] = "CONFLICTING"; break;
            case "gate": fixture.Metadata["mergeStateStatus"] = "BLOCKED"; break;
            case "approval": fixture.Metadata["reviewDecision"] = "REVIEW_REQUIRED"; fixture.Metadata["mergeStateStatus"] = "BLOCKED"; break;
            case "unknown": fixture.Metadata["mergeable"] = "UNKNOWN"; fixture.Metadata["mergeStateStatus"] = "UNKNOWN"; break;
            case "behind": fixture.Metadata["mergeStateStatus"] = "BEHIND"; fixture.Metadata["baseRef"]!["branchProtectionRule"] = Protection(); break;
        }

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(verdict, report.Verdict);
        Assert.Contains(reason, report.Reasons);
        Assert.Equal(fixture.Metadata["mergeable"]!.GetValue<string>(), report.Mergeable);
        Assert.Equal(fixture.Metadata["mergeStateStatus"]!.GetValue<string>(), report.MergeStateStatus);
    }

    [Fact]
    public async Task Readiness_ReportsRequiredContextsAndProtectionWithoutInferringApprovalFromCounts() {
        using var fixture = new Fixture();
        fixture.Metadata["baseRef"]!["branchProtectionRule"] = Protection();
        fixture.Metadata["reviewDecision"] = "REVIEW_REQUIRED";
        fixture.Page("contexts", null, null, Check("build", "SUCCESS"));
        fixture.Page("reviews", null, null, Review("reviewer-a", "APPROVED", "2026-01-01T12:00:00Z", OtherHead));

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(GitHubReadinessVerdict.Waiting, report.Verdict);
        Assert.Equal(GitHubReadinessCheckState.Pending, report.ChecksState);
        Assert.Contains(report.Checks, check => check.Name == "required-test" && check.State == GitHubReadinessCheckState.Pending && check.IsRequired == true);
        Assert.Contains("required-approval-pending", report.Reasons);
        Assert.False(Assert.Single(report.Reviews).IsCurrentHead);
        Assert.Equal(2, report.BranchProtection!.RequiredApprovingReviewCount);
        Assert.True(report.BranchProtection.RequiresCodeOwnerReviews);
        Assert.True(report.BranchProtection.RequireLastPushApproval);
        Assert.True(report.BranchProtection.DismissesStaleReviews);
        Assert.True(report.BranchProtection.RequiresConversationResolution);
        Assert.True(report.BranchProtection.RequiresStrictStatusChecks);
    }

    [Theory]
    [InlineData("2026-01-01T10:59:59Z", "reviewer-a", false)]
    [InlineData("2026-01-01T11:00:00Z", "reviewer-a", false)]
    [InlineData("2026-01-01T11:00:01Z", "REVIEWER-A", true)]
    [InlineData("2026-01-01T12:00:00Z", "reviewer-b", false)]
    public async Task Readiness_EyesRequireStrictlyLaterSameActorSubmittedReview(string submittedAt, string actor, bool superseded) {
        using var fixture = new Fixture();
        fixture.Page("reactions", null, null, Eyes("reviewer-a", "2026-01-01T11:00:00Z"));
        fixture.Page("reviews", null, null, Review(actor, "COMMENTED", submittedAt, OtherHead));

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(superseded ? GitHubReadinessVerdict.Ready : GitHubReadinessVerdict.Waiting, report.Verdict);
        Assert.Equal(superseded ? 0 : 1, report.PendingReviewers.Count);
        Assert.False(Assert.Single(report.Reviews).IsCurrentHead);
        Assert.Contains("push-timestamp-unavailable", report.EvidenceGaps);
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T09:00:00Z"), report.HeadCommittedAtUtc);
    }

    [Fact]
    public async Task Readiness_CommentDoesNotClearChangesRequestedAndPendingReviewDoesNotSupersedeEyes() {
        using var fixture = new Fixture();
        var pending = Review("reviewer-b", "PENDING", null, Head);
        pending["createdAt"] = "2026-01-01T14:00:00Z";
        fixture.Page("reviews", null, null, Review("reviewer-a", "CHANGES_REQUESTED", "2026-01-01T10:00:00Z", Head),
            Review("reviewer-a", "COMMENTED", "2026-01-01T12:00:00Z", Head), pending);
        fixture.Page("reactions", null, null, Eyes("reviewer-b", "2026-01-01T11:00:00Z"));

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(GitHubReadinessVerdict.Blocked, report.Verdict);
        Assert.Contains("changes-requested", report.Reasons);
        Assert.Equal(new[] { "eyes-reaction", "pending-review" }, report.PendingReviewers.Select(reviewer => reviewer.Source));
        Assert.Equal(DateTimeOffset.Parse("2026-01-01T14:00:00Z"), report.PendingReviewers[1].SinceUtc);
    }

    [Fact]
    public async Task Readiness_DeletedActorsCannotSupersedeEachOthersReviews() {
        using var fixture = new Fixture();
        var changes = Review("deleted-a", "CHANGES_REQUESTED", "2026-01-01T10:00:00Z", Head);
        changes["author"] = null;
        var approval = Review("deleted-b", "APPROVED", "2026-01-01T12:00:00Z", Head);
        approval["author"] = null;
        fixture.Page("reviews", null, null, changes, approval);

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(GitHubReadinessVerdict.Blocked, report.Verdict);
        Assert.Contains("changes-requested", report.Reasons);
        Assert.Equal(2, report.Reviews.Count);
    }

    [Theory]
    [InlineData(false, GitHubReadinessVerdict.Ready)]
    [InlineData(true, GitHubReadinessVerdict.Waiting)]
    public async Task Readiness_NoRollupDistinguishesNoChecksFromMissingRequiredChecks(bool required, GitHubReadinessVerdict verdict) {
        using var fixture = new Fixture { NoRollup = true };
        if (required) fixture.Metadata["baseRef"]!["branchProtectionRule"] = Protection();

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(verdict, report.Verdict);
        Assert.Equal(required ? GitHubReadinessCheckState.Pending : GitHubReadinessCheckState.Green, report.ChecksState);
        if (required) Assert.Contains("required-approval-unknown", report.Reasons);
        else Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData(0, false, false, GitHubReadinessVerdict.Ready)]
    [InlineData(1, false, false, GitHubReadinessVerdict.Waiting)]
    [InlineData(0, true, false, GitHubReadinessVerdict.Waiting)]
    [InlineData(0, false, true, GitHubReadinessVerdict.Waiting)]
    public async Task Readiness_NullReviewDecisionRequiresActualApprovalConditions(int count, bool codeOwners, bool latestPush, GitHubReadinessVerdict expected) {
        using var fixture = new Fixture();
        var protection = Protection();
        protection["requiredApprovingReviewCount"] = count;
        protection["requiresCodeOwnerReviews"] = codeOwners;
        protection["requireLastPushApproval"] = latestPush;
        protection["requiresStatusChecks"] = false;
        protection["requiredStatusCheckContexts"] = new JsonArray();
        fixture.Metadata["baseRef"]!["branchProtectionRule"] = protection;
        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);
        Assert.Equal(expected, report.Verdict);
        Assert.Equal(expected == GitHubReadinessVerdict.Waiting, report.Reasons.Contains("required-approval-unknown"));
    }

    [Fact]
    public async Task Readiness_IndependentObservationsFetchFreshChecksAndFailureDominatesPending() {
        using var fixture = new Fixture();
        fixture.Page("contexts", null, null, Check("build", "SUCCESS"));
        var first = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);
        fixture.Page("contexts", null, null, Check("build", "FAILURE"), Check("test", null, "IN_PROGRESS"));

        var second = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(GitHubReadinessVerdict.Ready, first.Verdict);
        Assert.Equal(GitHubReadinessVerdict.Blocked, second.Verdict);
        Assert.Equal(GitHubReadinessCheckState.Failing, second.ChecksState);
        Assert.Equal(2, fixture.Reads.Count(read => read.Field == "contexts"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Readiness_HeadChangesCannotProduceReadyEvenWhenItChangesBack(bool changeBack) {
        using var fixture = new Fixture();
        if (changeBack) fixture.ConnectionHead = OtherHead;
        else fixture.FinalHead = OtherHead;

        var report = await fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42);

        Assert.Equal(GitHubReadinessVerdict.Waiting, report.Verdict);
        Assert.Contains("head-changed-during-read", report.Reasons);
        Assert.Equal(Head, report.HeadSha);
        Assert.Equal(changeBack ? Head : OtherHead, report.ObservedHeadSha);
    }

    [Theory]
    [InlineData("missing-cursor")]
    [InlineData("repeated-cursor")]
    [InlineData("missing-connection")]
    [InlineData("graphql-error")]
    [InlineData("page-limit")]
    public async Task Readiness_IncompleteEvidenceRejectsTheRead(string scenario) {
        using var fixture = new Fixture();
        if (scenario == "missing-cursor") fixture.Page("reviewThreads", null, "");
        if (scenario == "repeated-cursor") {
            fixture.Page("reviewThreads", null, "same");
            fixture.Page("reviewThreads", "same", "same");
        }
        if (scenario == "page-limit") {
            for (var page = 0; page < 100; page++) fixture.Page("reviewThreads", page == 0 ? null : page.ToString(), (page + 1).ToString());
        }
        fixture.Failure = scenario;

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.FetchPullRequestReadinessAsync("Sample/Project", 42));
    }

    private static JsonNode Check(string name, string? conclusion, string status = "COMPLETED") => new JsonObject {
        ["__typename"] = "CheckRun", ["name"] = name, ["status"] = status, ["conclusion"] = conclusion, ["isRequired"] = true
    };
    private static JsonNode Thread(string path, string text, bool resolved, bool outdated = false) => new JsonObject {
        ["path"] = path, ["isResolved"] = resolved, ["isOutdated"] = outdated,
        ["comments"] = new JsonObject { ["nodes"] = new JsonArray(new JsonObject { ["bodyText"] = text }) }
    };
    private static JsonNode Review(string actor, string state, string? at, string sha) => new JsonObject {
        ["id"] = actor + "-" + state + "-" + at, ["author"] = new JsonObject { ["login"] = actor }, ["state"] = state, ["submittedAt"] = at,
        ["commit"] = new JsonObject { ["oid"] = sha }
    };
    private static JsonNode Eyes(string actor, string at) => new JsonObject {
        ["user"] = new JsonObject { ["login"] = actor }, ["createdAt"] = at
    };
    private static JsonNode Protection() => JsonNode.Parse("""
        {"requiresApprovingReviews":true,"requiredApprovingReviewCount":2,"requiresCodeOwnerReviews":true,
         "requireLastPushApproval":true,"dismissesStaleReviews":true,"requiresConversationResolution":true,
         "requiresStatusChecks":true,"requiresStrictStatusChecks":true,"requiredStatusCheckContexts":["build","required-test"]}
        """)!;

    private sealed class Fixture : HttpMessageHandler {
        private readonly HttpClient _http;
        private readonly Dictionary<(string Field, string After), JsonNode> _pages = new();
        private int _metadataReads;
        public GitHubDashboardService Service { get; }
        public ConcurrentQueue<(string Field, JsonNode Variables)> Reads { get; } = new();
        public string? FinalHead { get; set; }
        public string? ConnectionHead { get; set; }
        public string? Failure { get; set; }
        public bool NoRollup { get; set; }
        public JsonNode Metadata { get; } = JsonNode.Parse("""
            {"headRefOid":"1111111111111111111111111111111111111111","headRefName":"feature/sample","state":"OPEN","isDraft":false,
             "mergeable":"MERGEABLE","mergeStateStatus":"CLEAN","reviewDecision":null,
             "commits":{"nodes":[{"commit":{"oid":"1111111111111111111111111111111111111111","committedDate":"2026-01-01T09:00:00Z"}}]},
             "baseRef":{"branchProtectionRule":null}}
            """)!;

        public Fixture() {
            _http = new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://api.github.com/") };
            Service = new GitHubDashboardService(_http, disposeHttpClient: false);
        }

        public void Page(string field, string? after, string? next, params JsonNode[] nodes) => _pages[(field, after ?? "")] = new JsonObject {
            ["nodes"] = new JsonArray(nodes), ["pageInfo"] = new JsonObject { ["hasNextPage"] = next is not null, ["endCursor"] = next }
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/graphql", request.RequestUri!.AbsolutePath);
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            var query = body["query"]!.GetValue<string>();
            var variables = body["variables"]!;
            Assert.Equal("Sample", variables["owner"]!.GetValue<string>());
            Assert.Equal("Project", variables["repo"]!.GetValue<string>());
            Assert.Equal(42, variables["number"]!.GetValue<int>());
            JsonNode result;
            if (query.Contains("commits(last: 1)", StringComparison.Ordinal)) {
                result = Metadata.DeepClone();
                if (++_metadataReads > 1 && FinalHead is not null) result["headRefOid"] = FinalHead;
                result = new JsonObject { ["pullRequest"] = result };
            } else {
                var field = new[] { "contexts", "reviewThreads", "reviews", "reviewRequests", "reactions" }
                    .Single(name => query.Contains(name + "(", StringComparison.Ordinal));
                Reads.Enqueue((field, variables.DeepClone()));
                var after = variables["after"]?.GetValue<string>() ?? "";
                var page = _pages.TryGetValue((field, after), out var configured) ? configured.DeepClone()
                    : new JsonObject { ["nodes"] = new JsonArray(), ["pageInfo"] = new JsonObject { ["hasNextPage"] = false, ["endCursor"] = null } };
                if (field == "reactions") Assert.Contains("content: EYES", query);
                result = field == "contexts"
                    ? new JsonObject { ["object"] = new JsonObject { ["oid"] = Head, ["statusCheckRollup"] = new JsonObject { ["state"] = "SUCCESS", [field] = page } } }
                    : new JsonObject { ["pullRequest"] = new JsonObject { ["headRefOid"] = ConnectionHead ?? Head, [field] = page } };
                if (NoRollup && field == "contexts") result["object"]!["statusCheckRollup"] = null;
                if (Failure == "missing-connection" && field == "reviewThreads") result["pullRequest"]![field] = null;
                if (Failure == "graphql-error" && field == "reviewThreads") return Response(JsonNode.Parse("""{"data":null,"errors":[{"message":"Synthetic GraphQL failure"}]}""")!);
            }
            return Response(new JsonObject { ["data"] = new JsonObject { ["repository"] = result } });
        }

        private static HttpResponseMessage Response(JsonNode body) => new(HttpStatusCode.OK) {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };

        protected override void Dispose(bool disposing) {
            if (disposing) {
                Service.Dispose();
                _http.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
