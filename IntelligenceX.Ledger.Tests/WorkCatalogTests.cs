using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Catalog;
using IntelligenceX.Ledger.Contracts;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using V = IntelligenceX.Ledger.Contracts.TaskVerdicts;

namespace IntelligenceX.Ledger.Tests;

public sealed class WorkCatalogTests {
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private const string Repo = "Sample/Project";

    private static readonly WorkCatalogSettings Settings = new() {
        Organizations = ["Sample"], TaskRepositories = [Repo], StaleAfterDays = 7,
        Board = new() { Organization = "Sample", ProjectNumber = 1, StatusFieldId = 1, AgentFieldId = 2, MachineFieldId = 3,
            MachineAliases = new() { ["Host"] = "machine-a" } }
    };

    private static CatalogBoardItem Item(long id, string? status = "Active", string? agent = null, string? links = null,
        string type = "DraftIssue", string? url = null, string? state = null, int ageDays = 1) =>
        new(id, type, "Task " + id, url, state, Now.AddDays(-ageDays), false) {
            Status = status, Agent = agent, Machine = agent is null ? null : "Host", PullRequestLinks = links, WorkRepo = Repo
        };

    private static CatalogIssue Issue(int number, int ageDays = 1, string label = "to:claude") =>
        new(Repo, number, "Issue " + number, [label], Now.AddDays(-30), Now.AddDays(-ageDays), false, false);

    private static LedgerItem Session(Guid id, string verdict, string reason = "observed", bool archived = false) => new() {
        Id = LedgerScope.SessionItemId("machine-a", "codex", id.ToString("D")), MachineId = "machine-a", Provider = "codex",
        FirstObservedAtUtc = Now.AddDays(-2), Session = new() { Archived = archived, LastActivityAtUtc = Now.AddHours(-1) },
        Current = new(verdict, reason, Now.AddHours(-1), "test", Now, 1, [])
    };

    private static TaskVerdictRow[] Reconcile(WorkCatalogSnapshot catalog, params LedgerItem[] sessions) =>
        new TaskReconciler(Settings, ["machine-a"]).Reconcile(catalog with { BoardReadAtUtc = catalog.BoardReadAtUtc ?? Now }, sessions, [], Now);

    [Fact]
    public void FinishedWorkIsClosableButNeverClosedAndUnknownPullRequestStatesBlockThatClaim() {
        const string merged = "https://github.com/Sample/Project/pull/1";
        const string unknown = "https://github.com/Sample/Project/pull/2";
        var catalog = new WorkCatalogSnapshot {
            Board = [Item(1, links: merged), Item(2, links: merged + " " + unknown),
                Item(3, status: "Done", type: "Issue", url: "https://github.com/Sample/Project/issues/9", state: "open")],
            Issues = [Issue(9)],
            PullStates = new(StringComparer.OrdinalIgnoreCase) { [merged] = new("merged", Now.AddDays(-1), Now) }
        };
        var rows = Reconcile(catalog).ToDictionary(row => row.Id);

        Assert.Equal((V.DoneClosable, "pull-requests-merged"), (rows["board:1"].Verdict, rows["board:1"].Reason));
        Assert.Contains("mark-board-done:1", rows["board:1"].SuggestedActions);
        Assert.NotEqual(V.DoneClosable, rows["board:2"].Verdict);
        Assert.Contains("pull-request-state:" + unknown, rows["board:2"].MissingFacts);
        Assert.Equal((V.DoneClosable, "board-done-issue-open"), (rows["board:3"].Verdict, rows["board:3"].Reason));
        Assert.Contains("close-issue:https://github.com/Sample/Project/issues/9", rows["board:3"].SuggestedActions);
        Assert.DoesNotContain(rows.Values, row => row.Id == "issue:Sample/Project#9"); // The board item already covers it.
    }

    [Theory]
    [InlineData("working", false, V.InProgress, "session-working")]
    [InlineData("needs-you", false, V.WaitingOnYou, "session-usage-limit")]
    [InlineData("stuck", false, V.NeedsReplacement, "session-usage-limit")]
    [InlineData("working", true, V.NeedsReplacement, "agent-archived")]
    public void ClaimedTasksFollowTheirSessionVerdict(string sessionVerdict, bool archived, string expected, string reason) {
        var id = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var rows = Reconcile(new() { Board = [Item(1, agent: "codex:" + id + ":owner-driven")] },
            Session(id, sessionVerdict, "usage-limit", archived));
        var row = Assert.Single(rows);
        Assert.Equal((expected, reason), (row.Verdict, row.Reason));
        Assert.StartsWith("machine-a codex", row.Where);
        Assert.Equal(sessionVerdict, row.SessionVerdict);
    }

