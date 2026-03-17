using System.Diagnostics;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tray.Services;

public sealed class GitHubService {
    private string? _cachedToken;

    public async Task<GitHubDashboardData?> FetchAsync(CancellationToken ct = default) {
        var token = await ResolveTokenAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)) {
            throw new InvalidOperationException(
                "GitHub authentication not available. Set INTELLIGENCEX_GITHUB_TOKEN, GITHUB_TOKEN, or GH_TOKEN. `gh auth token` is supported only as an optional fallback.");
        }

        using var dashboard = new GitHubDashboardService(token);
        return await dashboard.FetchAsync(cancellationToken: ct).ConfigureAwait(false);
    }

    private static async Task<string?> RunGhAsync(string[] args, CancellationToken ct) {
        try {
            var psi = new ProcessStartInfo("gh") {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var arg in args) {
                psi.ArgumentList.Add(arg);
            }

            using var proc = Process.Start(psi);
            if (proc == null) {
                return null;
            }

            var output = await proc.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return proc.ExitCode == 0 ? NormalizeOptional(output) : null;
        } catch {
            return null;
        }
    }

    private async Task<string?> ResolveTokenAsync(CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(_cachedToken)) {
            return _cachedToken;
        }

        _cachedToken = GitHubDashboardService.ResolveTokenFromEnvironment()
                       ?? await RunGhAsync(["auth", "token"], ct).ConfigureAwait(false);
        return _cachedToken;
    }

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
