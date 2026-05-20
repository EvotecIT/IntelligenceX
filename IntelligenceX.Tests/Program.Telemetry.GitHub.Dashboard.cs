using System;
using System.Collections.Generic;
#if !NET472
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
#endif
using System.Linq;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
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

    private static void TestGitHubDashboardServiceReusesFreshCachedDashboard() {
        var graphqlPayload = JsonSerializer.Serialize(new {
            data = new {
                user = new {
                    contributionsCollection = new {
                        totalCommitContributions = 1,
                        totalIssueContributions = 0,
                        totalPullRequestContributions = 0,
                        totalPullRequestReviewContributions = 0,
                        contributionCalendar = new {
                            totalContributions = 1,
                            weeks = new[] {
                                new {
                                    contributionDays = new[] {
                                        new {
                                            date = "2026-05-20",
                                            contributionCount = 1,
                                            color = "#40c463"
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                repositoryOwner = new {
                    repositories = new {
                        pageInfo = new {
                            hasNextPage = false,
                            endCursor = (string?)null
                        },
                        nodes = Array.Empty<object>()
                    }
                }
            }
        });

        var graphqlRequests = 0;
        var orgRequests = 0;
        using var http = new HttpClient(new GitHubDashboardDelegateHttpMessageHandler((request, _) => {
            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/user") {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("{\"login\":\"octocat\",\"name\":\"Octo Cat\"}", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Get && request.RequestUri?.AbsolutePath == "/user/orgs") {
                orgRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Post && request.RequestUri?.AbsolutePath == "/graphql") {
                graphqlRequests++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                    Content = new StringContent(graphqlPayload, Encoding.UTF8, "application/json")
                });
            }

            throw new InvalidOperationException("Unexpected request: " + request.Method + " " + request.RequestUri);
        })) {
            BaseAddress = new Uri("https://cache.github.test")
        };
        using var service = new GitHubDashboardService(http, disposeHttpClient: false);

        var first = service.FetchAsync("octocat").GetAwaiter().GetResult();
        var second = service.FetchAsync("octocat").GetAwaiter().GetResult();

        AssertEqual(1, first.Contributions.TotalContributions, "github dashboard cache first contribution total");
        AssertEqual(1, second.Contributions.TotalContributions, "github dashboard cache second contribution total");
        AssertEqual(2, graphqlRequests, "github dashboard cache avoids repeated contribution and repository GraphQL calls");
        AssertEqual(1, orgRequests, "github dashboard cache avoids repeated organization lookup");
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
#endif
}