    [Fact]
    public void ClaimWhoseSessionTheLedgerNeverSawNeedsReplacementOnlyOnceItGoesQuiet() {
        var agent = "claude:" + Guid.NewGuid();
        var rows = Reconcile(new() { Board = [Item(1, agent: agent, ageDays: 2), Item(2, agent: agent, ageDays: 20)] }).ToDictionary(row => row.Id);
        Assert.Equal(V.InProgress, rows["board:1"].Verdict);
        Assert.Contains("session-unobserved", rows["board:1"].MissingFacts);
        Assert.Equal((V.NeedsReplacement, "claimed-session-missing"), (rows["board:2"].Verdict, rows["board:2"].Reason));
    }

    [Fact]
    public void UnclaimedWorkIsNotStartedUntilItGoesQuietAndPlannedWorkStaysNotStarted() {
        const string referencing = "https://github.com/Sample/Project/pull/7";
        var catalog = new WorkCatalogSnapshot {
            Board = [Item(1, status: "Planned", ageDays: 90), Item(2, status: "Active", ageDays: 30)],
            Issues = [Issue(10, ageDays: 2), Issue(11, ageDays: 20), Issue(12), Issue(13, label: "bug")],
            IssueReferences = new(StringComparer.Ordinal) { ["Sample/Project#12"] = [referencing] },
            PullStates = new(StringComparer.OrdinalIgnoreCase) { [referencing] = new("merged", Now, Now) }
        };
        var rows = Reconcile(catalog).ToDictionary(row => row.Id);
        Assert.Equal(V.NotStarted, rows["board:1"].Verdict);
        Assert.Equal(V.Stale, rows["board:2"].Verdict);
        Assert.Equal(V.NotStarted, rows["issue:Sample/Project#10"].Verdict);
        Assert.Equal(V.Stale, rows["issue:Sample/Project#11"].Verdict);
        Assert.Equal(V.DoneClosable, rows["issue:Sample/Project#12"].Verdict);
        Assert.Contains("completion-criteria-unverified", rows["issue:Sample/Project#12"].MissingFacts);
        Assert.False(rows.ContainsKey("issue:Sample/Project#13")); // Only addressed issues are tasks.
    }

    [Fact]
    public void UncertainEvidenceNeverMakesATaskClosable() {
        const string merged = "https://github.com/Other/Elsewhere/pull/1";
        const string outside = "https://github.com/Other/Elsewhere/pull/2";
        const string oldClosed = "https://github.com/Other/Elsewhere/pull/3";
        const string unknown = "https://github.com/Other/Elsewhere/pull/4";
        const string retained = "https://github.com/Other/Elsewhere/pull/6";
        var tooLong = merged + " " + new string('x', WorkCatalogReader.MaximumLinkText);
        var catalog = new WorkCatalogSnapshot {
            CapturedAtUtc = Now,
            Board = [
                Item(1, type: "Issue", url: "https://github.com/Other/Elsewhere/issues/5"), // State unread: not closed.
                Item(2, links: merged + " " + outside),       // The open PR was read individually, outside the open lists.
                Item(3, links: merged + " " + oldClosed),     // Closed, but only from an observation older than this pass.
                Item(4, links: merged + " " + unknown),       // GitHub gave no usable state.
                Item(5, links: tooLong),                      // Links beyond the bound may hide an open PR.
                Item(6, links: merged + " " + retained)       // Closed when last seen, but this pass failed to confirm it.
            ],
            PullStates = new(StringComparer.OrdinalIgnoreCase) {
                [merged] = new("merged", Now.AddDays(-1), Now.AddDays(-3)),
                [outside] = new("open", Now.AddHours(-2), Now) { HeadBranch = "fix/x" },
                [oldClosed] = new("closed", Now.AddDays(-2), Now.AddHours(-2)),
                [unknown] = new("unknown", Now, Now),
                [retained] = new("closed", Now, Now) { Retained = true }
            }
        };
        var rows = Reconcile(catalog).ToDictionary(row => row.Id);

        Assert.NotEqual(V.DoneClosable, rows["board:1"].Verdict);
        Assert.Contains("issue-state", rows["board:1"].MissingFacts);
        Assert.Equal((V.InProgress, "open-pull-request"), (rows["board:2"].Verdict, rows["board:2"].Reason));
        Assert.Equal(outside, rows["board:2"].Where);
        Assert.NotEqual(V.DoneClosable, rows["board:3"].Verdict);
        Assert.Contains("pull-request-state:" + oldClosed, rows["board:3"].MissingFacts);
        Assert.Contains("pull-request-state:" + unknown, rows["board:4"].MissingFacts);
        Assert.NotEqual(V.DoneClosable, rows["board:4"].Verdict);
        Assert.Contains("pull-request-links-capped", rows["board:5"].MissingFacts);
        Assert.NotEqual(V.DoneClosable, rows["board:5"].Verdict);
        Assert.Contains("pull-request-state:" + retained, rows["board:6"].MissingFacts);
        Assert.NotEqual(V.DoneClosable, rows["board:6"].Verdict);
    }

