#if !NET472
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestGitHubOwnerScopeResolverReturnsAdministeredOrganizationsWithPublicRepos() {
        var resolver = new GitHubOwnerScopeResolver(login => Task.FromResult(ParseJson("""
{
  "data": {
    "user": {
      "organizations": {
        "nodes": [
          {
            "login": "EvotecIT",
            "viewerCanAdminister": true,
            "repositories": { "totalCount": 114 }
          },
          {
            "login": "PrivateEmptyOrg",
            "viewerCanAdminister": true,
            "repositories": { "totalCount": 0 }
          },
          {
            "login": "CommunityOrg",
            "viewerCanAdminister": false,
            "repositories": { "totalCount": 25 }
          },
          {
            "login": "evotecit",
            "viewerCanAdminister": true,
            "repositories": { "totalCount": 114 }
          }
        ]
      }
    }
  }
}
""")));

        var owners = resolver.ResolveAdministeredOwnersAsync("przemyslawklys")
            .GetAwaiter()
            .GetResult();

        AssertEqual(1, owners.Count, "github owner scope resolver owner count");
        AssertEqual("EvotecIT", owners[0], "github owner scope resolver owner");
    }

    private static void TestGitHubOverviewDataCollectorAppendsCorrelatedOwnersForUserRuns() {
        var capturedOwners = Array.Empty<string>();
        var collector = new GitHubOverviewDataCollector(
            new GitHubContributionCalendarClient((login, from, to) => Task.FromResult(ParseJson("""
{
  "data": {
    "user": {
      "login": "przemyslawklys",
      "name": "Przemyslaw Klys",
      "url": "https://github.com/przemyslawklys",
      "contributionsCollection": {
        "contributionCalendar": {
          "totalContributions": 5,
          "weeks": [
            {
              "contributionDays": [
                { "date": "2026-03-12", "contributionCount": 5, "weekday": 4, "color": "#111", "contributionLevel": "SECOND_QUARTILE" }
              ]
            }
          ]
        }
      }
    }
  }
}
"""))),
            owners => {
                capturedOwners = owners.ToArray();
                return Task.FromResult(new GitHubRepositoryImpactSummary(Array.Empty<GitHubRepositoryOwnerImpact>(), Array.Empty<GitHubRepositoryImpactRepository>()));
            },
            login => Task.FromResult<IReadOnlyList<string>>(new[] { "EvotecIT", "AnotherOrg", "przemyslawklys" }));

        var snapshot = collector.CollectAsync(" przemyslawklys ")
            .GetAwaiter()
            .GetResult();

        AssertEqual(3, snapshot.RepositoryOwners.Count, "github overview collector correlated owner count");
        AssertEqual("przemyslawklys", snapshot.RepositoryOwners[0], "github overview collector login owner");
        AssertEqual("EvotecIT", snapshot.RepositoryOwners[1], "github overview collector first correlated owner");
        AssertEqual("AnotherOrg", snapshot.RepositoryOwners[2], "github overview collector second correlated owner");
        AssertEqual(2, snapshot.AutoCorrelatedOwners.Count, "github overview collector auto-correlated owner count");
        AssertEqual("EvotecIT", snapshot.AutoCorrelatedOwners[0], "github overview collector auto-correlated owner first");
        AssertEqual("AnotherOrg", snapshot.AutoCorrelatedOwners[1], "github overview collector auto-correlated owner second");
        AssertEqual(3, capturedOwners.Length, "github overview collector repository impact owner count");
    }

    private static void TestGitHubOverviewDataCollectorSupportsOwnerOnlyRuns() {
        var collector = new GitHubOverviewDataCollector(
            new GitHubContributionCalendarClient((login, from, to) => throw new InvalidOperationException("Calendar client should not be used for owner-only runs.")),
            owners => Task.FromResult(new GitHubRepositoryImpactSummary(
                new[] {
                    new GitHubRepositoryOwnerImpact(
                        owner: "EvotecIT",
                        repositoryCount: 2,
                        totalStars: 42,
                        totalForks: 9,
                        repositories: new[] {
                            new GitHubRepositoryImpactRepository("EvotecIT/IntelligenceX", null, 24, 5, "C#", "#178600", "2026-03-10T00:00:00Z"),
                            new GitHubRepositoryImpactRepository("EvotecIT/PSWriteHTML", null, 18, 4, "PowerShell", "#012456", "2026-03-09T00:00:00Z")
                        },
                        topRepository: new GitHubRepositoryImpactRepository("EvotecIT/IntelligenceX", null, 24, 5, "C#", "#178600", "2026-03-10T00:00:00Z"))
                },
                Array.Empty<GitHubRepositoryImpactRepository>())));

        var snapshot = collector.CollectOwnerImpactOnlyAsync(new[] { " EvotecIT ", "evotecit" })
            .GetAwaiter()
            .GetResult();

        AssertEqual(true, snapshot.OwnerImpactOnly, "github overview collector owner-only flag");
        AssertEqual(true, snapshot.Calendar is null, "github overview collector owner-only calendar absent");
        AssertEqual(true, snapshot.PreviousYearCalendar is null, "github overview collector owner-only comparison calendar absent");
        AssertEqual(1, snapshot.RepositoryOwners.Count, "github overview collector owner-only distinct owner count");
        AssertEqual("EvotecIT", snapshot.RepositoryOwners[0], "github overview collector owner-only normalized owner");
        AssertEqual(42, snapshot.RepositoryImpact?.TotalStars ?? 0, "github overview collector owner-only repository impact");
    }

    private static void TestGitHubOverviewSectionProjectorUsesWindowEndForCurrentStreak() {
        var snapshot = new GitHubOverviewDataSnapshot(
            RequestedLogin: "przemyslawklys",
            StartUtc: new DateTimeOffset(2025, 03, 13, 0, 0, 0, TimeSpan.Zero),
            EndUtc: new DateTimeOffset(2026, 03, 12, 0, 0, 0, TimeSpan.Zero),
            Calendar: new GitHubContributionCalendar(
                login: "przemyslawklys",
                name: "Przemyslaw Klys",
                profileUrl: "https://github.com/przemyslawklys",
                totalContributions: 12,
                days: new[] {
                    new GitHubContributionDay(new DateTime(2026, 03, 10, 0, 0, 0, DateTimeKind.Utc), 3, "#111", "SECOND_QUARTILE", 2),
                    new GitHubContributionDay(new DateTime(2026, 03, 11, 0, 0, 0, DateTimeKind.Utc), 4, "#222", "THIRD_QUARTILE", 3),
                    new GitHubContributionDay(new DateTime(2026, 03, 12, 0, 0, 0, DateTimeKind.Utc), 5, "#333", "FOURTH_QUARTILE", 4)
                }),
            PreviousYearCalendar: new GitHubContributionCalendar(
                login: "przemyslawklys",
                name: "Przemyslaw Klys",
                profileUrl: "https://github.com/przemyslawklys",
                totalContributions: 0,
                days: Array.Empty<GitHubContributionDay>()),
            RepositoryImpact: null,
            RepositoryOwners: new[] { "przemyslawklys" },
            AutoCorrelatedOwners: Array.Empty<string>(),
            OwnerImpactOnly: false);

        var section = GitHubOverviewSectionProjector.Project(snapshot);

        AssertEqual(3, section.LongestStreakDays, "github overview projector longest streak");
        AssertEqual(3, section.CurrentStreakDays, "github overview projector current streak honors window end");
    }

    private static void TestGitHubRepositoryObservabilityMapperBuildsSnapshot() {
        var snapshot = GitHubRepositoryObservabilityMapper.CreateSnapshot(
            new GitHubRepositoryImpactRepository(
                "EvotecIT/IntelligenceX",
                "https://github.com/EvotecIT/IntelligenceX",
                126,
                21,
                "C#",
                "#178600",
                "2026-03-16T09:55:00Z",
                watchers: 15,
                openIssues: 5,
                description: "Unified intelligence workspace",
                isArchived: false,
                isFork: false),
            new DateTimeOffset(2026, 03, 16, 10, 05, 0, TimeSpan.Zero));

        AssertEqual("EvotecIT/IntelligenceX", snapshot.RepositoryNameWithOwner, "github observability mapper repository");
        AssertEqual(126, snapshot.Stars, "github observability mapper stars");
        AssertEqual(21, snapshot.Forks, "github observability mapper forks");
        AssertEqual(15, snapshot.Watchers, "github observability mapper watchers");
        AssertEqual(5, snapshot.OpenIssues, "github observability mapper open issues");
        AssertEqual("Unified intelligence workspace", snapshot.Description, "github observability mapper description");
        AssertEqual("C#", snapshot.PrimaryLanguage, "github observability mapper language");
        AssertEqual(new DateTimeOffset(2026, 03, 16, 09, 55, 0, TimeSpan.Zero), snapshot.PushedAtUtc, "github observability mapper pushed at");
    }

    private static void TestGitHubRepositoryForkScoringRanksRecentPopularForksFirst() {
        var insights = GitHubRepositoryForkScoring.Score(
            new[] {
                new GitHubRepositoryForkRecord(
                    "someone/IntelligenceX",
                    "https://github.com/someone/IntelligenceX",
                    18,
                    3,
                    9,
                    2,
                    "High-signal fork",
                    "C#",
                    "2026-03-14T12:00:00Z",
                    "2026-03-14T12:00:00Z",
                    "2025-11-01T00:00:00Z",
                    false),
                new GitHubRepositoryForkRecord(
                    "archive/IntelligenceX",
                    "https://github.com/archive/IntelligenceX",
                    40,
                    6,
                    4,
                    30,
                    "",
                    "C#",
                    "2025-01-01T12:00:00Z",
                    "2025-01-01T12:00:00Z",
                    "2024-01-01T00:00:00Z",
                    true)
            },
            utcNow: () => new DateTimeOffset(2026, 03, 15, 0, 0, 0, TimeSpan.Zero));

        AssertEqual(2, insights.Count, "github fork scoring count");
        AssertEqual("someone/IntelligenceX", insights[0].Fork.RepositoryNameWithOwner, "github fork scoring top repository");
        AssertEqual("high", insights[0].Tier, "github fork scoring top tier");
        AssertEqual(true, insights[0].Score > insights[1].Score, "github fork scoring prefers recent active fork");
        AssertEqual("low", insights[1].Tier, "github fork scoring archived fork tier");
    }

    private static void TestGitHubRepositoryForkDiscoveryHandlesPartialPagesWithNextPage() {
        var calls = new List<(int First, string? After)>();
        var forks = GitHubRepositoryForkDiscoveryClient.GetForksForTestAsync(
                "EvotecIT/IntelligenceX",
                55,
                (owner, repository, first, after) => {
                    calls.Add((first, after));
                    if (calls.Count == 1) {
                        return Task.FromResult(CreateForkDiscoveryResponse(
                            CreateForkRepositoryBatch("batch1", 50, 100),
                            hasNextPage: true,
                            endCursor: "cursor-1"));
                    }

                    if (calls.Count == 2) {
                        return Task.FromResult(CreateForkDiscoveryResponse(
                            CreateForkRepositoryBatch("batch2", 1, 50),
                            hasNextPage: true,
                            endCursor: "cursor-2"));
                    }

                    return Task.FromResult(CreateForkDiscoveryResponse(
                        CreateForkRepositoryBatch("batch3", 4, 49),
                        hasNextPage: false,
                        endCursor: null));
                })
            .GetAwaiter()
            .GetResult();

        AssertEqual(55, forks.Count, "github fork discovery partial-page total count");
        AssertEqual(3, calls.Count, "github fork discovery partial-page call count");
        AssertEqual(50, calls[0].First, "github fork discovery first page size");
        AssertEqual(5, calls[1].First, "github fork discovery second page requested remaining count");
        AssertEqual(4, calls[2].First, "github fork discovery third page requested actual remaining count");
        AssertEqual("cursor-1", calls[1].After, "github fork discovery second page cursor");
        AssertEqual("cursor-2", calls[2].After, "github fork discovery third page cursor");
    }

    private static JsonElement CreateForkDiscoveryResponse(
        IReadOnlyList<string> repositories,
        bool hasNextPage,
        string? endCursor) {
        var nodes = string.Join(",", repositories.Select(static repository => """
{
  "nameWithOwner": "__REPOSITORY__",
  "url": "https://github.com/__REPOSITORY__",
  "description": "fork",
  "stargazerCount": 1,
  "forkCount": 0,
  "updatedAt": "2026-03-15T00:00:00Z",
  "createdAt": "2026-03-01T00:00:00Z",
  "pushedAt": "2026-03-15T00:00:00Z",
  "isArchived": false,
  "watchers": { "totalCount": 0 },
  "issues": { "totalCount": 0 },
  "primaryLanguage": { "name": "C#" }
}
""".Replace("__REPOSITORY__", repository)));
        return ParseJson($$"""
{
  "data": {
    "repository": {
      "forks": {
        "nodes": [{{nodes}}],
        "pageInfo": {
          "hasNextPage": {{hasNextPage.ToString().ToLowerInvariant()}},
          "endCursor": {{(endCursor is null ? "null" : "\"" + endCursor + "\"")}}
        }
      }
    }
  }
}
""");
    }

    private static IReadOnlyList<string> CreateForkRepositoryBatch(string prefix, int count, int startIndex) {
        var repositories = new List<string>(count);
        for (var i = 0; i < count; i++) {
            repositories.Add(prefix + (startIndex + i).ToString() + "/IntelligenceX");
        }

        return repositories;
    }

    private static void TestGitHubContributionCalendarClientStitchesNonOverlappingWindows() {
        var calls = new List<(DateTimeOffset From, DateTimeOffset To)>();
        var client = new GitHubContributionCalendarClient((login, from, to) => {
            calls.Add((from, to));
            var payload = calls.Count switch {
                1 => """
{
  "data": {
    "user": {
      "login": "octocat",
      "name": "The Octocat",
      "url": "https://github.com/octocat",
      "contributionsCollection": {
        "contributionCalendar": {
          "totalContributions": 3,
          "weeks": [
            {
              "contributionDays": [
                { "date": "2024-02-29", "contributionCount": 1, "weekday": 4, "color": "#000", "contributionLevel": "FIRST_QUARTILE" },
                { "date": "2025-02-27", "contributionCount": 2, "weekday": 4, "color": "#111", "contributionLevel": "SECOND_QUARTILE" }
              ]
            }
          ]
        }
      }
    }
  }
}
""",
                _ => """
{
  "data": {
    "user": {
      "login": "octocat",
      "name": "The Octocat",
      "url": "https://github.com/octocat",
      "contributionsCollection": {
        "contributionCalendar": {
          "totalContributions": 7,
          "weeks": [
            {
              "contributionDays": [
                { "date": "2025-02-27", "contributionCount": 2, "weekday": 4, "color": "#111", "contributionLevel": "SECOND_QUARTILE" },
                { "date": "2025-02-28", "contributionCount": 3, "weekday": 5, "color": "#222", "contributionLevel": "THIRD_QUARTILE" },
                { "date": "2025-03-01", "contributionCount": 2, "weekday": 6, "color": "#333", "contributionLevel": "FOURTH_QUARTILE" }
              ]
            }
          ]
        }
      }
    }
  }
}
"""
            };

            return Task.FromResult(ParseJson(payload));
        });

        var calendar = client.GetUserContributionCalendarAsync(
                "octocat",
                new DateTimeOffset(2024, 02, 29, 12, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2025, 03, 01, 18, 0, 0, TimeSpan.Zero))
            .GetAwaiter()
            .GetResult();

        AssertEqual(3, calls.Count, "github contribution calendar window count");
        AssertEqual(new DateTimeOffset(2024, 02, 29, 0, 0, 0, TimeSpan.Zero), calls[0].From, "github contribution calendar first window start");
        AssertEqual(new DateTimeOffset(2025, 02, 27, 0, 0, 0, TimeSpan.Zero), calls[0].To, "github contribution calendar first window end");
        AssertEqual(new DateTimeOffset(2025, 02, 28, 0, 0, 0, TimeSpan.Zero), calls[1].From, "github contribution calendar second window start");
        AssertEqual(new DateTimeOffset(2025, 03, 01, 0, 0, 0, TimeSpan.Zero), calls[1].To, "github contribution calendar second window end");
        AssertEqual(new DateTimeOffset(2024, 02, 29, 0, 0, 0, TimeSpan.Zero), calls[2].From, "github contribution calendar summary window start");
        AssertEqual(new DateTimeOffset(2025, 03, 01, 0, 0, 0, TimeSpan.Zero), calls[2].To, "github contribution calendar summary window end");
        AssertEqual(8, calendar.TotalContributions, "github contribution calendar totals use deduped days");
        AssertEqual(4, calendar.Days.Count, "github contribution calendar deduped day count");
        AssertEqual(new DateTime(2024, 02, 29, 0, 0, 0, DateTimeKind.Utc), calendar.Days[0].Date, "github contribution calendar first date");
        AssertEqual(new DateTime(2025, 03, 01, 0, 0, 0, DateTimeKind.Utc), calendar.Days[3].Date, "github contribution calendar last date");
    }

    private static void TestGitHubContributionCalendarClientParsesIsoDatesDeterministically() {
        var client = new GitHubContributionCalendarClient((login, from, to) => Task.FromResult(ParseJson("""
{
  "data": {
    "user": {
      "login": "octocat",
      "name": "The Octocat",
      "url": "https://github.com/octocat",
      "contributionsCollection": {
        "contributionCalendar": {
          "totalContributions": 5,
          "weeks": [
            {
              "contributionDays": [
                { "date": "2026-03-10", "contributionCount": 5, "weekday": 2, "color": "#111", "contributionLevel": "SECOND_QUARTILE" }
              ]
            }
          ]
        }
      }
    }
  }
}
""")));

        var calendar = client.GetUserContributionCalendarAsync(
                "octocat",
                new DateTimeOffset(2026, 03, 10, 22, 30, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 03, 10, 22, 30, 0, TimeSpan.FromHours(2)))
            .GetAwaiter()
            .GetResult();

        AssertEqual(1, calendar.Days.Count, "github contribution calendar deterministic date count");
        AssertEqual(new DateTime(2026, 03, 10, 0, 0, 0, DateTimeKind.Utc), calendar.Days[0].Date, "github contribution calendar deterministic date");
        AssertEqual(DateTimeKind.Utc, calendar.Days[0].Date.Kind, "github contribution calendar deterministic date kind");
    }

    private static void TestGitHubContributionCalendarClientTreatsNullUserAsNotFound() {
        var client = new GitHubContributionCalendarClient((login, from, to) => Task.FromResult(ParseJson("""
{
  "data": {
    "user": null
  }
}
""")));

        try {
            client.GetUserContributionCalendarAsync(
                    "missing-user",
                    new DateTimeOffset(2026, 03, 10, 0, 0, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 03, 11, 0, 0, 0, TimeSpan.Zero))
                .GetAwaiter()
                .GetResult();
            throw new InvalidOperationException("Expected github contribution calendar null user to throw.");
        } catch (InvalidOperationException ex) {
            AssertContainsText(ex.Message, "missing-user", "github contribution calendar null user message");
        }
    }

    private static JsonElement ParseJson(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
#endif
