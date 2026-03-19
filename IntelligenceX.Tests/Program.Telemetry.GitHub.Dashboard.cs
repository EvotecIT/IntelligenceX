using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestGitHubDashboardServiceExplicitSelfLookupKeepsAuthenticatedOrganizations() {
        var graphqlPayload = JsonSerializer.Serialize(new {
            data = new {
                repositoryOwner = new {
                    repositories = new {
                        pageInfo = new {
                            hasNextPage = false,
                            endCursor = (string?)null
                        },
                        nodes = new object[] {
                            new {
                                nameWithOwner = "private-org/repo-a",
                                stargazerCount = 7,
                                forkCount = 1,
                                description = "private org repo",
                                primaryLanguage = new {
                                    name = "C#",
                                    color = "#178600"
                                }
                            }
                        }
                    }
                }
            }
        });

        var requests = new List<string>();
        using var http = new HttpClient(new GitHubDashboardDelegateHttpMessageHandler((request, _) => {
            requests.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/user") {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("{\"login\":\"octocat\",\"name\":\"Octo Cat\"}", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/user/orgs") {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("[{\"login\":\"private-org\"}]", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/graphql") {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(graphqlPayload, Encoding.UTF8, "application/json")
                });
            }

            throw new InvalidOperationException("Unexpected request: " + request.Method + " " + request.RequestUri);
        })) {
            BaseAddress = new Uri("https://api.github.com")
        };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);

        var dashboard = service.FetchAsync("octocat").GetAwaiter().GetResult();

        AssertContainsText(string.Join("\n", requests), "/user/orgs", "github dashboard self lookup uses authenticated org endpoint");
        AssertEqual(false, requests.Any(path => path.Contains("/users/octocat/orgs", StringComparison.OrdinalIgnoreCase)), "github dashboard avoids public org lookup for authenticated self");
        AssertEqual(true, dashboard.AllRepos.Any(static repo => string.Equals(repo.NameWithOwner, "private-org/repo-a", StringComparison.OrdinalIgnoreCase)), "github dashboard includes authenticated org repositories");
    }

    private static void TestGitHubDashboardRepositoryRankingDeduplicatesOverlappingRepositories() {
        var ranked = GitHubDashboardRepositoryRanking.BuildTopRepositories(
            new[] {
                new GitHubRepoInfo("octocat/shared", 10, 2, "user copy", "C#", "#178600"),
                new GitHubRepoInfo("OctoCat/Shared", 42, 9, "org copy", "C#", "#178600"),
                new GitHubRepoInfo("octocat/solo", 11, 1, "solo", "PowerShell", "#012456")
            },
            limit: 8);

        AssertEqual(2, ranked.Count, "github ranking deduped repository count");
        AssertEqual("OctoCat/Shared", ranked[0].NameWithOwner, "github ranking keeps strongest duplicate entry");
        AssertEqual(42, ranked[0].Stars, "github ranking duplicate keeps higher stars");
        AssertEqual("octocat/solo", ranked[1].NameWithOwner, "github ranking preserves distinct repository");
    }

    private static void TestGitHubDashboardRepositoryRankingOrdersAndCapsRepositories() {
        var ranked = GitHubDashboardRepositoryRanking.BuildTopRepositories(
            Enumerable.Range(1, 10)
                .Select(index => new GitHubRepoInfo(
                    "octocat/repo-" + index.ToString("00"),
                    100 - index,
                    index % 3,
                    null,
                    "C#",
                    "#178600")),
            limit: 5);

        AssertEqual(5, ranked.Count, "github ranking cap");
        AssertEqual("octocat/repo-01", ranked[0].NameWithOwner, "github ranking highest stars first");
        AssertEqual("octocat/repo-05", ranked[4].NameWithOwner, "github ranking respects limit ordering");
    }

    private sealed class GitHubDashboardDelegateHttpMessageHandler : HttpMessageHandler {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _sendAsync;

        public GitHubDashboardDelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) {
            _sendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            return _sendAsync(request, cancellationToken);
        }
    }
}
