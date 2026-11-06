using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Agents;
using IntelligenceX.Telemetry.GitHub;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class GitHubAgentWorkTests {
    [Theory]
    [InlineData("success", null, AgentCheckState.Unknown, true)]
    [InlineData("failure", null, AgentCheckState.Failure, true)]
    [InlineData("success", "stale", AgentCheckState.Pending, false)]
    [InlineData("failure", "stale", AgentCheckState.Failure, false)]
    public async Task CheckConclusions_DistinguishStaleFromUnavailableResults(string known, string? conclusion, AgentCheckState expected, bool unavailable) {
        using var http = new HttpClient(new Handler(request => {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path == "/search/issues" ? Search("1")
                : path.Contains("/pulls/", StringComparison.Ordinal) ? Pull("1", "sample", "Sample/Project")
                : path.EndsWith("/status", StringComparison.Ordinal) ? Json("{\"total_count\":0}")
                : Json("{\"check_runs\":[{\"status\":\"completed\",\"conclusion\":\"" + known + "\"},{\"status\":\"completed\",\"conclusion\":"
                    + JsonSerializer.Serialize(conclusion) + "}]}"));
        })) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var snapshot = await service.EnrichAgentWorkAsync(new AgentWorkSnapshot(), "sample");
        var pr = Assert.Single(snapshot.PullRequests);
        Assert.Equal(expected, pr.Checks);
        Assert.Equal(unavailable, pr.ChecksUnavailable);
        if (unavailable) {
            Assert.Equal("checks-unavailable", Assert.Single(snapshot.Diagnostics).Code);
            Assert.Null(snapshot.GitHubCheckedAtUtc);
        } else {
            Assert.Empty(snapshot.Diagnostics);
            Assert.NotNull(snapshot.GitHubCheckedAtUtc);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialCheckFailure_PreservesSameHeadEvidenceAndNewReviewsWithoutClaimingRevalidation(bool newService) {
        var phase = 0;
        using var http = new HttpClient(new Handler(request => {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/search/issues") return Task.FromResult(Search(
                request.RequestUri.Query.Contains("review-requested", StringComparison.Ordinal) && phase > 0 ? "2" : "1"));
            if (path.Contains("/pulls/", StringComparison.Ordinal)) {
                var number = path.Split('/').Last();
                return Task.FromResult(Pull(number, number == "1" ? "sample" : "other", "Sample/Project",
                    number == "1" && phase >= 2 ? "new-head" : null));
            }
            var first = path.Contains("sha-1", StringComparison.Ordinal) || path.Contains("new-head", StringComparison.Ordinal);
            if (first && phase is 1 or 2) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (path.EndsWith("/status", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"total_count\":1,\"state\":\"" + (first && phase == 0 ? "failure" : "success") + "\"}"));
            return Task.FromResult(Checks(first && phase == 0 ? "failure" : "success"));
        })) { BaseAddress = new Uri("https://api.github.com/") };
        using var original = new GitHubDashboardService(http, disposeHttpClient: false);
        var local = new AgentWorkSnapshot { CapturedAtUtc = DateTimeOffset.UtcNow };
        var firstSnapshot = await original.EnrichAgentWorkAsync(local, "sample");
        Assert.Equal(AgentCheckState.Failure, Assert.Single(firstSnapshot.PullRequests).Checks);
        Assert.False(firstSnapshot.PullRequests[0].ChecksUnavailable);
        Assert.NotNull(firstSnapshot.GitHubCheckedAtUtc);
        using var replacement = newService ? new GitHubDashboardService(http, disposeHttpClient: false) : null;
        var service = replacement ?? original;
        phase = 1;
        var partial = await service.EnrichAgentWorkAsync(firstSnapshot, "sample");
        Assert.Equal(2, partial.PullRequests.Count);
        Assert.Equal(AgentCheckState.Failure, partial.PullRequests[0].Checks);
        Assert.True(partial.PullRequests[0].ChecksUnavailable);
        Assert.True(partial.PullRequests[1].ReviewRequested);
        Assert.Equal("checks-unavailable", Assert.Single(partial.Diagnostics).Code);
        Assert.Equal(firstSnapshot.GitHubCheckedAtUtc, partial.GitHubCheckedAtUtc);
        Assert.Contains(partial.NeedsAttention, item => item.Kind == AgentAttentionKind.FailedChecks);
        Assert.Contains(partial.NeedsAttention, item => item.Kind == AgentAttentionKind.ReviewRequested);
        phase = 2;
        var newHead = await service.EnrichAgentWorkAsync(partial, "sample");
        Assert.Equal(AgentCheckState.Unknown, newHead.PullRequests[0].Checks);
        Assert.NotEqual(partial.PullRequests[0].ChecksUrl, newHead.PullRequests[0].ChecksUrl);
        Assert.DoesNotContain(newHead.NeedsAttention, item => item.Kind == AgentAttentionKind.FailedChecks);
        phase = 3;
        var recovered = await service.EnrichAgentWorkAsync(newHead, "sample");
        Assert.Empty(recovered.Diagnostics);
        Assert.All(recovered.PullRequests, pr => Assert.False(pr.ChecksUnavailable));
        Assert.True(recovered.GitHubCheckedAtUtc > firstSnapshot.GitHubCheckedAtUtc);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BodyDeadline_CancelsAStalledResponseAndReleasesTheFetchGate(bool resolveLogin) {
        var clock = new AgentWorkTestClock();
        var reading = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Interlocked.Increment(ref requests) == 1
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledBody(reading)) }
            : Json("{\"total_count\":0,\"items\":[]}")))) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false, clock);
        var fetch = service.FetchAgentPullRequestsAsync(resolveLogin ? null : "sample");
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await clock.Scheduled(TimeSpan.FromSeconds(30)).WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(fetch.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetch);
        Assert.Empty(await service.FetchAgentPullRequestsAsync("sample"));
        Assert.Equal(3, requests);
    }
    [Theory]
    [InlineData(true, "request-timeout")]
    [InlineData(false, "request-failed")]
    [InlineData(false, null)]
    public async Task OptionalEnrichment_PreservesObservedWorkOnFailureAndMetadataFreshnessOnSuccess(bool timeout, string? diagnostic) {
        var local = AgentWorkStateRules.Evaluate(new[] {
            new AgentSessionState { Provider = "codex", Id = "local-session", Title = "Local work", State = AgentWorkState.WaitingOnPerson,
                Repository = "Sample/Project", Branch = "feature/work" }
        }, new[] { new AgentPullRequestState { Url = "https://github.com/Sample/Project/pull/1", HeadRepository = "Sample/Project",
            Branch = "feature/work", Checks = AgentCheckState.Failure, ReviewRequested = true, IsAuthor = true } }, DateTimeOffset.Parse("2026-11-01T12:00:00Z"))
            with { MetadataCheckedAtUtc = DateTimeOffset.Parse("2026-11-01T11:59:00Z"), GitHubCheckedAtUtc = DateTimeOffset.Parse("2026-11-01T11:58:00Z"),
                Diagnostics = new[] { new AgentSourceDiagnostic { Provider = "github", Code = "request-failed" },
                    new AgentSourceDiagnostic { Provider = "claude", Code = "unreadable-source" } } };
        using var http = new HttpClient(new Handler(_ => timeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("private sentinel"))
            : Task.FromResult(diagnostic is null ? Search(null)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("private sentinel") }))) {
            BaseAddress = new Uri("https://api.github.com/")
        };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var partial = await service.EnrichAgentWorkAsync(local, "sample");
        Assert.Equal("local-session", Assert.Single(partial.Sessions).Id);
        Assert.Equal(local.MetadataCheckedAtUtc, partial.MetadataCheckedAtUtc);
        if (diagnostic is null) {
            Assert.Equal(AgentAttentionKind.WaitingOnPerson, Assert.Single(partial.NeedsAttention).Kind);
            Assert.Equal("claude", Assert.Single(partial.Diagnostics).Provider);
            Assert.Empty(partial.PullRequests);
            Assert.True(partial.GitHubCheckedAtUtc > local.GitHubCheckedAtUtc);
        } else {
            Assert.Equal(local.GitHubCheckedAtUtc, partial.GitHubCheckedAtUtc);
            Assert.Same(local.PullRequests, partial.PullRequests);
            Assert.Same(local.Sessions, partial.Sessions);
            Assert.Same(local.NeedsAttention, partial.NeedsAttention);
            Assert.Contains(partial.NeedsAttention, item => item.Kind == AgentAttentionKind.FailedChecks);
            Assert.Contains(partial.NeedsAttention, item => item.Kind == AgentAttentionKind.ReviewRequested);
            Assert.Equal(diagnostic, Assert.Single(partial.Diagnostics, item => item.Provider == "github").Code);
            Assert.Contains(partial.Diagnostics, item => item.Provider == "claude" && item.Code == "unreadable-source");
        }
        var repeated = await service.EnrichAgentWorkAsync(partial, "sample");
        Assert.Equal(partial.Diagnostics, repeated.Diagnostics);
        var json = AgentWorkJson.Serialize(partial);
        var decoded = JsonSerializer.Deserialize(json, AgentWorkJsonContext.Default.AgentWorkSnapshot)!;
        Assert.Equal("local-session", Assert.Single(decoded.Sessions).Id);
        Assert.DoesNotContain("private sentinel", json);
    }

    [Fact]
    public async Task Fetch_PaginatesSearchAndChecksAndReadsCurrentHead() {
        var searchPages = new List<string>();
        using var http = new HttpClient(new Handler(request => {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/search/issues") {
                lock (searchPages) searchPages.Add(uri.Query);
                var reviews = uri.Query.Contains("review-requested", StringComparison.Ordinal);
                var second = uri.Query.Contains("page=2", StringComparison.Ordinal);
                return Task.FromResult(Search(reviews ? "2" : second ? "3" : "1", more: !reviews && !second));
            }
            if (uri.AbsolutePath.Contains("/pulls/", StringComparison.Ordinal)) {
                var n = uri.Segments.Last();
                return Task.FromResult(Pull(n, n == "2" ? "Someone" : "sample", n == "2" ? "Sample/Fork" : "Sample/Project"));
            }
            if (uri.AbsolutePath.EndsWith("/status", StringComparison.Ordinal)) return Task.FromResult(Json("{\"total_count\":0,\"state\":\"pending\"}"));
            var number = uri.Segments[^2].Trim('/').Replace("sha-", "");
            var next = number == "1" && !uri.Query.Contains("page=2", StringComparison.Ordinal);
            return Task.FromResult(Checks(number == "1" && !next ? "failure" : number == "3" ? "pending" : "success", next));
        })) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var prs = await service.FetchAgentPullRequestsAsync("sample");
        Assert.Equal(3, prs.Count);
        Assert.Equal(AgentCheckState.Failure, prs[0].Checks);
        Assert.True(prs[0].IsAuthor);
        Assert.False(prs[0].ReviewRequested);
        Assert.Equal("https://github.com/Sample/Project/commit/sha-1/checks", prs[0].ChecksUrl);
        Assert.Equal("Sample/Fork", prs[1].HeadRepository);
        Assert.True(prs[1].ReviewRequested);
        Assert.False(prs[1].IsAuthor);
        Assert.Equal(AgentCheckState.Pending, prs[2].Checks);
        Assert.Equal(3, searchPages.Count);
        Assert.DoesNotContain("private sentinel", AgentWorkJson.Serialize(new AgentWorkSnapshot { PullRequests = prs }));
    }

    [Fact]
    public async Task ETags_RevalidateChecksEvenWhenSearchAndPullRequestAreUnchanged() {
        var second = false;
        var conditional = 0;
        using var http = new HttpClient(new Handler(request => {
            if (second) {
                Assert.Equal("\"v1\"", Assert.Single(request.Headers.IfNoneMatch).ToString());
                Interlocked.Increment(ref conditional);
                if (!request.RequestUri!.AbsolutePath.EndsWith("/check-runs", StringComparison.Ordinal))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            var path = request.RequestUri!.AbsolutePath;
            var response = path == "/search/issues" ? Search(request.RequestUri.Query.Contains("review-requested", StringComparison.Ordinal) ? null : "1")
                : path.Contains("/pulls/", StringComparison.Ordinal) ? Pull("1", "sample", "Sample/Project")
                : path.EndsWith("/status", StringComparison.Ordinal) ? Json("{\"total_count\":1,\"state\":\"success\"}")
                : Checks(second ? "failure" : "success");
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        })) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        Assert.Equal(AgentCheckState.Success, Assert.Single(await service.FetchAgentPullRequestsAsync("sample")).Checks);
        second = true;
        Assert.Equal(AgentCheckState.Failure, Assert.Single(await service.FetchAgentPullRequestsAsync("sample")).Checks);
        Assert.Equal(5, conditional);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"items\":[],\"total_count\":1001}", false)]
    [InlineData("{\"items\":[],\"total_count\":0,\"incomplete_results\":true}", false)]
    [InlineData("private sentinel", true)]
    public async Task Fetch_ReportsIncompleteSearchAndSanitizesErrors(string response, bool httpFailure) {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(httpFailure ? HttpStatusCode.Forbidden : HttpStatusCode.OK) {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        }))) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => service.FetchAgentPullRequestsAsync("sample"));
        Assert.DoesNotContain("private sentinel", exception.Message);
    }

    private static HttpResponseMessage Pull(string n, string author, string head, string? sha = null) => Json($$$$"""
        {"state":"open","html_url":"https://github.com/Sample/Project/pull/{{{{n}}}}","title":"Sample PR","body":"private sentinel",
        "user":{"login":"{{{{author}}}}"},"base":{"repo":{"full_name":"Sample/Project"}},
        "head":{"sha":"{{{{sha ?? "sha-" + n}}}}","ref":"feature/work","repo":{"full_name":"{{{{head}}}}"}}}
        """);
    private static HttpResponseMessage Search(string? n, bool more = false) {
        var response = Json("{\"total_count\":2,\"incomplete_results\":false,\"items\":[" +
            (n is null ? "" : "{\"pull_request\":{\"url\":\"https://api.github.com/repos/Sample/Project/pulls/" + n + "\"}}") + "]}");
        if (more) response.Headers.TryAddWithoutValidation("Link", "<https://api.github.com/search/issues?page=2>; rel=\"next\"");
        return response;
    }
    private static HttpResponseMessage Checks(string state, bool more = false) {
        var response = Json("{\"total_count\":1,\"check_runs\":[{\"status\":\"" + (state == "pending" ? "in_progress" : "completed") + "\",\"conclusion\":\"" + state + "\"}]}");
        if (more) response.Headers.TryAddWithoutValidation("Link", "<https://api.github.com/check-runs?page=2>; rel=\"next\"");
        return response;
    }
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class StalledBody(TaskCompletionSource<bool> reading) : Stream {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
            reading.TrySetResult(true);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
