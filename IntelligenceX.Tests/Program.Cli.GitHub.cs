#if !NET472
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
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