    [Fact]
    public void AnyLinkedPullRequestWithALiveCheckoutKeepsTheTaskInProgress() {
        var idle = new CatalogPull(Repo, 1, "Newer, nobody on it", "fix/a", false, Now.AddDays(-10));
        var worked = new CatalogPull(Repo, 2, "Older, being worked", "fix/b", false, Now.AddDays(-12));
        var checkout = new TaskReconciler.Worktree(Repo, new("a", "machine-a", "codex", "fix/b", "/work/b", null, [],
            Now.AddMinutes(-1), null, false, null, "observed", Live: true));
        var row = Assert.Single(new TaskReconciler(Settings, ["machine-a"]).Reconcile(
            new() { Board = [Item(1, links: idle.Url + " " + worked.Url)], PullRequests = [idle, worked] }, [], [checkout], Now));
        Assert.Equal((V.InProgress, "open-pull-request-live-worktree"), (row.Verdict, row.Reason));
        Assert.Contains("/work/b", row.Where);
    }

    [Fact]
    public void ForkPullRequestsMatchTheirOwnCheckoutAndRepeatedRowsDoNotBreakTheView() {
        var fork = new CatalogPull(Repo, 8, "From a fork", "main", false, Now.AddDays(-20)) { HeadRepo = "Someone/Project" };
        var catalog = new WorkCatalogSnapshot {
            Board = [Item(1, links: fork.Url)], PullRequests = [fork, fork], Issues = [Issue(3), Issue(3)]
        };
        var baseCheckout = new TaskReconciler.Worktree(Repo, new("a", "machine-a", "codex", "main", "/work/project", null, [],
            Now.AddMinutes(-1), null, false, null, "observed", Live: true));
        var rows = new TaskReconciler(Settings, ["machine-a"]).Reconcile(catalog, [], [baseCheckout], Now).ToDictionary(row => row.Id);

        // The base repository's main checkout is not where a fork's main branch is worked on.
        Assert.Equal((V.Stale, "pull-request-idle"), (rows["board:1"].Verdict, rows["board:1"].Reason));
        Assert.Single(rows.Keys, key => key == "issue:Sample/Project#3");
    }

    [Fact]
    public void BoardCopiesFromAFailedReadNeverCloseATask() {
        const string reopened = "https://github.com/Sample/Project/pull/9";
        var reopenedPull = new CatalogPull(Repo, 9, "Reopened", "fix/r", false, Now.AddHours(-1));
        var catalog = new WorkCatalogSnapshot {
            CapturedAtUtc = Now, BoardReadAtUtc = Now.AddHours(-3), PullRequests = [reopenedPull],
            Board = [Item(1, type: "PullRequest", url: reopened, state: "closed"),
                Item(2, status: "Done", type: "Issue", url: "https://github.com/Sample/Project/issues/4", state: "open")]
        };
        var rows = new TaskReconciler(Settings, ["machine-a"]).Reconcile(catalog, [], [], Now).ToDictionary(row => row.Id);
        Assert.Equal(V.InProgress, rows["board:1"].Verdict); // The PR itself is open again.
        Assert.Contains("board-observation-stale", rows["board:2"].MissingFacts);
        Assert.NotEqual(V.DoneClosable, rows["board:2"].Verdict);
    }

    [Fact]
    public void LastKnownSessionVerdictsStayQualified() {
        var id = Guid.NewGuid();
        var session = Session(id, "working");
        session = session with { Current = session.Current! with { LastKnown = true } };
        var row = Assert.Single(Reconcile(new() { Board = [Item(1, agent: "codex:" + id)] }, session));
        Assert.Equal(V.InProgress, row.Verdict);
        Assert.Contains("session-current-observation", row.MissingFacts);
    }

