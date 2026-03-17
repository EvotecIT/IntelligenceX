using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tray.Services;

public sealed class GitHubService {
    public async Task<GitHubDashboardData?> FetchAsync(CancellationToken ct = default) {
        var token = GitHubDashboardService.ResolveTokenFromEnvironment();
        if (string.IsNullOrWhiteSpace(token)) return null;

        using var dashboard = new GitHubDashboardService(token);
        return await dashboard.FetchAsync(cancellationToken: ct).ConfigureAwait(false);
    }
}
