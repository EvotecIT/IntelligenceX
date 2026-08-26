using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.GitHub;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class GitHubNotificationServiceTests {
    [Fact]
    public async Task FetchAsync_ProjectsInboxThreadsAndActionReasons() {
        var handler = new RecordingHandler((request, _) => {
            var response = Json(HttpStatusCode.OK, """
                [
                  {
                    "id": "101",
                    "repository": {
                      "full_name": "EvotecIT/OfficeIMO",
                      "html_url": "https://github.com/EvotecIT/OfficeIMO"
                    },
                    "subject": {
                      "title": "Review notification layout",
                      "url": "https://api.github.com/repos/EvotecIT/OfficeIMO/pulls/2394",
                      "type": "PullRequest"
                    },
                    "reason": "review_requested",
                    "unread": true,
                    "updated_at": "2026-08-26T10:58:00Z",
                    "last_read_at": null
                  },
                  {
                    "id": "102",
                    "repository": {
                      "full_name": "EvotecIT/CasaRay",
                      "html_url": "https://github.com/EvotecIT/CasaRay"
                    },
                    "subject": {
                      "title": "Energy flow discussion",
                      "url": "https://api.github.com/repos/EvotecIT/CasaRay/issues/184",
                      "type": "Issue"
                    },
                    "reason": "subscribed",
                    "unread": true,
                    "updated_at": "2026-08-26T10:00:00Z",
                    "last_read_at": "2026-08-26T09:00:00Z"
                  }
                ]
                """);
            response.Headers.Add("X-Poll-Interval", "90");
            response.Headers.Add("X-RateLimit-Remaining", "4998");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        var snapshot = await service.FetchAsync(new GitHubNotificationQuery { Limit = 12 });

        Assert.Equal(2, snapshot.Threads.Count);
        Assert.True(snapshot.Threads[0].IsActionRequired);
        Assert.False(snapshot.Threads[1].IsActionRequired);
        Assert.Equal("https://github.com/EvotecIT/OfficeIMO/pull/2394", snapshot.Threads[0].OpenUrl);
        Assert.Equal("https://github.com/EvotecIT/CasaRay/issues/184", snapshot.Threads[1].OpenUrl);
        Assert.Equal(TimeSpan.FromSeconds(90), snapshot.RecommendedPollInterval);
        Assert.Equal(4998, snapshot.RateLimitRemaining);
        Assert.Contains("all=false", handler.Requests[0].RequestUri?.Query);
        Assert.Contains("participating=false", handler.Requests[0].RequestUri?.Query);
        Assert.Contains("per_page=12", handler.Requests[0].RequestUri?.Query);
    }

    [Fact]
    public async Task FetchAsync_ReusesSnapshotUntilPollIntervalExpires() {
        var handler = new RecordingHandler((_, _) => {
            var response = Json(HttpStatusCode.OK, "[]");
            response.Headers.Add("X-Poll-Interval", "60");
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        var first = await service.FetchAsync();
        var second = await service.FetchAsync();

        Assert.Same(first, second);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task FetchAsync_FollowsPaginationForCompleteInbox() {
        var handler = new RecordingHandler((request, _) => {
            var page = request.RequestUri?.Query.Contains("page=2", StringComparison.Ordinal) == true ? 2 : 1;
            var response = Json(HttpStatusCode.OK, NotificationJson(page.ToString(), "EvotecIT/Repo" + page));
            if (page == 1) {
                response.Headers.Add("Link", "<https://api.github.com/notifications?all=false&participating=false&per_page=50&page=2>; rel=\"next\"");
            }

            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        var snapshot = await service.FetchAsync();

        Assert.Equal(2, snapshot.Threads.Count);
        Assert.Equal("EvotecIT/Repo1", snapshot.Threads[0].RepositoryNameWithOwner);
        Assert.Equal("EvotecIT/Repo2", snapshot.Threads[1].RepositoryNameWithOwner);
        Assert.Collection(handler.Requests,
            request => { Assert.Contains("per_page=50", request.RequestUri?.Query); Assert.Contains("page=1", request.RequestUri?.Query); },
            request => Assert.Contains("page=2", request.RequestUri?.Query));
    }

    [Fact]
    public async Task FetchAsync_KeepsPageSizeStableForFiniteLimitsAboveOnePage() {
        var handler = new RecordingHandler((request, _) => {
            var secondPage = request.RequestUri?.Query.Contains("page=2", StringComparison.Ordinal) == true;
            var response = Json(HttpStatusCode.OK, secondPage
                ? NotificationPageJson(51, 20)
                : NotificationPageJson(1, 50));
            if (!secondPage) {
                response.Headers.Add("Link", "<https://api.github.com/notifications?all=false&participating=false&per_page=50&page=2>; rel=\"next\"");
            }

            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        var snapshot = await service.FetchAsync(new GitHubNotificationQuery { Limit = 60 });

        Assert.Equal(60, snapshot.Threads.Count);
        Assert.Equal("1", snapshot.Threads[0].Id);
        Assert.Equal("60", snapshot.Threads[59].Id);
        Assert.All(handler.Requests, request => Assert.Contains("per_page=50", request.RequestUri?.Query));
    }

    [Fact]
    public async Task FetchAsync_CoalescesConcurrentRequests() {
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async (_, _) => {
            requestStarted.TrySetResult(true);
            await releaseRequest.Task.ConfigureAwait(false);
            var response = Json(HttpStatusCode.OK, "[]");
            response.Headers.Add("X-Poll-Interval", "60");
            return response;
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        var first = service.FetchAsync();
        await requestStarted.Task;
        var second = service.FetchAsync();
        Assert.Single(handler.Requests);

        releaseRequest.TrySetResult(true);
        var snapshots = await Task.WhenAll(first, second);

        Assert.Same(snapshots[0], snapshots[1]);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ThreadAction_WaitsForActiveFetchThenInvalidatesItsSnapshot() {
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRequest = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var getCount = 0;
        var handler = new RecordingHandler(async (request, _) => {
            if (request.Method == HttpMethod.Get && Interlocked.Increment(ref getCount) == 1) {
                requestStarted.TrySetResult(true);
                await releaseRequest.Task.ConfigureAwait(false);
            }

            return request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, "[]")
                : new HttpResponseMessage(HttpStatusCode.ResetContent);
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        var fetch = service.FetchAsync();
        await requestStarted.Task;
        var markRead = service.MarkReadAsync("123");
        Assert.Single(handler.Requests);

        releaseRequest.TrySetResult(true);
        await Task.WhenAll(fetch, markRead);
        await service.FetchAsync();

        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("GET", handler.Requests[0].Method.Method);
        Assert.Equal("PATCH", handler.Requests[1].Method.Method);
        Assert.Equal("GET", handler.Requests[2].Method.Method);
    }

    [Fact]
    public void Constructor_RejectsPlaintextApiBaseUrl() {
        Assert.Throws<ArgumentException>(() => new GitHubNotificationService("token", "http://github.example.test/api/v3"));
    }

    [Fact]
    public async Task ThreadActions_UseSafeGitHubMethodsAndInvalidateCache() {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get
                ? Json(HttpStatusCode.OK, "[]")
                : new HttpResponseMessage(request.Method.Method == "PATCH" ? HttpStatusCode.ResetContent : HttpStatusCode.NoContent)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.com/") };
        using var service = new GitHubNotificationService(http, disposeHttpClient: false);

        await service.FetchAsync();
        await service.MarkReadAsync("123");
        await service.MarkDoneAsync("456");
        await service.FetchAsync();

        Assert.Collection(handler.Requests,
            request => Assert.Equal("GET", request.Method.Method),
            request => { Assert.Equal("PATCH", request.Method.Method); Assert.Equal("/notifications/threads/123", request.RequestUri?.AbsolutePath); },
            request => { Assert.Equal("DELETE", request.Method.Method); Assert.Equal("/notifications/threads/456", request.RequestUri?.AbsolutePath); },
            request => Assert.Equal("GET", request.Method.Method));
        await Assert.ThrowsAsync<ArgumentException>(() => service.MarkReadAsync("../../user"));
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) {
        return new HttpResponseMessage(statusCode) {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string NotificationJson(string id, string repository) {
        return "[{\"id\":\"" + id + "\",\"repository\":{\"full_name\":\"" + repository
               + "\",\"html_url\":\"https://github.com/" + repository
               + "\"},\"subject\":{\"title\":\"Notification " + id
               + "\",\"url\":\"https://api.github.com/repos/" + repository
               + "/issues/" + id + "\",\"type\":\"Issue\"},\"reason\":\"mention\",\"unread\":true,\"updated_at\":\"2026-08-26T10:58:00Z\",\"last_read_at\":null}]";
    }

    private static string NotificationPageJson(int startId, int count) {
        var builder = new StringBuilder("[");
        for (var index = 0; index < count; index++) {
            if (index > 0) {
                builder.Append(',');
            }

            var id = (startId + index).ToString();
            var item = NotificationJson(id, "EvotecIT/Repo" + id);
            builder.Append(item, 1, item.Length - 2);
        }

        return builder.Append(']').ToString();
    }

    private sealed class RecordingHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) {
            _send = send;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            Requests.Add(request);
            return _send(request, cancellationToken);
        }
    }
}
