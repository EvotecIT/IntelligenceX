using System.Collections.Generic;
using System.Threading.Tasks;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Cli.GitHub;

internal static class GitHubOverviewSectionBuilder {
    public static async Task<UsageTelemetryOverviewProviderSection> BuildAsync(string login, IReadOnlyList<string>? repositoryOwners = null) {
        var snapshot = await new GitHubOverviewDataCollector()
            .CollectAsync(login, repositoryOwners)
            .ConfigureAwait(false);
        return GitHubOverviewSectionProjector.Project(snapshot);
    }

    public static async Task<UsageTelemetryOverviewProviderSection> BuildOwnerImpactOnlyAsync(IReadOnlyList<string> repositoryOwners) {
        var snapshot = await new GitHubOverviewDataCollector()
            .CollectOwnerImpactOnlyAsync(repositoryOwners)
            .ConfigureAwait(false);
        return GitHubOverviewSectionProjector.Project(snapshot);
    }
}
