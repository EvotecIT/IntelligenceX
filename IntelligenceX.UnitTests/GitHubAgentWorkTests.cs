using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class GitHubAgentWorkTests {
    [Theory]
    [InlineData(true, "request-timeout")]
    [InlineData(false, "request-failed")]
    public async Task OptionalEnrichment_PreservesLocalJsonWhenRemoteSourceFails(bool timeout, string diagnostic) {
        var local = AgentWorkStateRules.Evaluate(new[] {
            new AgentSessionState { Provider = "codex", Id = "local-session", Title = "Local work", State = AgentWorkState.WaitingOnPerson }
        }, Array.Empty<AgentPullRequestState>(), DateTimeOffset.Parse("2026-11-01T12:00:00Z"));
        using var http = new HttpClient(new Handler(_ => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("private sentinel"))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("private sentinel") }))) {
            BaseAddress = new Uri("https://api.github.com/")
        };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var partial = await service.EnrichAgentWorkAsync(local, "sample");
        Assert.Equal("local-session", Assert.Single(partial.Sessions).Id);
        Assert.Equal(AgentAttentionKind.WaitingOnPerson, Assert.Single(partial.NeedsAttention).Kind);
        Assert.Equal(diagnostic, Assert.Single(partial.Diagnostics).Code);
        var json = AgentWorkJson.Serialize(partial);
        var decoded = JsonSerializer.Deserialize(json, AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
        Assert.Equal("local-session", Assert.Single(decoded.Sessions).Id);
        Assert.DoesNotContain("private sentinel", json);
    }

    [Fact]
    public async Task Fetch_PaginatesAuthoredAndIncomingReviewsAndReadsHeadRollup() {
        var requests = new List<(string Query, string? Cursor)>();
        using var http = new HttpClient(new Handler(async request => {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var variables = body.RootElement.GetProperty("variables");
            var query = variables.GetProperty("query").GetString()!;
            var cursor = variables.GetProperty("cursor").GetString();
            requests.Add((query, cursor));
            Assert.DoesNotContain("body", body.RootElement.GetProperty("query").GetString()!);
            return query.Contains("review-requested:", StringComparison.Ordinal)
                ? Page(Node("2", "Someone", "Sample/Fork", "SUCCESS"), false, null)
                : cursor is null ? Page(Node("1", "sample", "Sample/Project", "FAILURE"), true, "next")
                    : Page(Node("3", "sample", "Sample/Project", "PENDING"), false, null);
        })) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var prs = await service.FetchAgentPullRequestsAsync("sample");
        Assert.Equal(3, prs.Count);
        Assert.Equal(AgentCheckState.Failure, prs[0].Checks);
        Assert.True(prs[0].IsAuthor);
        Assert.False(prs[0].ReviewRequested);
        Assert.Equal("https://github.com/Sample/Project/commit/fake-sha/checks", prs[0].ChecksUrl);
        Assert.Equal("Sample/Fork", prs[1].HeadRepository);
        Assert.True(prs[1].ReviewRequested);
        Assert.False(prs[1].IsAuthor);
        Assert.Equal(AgentCheckState.Pending, prs[2].Checks);
        Assert.Equal(new[] { ("is:pr is:open author:sample", (string?)null), ("is:pr is:open author:sample", "next"),
            ("is:pr is:open review-requested:sample", (string?)null) }, requests);
    }

    [Theory]
    [InlineData("{\"errors\":[{\"message\":\"private sentinel\"}]}\\", false)]
    [InlineData("{\"errors\":[{\"message\":\"private sentinel\"}]}", false)]
    [InlineData("{\"data\":{\"search\":{\"issueCount\":1001,\"nodes\":[]}}}", false)]
    [InlineData("{\"data\":{\"search\":{\"nodes\":[],\"pageInfo\":{\"hasNextPage\":true,\"endCursor\":\"same\"}}}}", false)]
    [InlineData("{\"data\":{}}", false)]
    [InlineData("private sentinel", true)]
    public async Task Fetch_FailsClosedForIncompleteSearchAndSanitizesProviderErrors(string response, bool httpFailure) {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(httpFailure ? HttpStatusCode.Forbidden : HttpStatusCode.OK) {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        }))) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => service.FetchAgentPullRequestsAsync("sample"));
        Assert.DoesNotContain("private sentinel", exception.Message);
    }

    private static string Node(string number, string author, string headRepo, string state) => $$$"""
        {"url":"https://github.com/Sample/Project/pull/{{{number}}}","title":"Sample PR","state":"OPEN","headRefName":"feature/work","headRefOid":"fake-sha",
         "author":{"login":"{{{author}}}"},"repository":{"nameWithOwner":"Sample/Project"},"headRepository":{"nameWithOwner":"{{{headRepo}}}"},"statusCheckRollup":{"state":"{{{state}}}"}}
        """;

    private static HttpResponseMessage Page(string nodes, bool more, string? cursor) => new(HttpStatusCode.OK) {
        Content = new StringContent("{\"data\":{\"search\":{\"issueCount\":3,\"nodes\":[" + nodes +
            "],\"pageInfo\":{\"hasNextPage\":" + (more ? "true" : "false") + ",\"endCursor\":" +
            (cursor is null ? "null" : "\"" + cursor + "\"") + "}}}}", Encoding.UTF8, "application/json")
    };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
