using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Telemetry.GitHub;

public sealed partial class GitHubDashboardService {
    /// <summary>Enriches a completed local snapshot while preserving it if the optional GitHub source fails or times out.</summary>
    /// <remarks>Cancellation during optional remote enrichment returns a partial snapshot with a timeout diagnostic.</remarks>
    /// <param name="local">Already collected local work and source diagnostics.</param>
    /// <param name="login">Selected user; null uses the authenticated user.</param>
    /// <param name="staleAfter">Inactivity threshold for sessions linked to an open PR.</param>
    /// <param name="cancellationToken">Bounds optional remote enrichment without discarding completed local data.</param>
    public async Task<AgentWorkSnapshot> EnrichAgentWorkAsync(AgentWorkSnapshot local, string? login = null,
        TimeSpan? staleAfter = null, CancellationToken cancellationToken = default) {
        if (local is null) throw new ArgumentNullException(nameof(local));
        if (staleAfter.HasValue && staleAfter.Value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(staleAfter));
        IReadOnlyList<AgentPullRequestState> prs = local.PullRequests;
        string? diagnostic = null;
        try {
            prs = await FetchAgentPullRequestsAsync(login, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            diagnostic = "request-timeout";
        } catch (HttpRequestException) {
            diagnostic = "request-failed";
        } catch (IOException) {
            diagnostic = "request-failed";
        } catch (InvalidOperationException) {
            diagnostic = "request-failed";
        } catch (JsonException) {
            diagnostic = "request-failed";
        }
        var diagnostics = diagnostic is null ? local.Diagnostics : local.Diagnostics.Concat(new[] {
            new AgentSourceDiagnostic { Provider = "github", Code = diagnostic }
        }).ToArray();
        if (diagnostic is not null) return local with { Diagnostics = diagnostics };
        return AgentWorkStateRules.Evaluate(local.Sessions, prs, local.CapturedAtUtc, staleAfter, diagnostics) with {
            Scan = local.Scan, MetadataCheckedAtUtc = local.MetadataCheckedAtUtc, GitHubCheckedAtUtc = DateTimeOffset.UtcNow
        };
    }

    /// <summary>Reads open authored PRs and incoming reviews using conditional REST requests scoped to this service instance.</summary>
    /// <remarks>Search and check pages are paginated. ETags cache parsed metadata only, never response bodies.</remarks>
    /// <param name="login">Selected user; null uses the authenticated user.</param>
    /// <param name="cancellationToken">Cancels network operations.</param>
    public async Task<IReadOnlyList<AgentPullRequestState>> FetchAgentPullRequestsAsync(string? login = null,
        CancellationToken cancellationToken = default) {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        cancellationToken = bounded.Token;
        await _agentWorkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            var user = NormalizeOptional(login) ?? (_agentWorkLogin ??= await GetAuthenticatedLoginAsync(cancellationToken).ConfigureAwait(false));
            if (string.IsNullOrEmpty(user) || user!.Any(static c => !char.IsLetterOrDigit(c) && c != '-'))
                throw new ArgumentException("A GitHub login is required.", nameof(login));
            var paths = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var reviews in new[] { false, true }) {
                var query = "is:pr is:open " + (reviews ? "review-requested:" : "author:") + user;
                for (var page = 1; ; page++) {
                    var search = await GetAgentJsonAsync("/search/issues?q=" + Uri.EscapeDataString(query) + "&per_page=100&page=" + page,
                        ParseAgentSearch, cancellationToken).ConfigureAwait(false);
                    if (search.Total > 1000 || search.Incomplete) throw new InvalidOperationException("GitHub pull request search is incomplete or exceeds 1,000 results.");
                    foreach (var path in search.Paths) paths[path] = reviews || (paths.TryGetValue(path, out var previous) && previous);
                    if (!search.HasNext) break;
                    if (page >= 10) throw new InvalidOperationException("GitHub pull request search exceeds its pagination limit.");
                }
            }
            // Bounded fan-out keeps a large PR set from serializing hundreds of network round trips.
            using var requests = new SemaphoreSlim(4);
            var tasks = paths.Select(async pair => {
                await requests.WaitAsync(cancellationToken).ConfigureAwait(false);
                try { return await ReadAgentPullRequestAsync(pair.Key, pair.Value, user!, cancellationToken).ConfigureAwait(false); }
                finally { requests.Release(); }
            }).ToArray();
            var result = await Task.WhenAll(tasks).ConfigureAwait(false);
            return result.Where(static pr => pr.IsOpen).OrderBy(static pr => pr.Url, StringComparer.Ordinal).ToArray();
        } finally { _agentWorkGate.Release(); }
    }
}
