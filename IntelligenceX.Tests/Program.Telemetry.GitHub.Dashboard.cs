using System.Linq;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
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
}