    [Fact]
    public void BoardRowMissingARequestedFieldIsIncompleteNotEmpty() {
        using var document = JsonDocument.Parse("""
            {"id":6,"content_type":"DraftIssue","updated_at":"2026-11-10T00:00:00Z","content":{"title":"Draft"},
             "fields":[{"id":1,"data_type":"single_select","value":{"name":{"raw":"Active"}}}]}
            """);
        Assert.Throws<InvalidDataException>(() => WorkCatalogReader.MapBoardItem(Settings.Board!, document.RootElement));
    }

    [Fact]
    public async Task UnchangedPagesAnswer304FromCacheWithoutSpendingTheBudget() {
        var handler = new PagedHandler();
        var cache = new ConcurrentDictionary<string, GitHubCatalogClient.Page>(StringComparer.Ordinal);
        var budget = new GitHubRequestBudget(hourlyBudget: 3, minimumRemaining: 0, new FakeTimeProvider(Now));
        using var http = new HttpClient(handler);
        var first = new GitHubCatalogClient(http, budget, cache);
        var url = "https://api.github.com/repos/Sample/Project/issues/1/timeline?per_page=100";
        Func<JsonElement, JsonElement?> map = WorkCatalogReader.MapReference;

        var references = await first.ReadAllAsync(url, map, 5, default);
        Assert.Equal("https://github.com/Sample/Project/pull/3", Assert.Single(references).GetString());
        Assert.Equal(2, budget.UsedThisHour);

        var again = await new GitHubCatalogClient(http, budget, cache).ReadAllAsync(url, map, 5, default);
        Assert.Single(again);
        Assert.Equal(2, budget.UsedThisHour); // Both pages were 304; the second read cost nothing.
        Assert.Equal(2, handler.Conditional);
    }

    [Fact]
    public async Task BudgetStopsReadsAndContinuationsNeverLeaveGitHub() {
        var budget = new GitHubRequestBudget(hourlyBudget: 1, minimumRemaining: 0, new FakeTimeProvider(Now));
        using var http = new HttpClient(new PagedHandler());
        var client = new GitHubCatalogClient(http, budget, new(StringComparer.Ordinal));
        await Assert.ThrowsAsync<GitHubBudgetExhaustedException>(() =>
            client.ReadAllAsync("https://api.github.com/repos/Sample/Project/issues/1/timeline?per_page=100", WorkCatalogReader.MapReference, 5, default));

        using var foreign = new HttpClient(new PagedHandler { NextHost = "example.com" });
        var exposed = new GitHubCatalogClient(foreign, new(10, 0), new(StringComparer.Ordinal));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            exposed.ReadAllAsync("https://api.github.com/repos/Sample/Project/issues/1/timeline?per_page=100", WorkCatalogReader.MapReference, 5, default));
    }

    [Fact]
    public void BoardRowsKeepMetadataFieldsAndDropContentBodies() {
        using var document = JsonDocument.Parse("""
            {"id":5,"content_type":"Issue","updated_at":"2026-11-10T00:00:00Z","archived_at":null,
             "content":{"title":"Fix it","html_url":"https://github.com/Sample/Project/issues/4","state":"open","body":"private notes"},
             "fields":[{"id":1,"data_type":"single_select","value":{"name":{"raw":"Active"}}},
                       {"id":2,"data_type":"text","value":{"raw":"codex:x"}},
                       {"id":3,"data_type":"single_select","value":null}]}
            """);
        var row = WorkCatalogReader.MapBoardItem(Settings.Board!, document.RootElement)!.Value;
        Assert.DoesNotContain("private notes", row.GetRawText(), StringComparison.Ordinal);
        var item = row.Deserialize<CatalogBoardItem>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(("Active", "codex:x", (string?)null, "open"), (item.Status, item.Agent, item.Machine, item.ContentState));
    }

    /// <summary>Two timeline pages: the second has no PR cross-reference, so mapping empties it, yet the page is complete.</summary>
    private sealed class PagedHandler : HttpMessageHandler {
        public string NextHost { get; init; } = "api.github.com";
        public int Conditional { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            var second = request.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal);
            if (request.Headers.TryGetValues("If-None-Match", out _)) {
                Conditional++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            var body = !second
                ? """[{"event":"cross-referenced","source":{"issue":{"html_url":"https://github.com/Sample/Project/pull/3","pull_request":{},"body":"x"}}}]"""
                : """[{"event":"commented","body":"long discussion"}]""";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            response.Headers.ETag = new("\"page-" + (second ? 2 : 1) + "\"");
            if (!second) response.Headers.Add("Link", "<https://" + NextHost + "/repositories/42/issues/1/timeline?per_page=100&page=2>; rel=\"next\"");
            return Task.FromResult(response);
        }
    }
}
