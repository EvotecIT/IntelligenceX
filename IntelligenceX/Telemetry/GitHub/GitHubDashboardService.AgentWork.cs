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
        IReadOnlyList<AgentPullRequestState> prs;
        string? diagnostic = null;
        try {
            prs = await FetchAgentPullRequestsAsync(login, cancellationToken).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            prs = Array.Empty<AgentPullRequestState>();
            diagnostic = "request-timeout";
        } catch (HttpRequestException) {
            prs = Array.Empty<AgentPullRequestState>();
            diagnostic = "request-failed";
        } catch (IOException) {
            prs = Array.Empty<AgentPullRequestState>();
            diagnostic = "request-failed";
        } catch (InvalidOperationException) {
            prs = Array.Empty<AgentPullRequestState>();
            diagnostic = "request-failed";
        } catch (JsonException) {
            prs = Array.Empty<AgentPullRequestState>();
            diagnostic = "request-failed";
        }
        var diagnostics = diagnostic is null ? local.Diagnostics : local.Diagnostics.Concat(new[] {
            new AgentSourceDiagnostic { Provider = "github", Code = diagnostic }
        }).ToArray();
        return AgentWorkStateRules.Evaluate(local.Sessions, prs, local.CapturedAtUtc, staleAfter, diagnostics) with { Scan = local.Scan };
    }

    private const string AgentPullRequestQuery = """
        query($query: String!, $cursor: String) {
          search(query: $query, type: ISSUE, first: 100, after: $cursor) {
            issueCount
            pageInfo { hasNextPage endCursor }
            nodes { ... on PullRequest {
              url title state headRefName headRefOid
              author { login }
              repository { nameWithOwner }
              headRepository { nameWithOwner }
              statusCheckRollup { state }
            } }
          }
        }
        """;

    /// <summary>Reads open authored PRs and incoming review requests using the existing GitHub client and credentials.</summary>
    /// <remarks>Searches are paginated. GitHub's 1,000-result search ceiling is reported as an error, never silently truncated.</remarks>
    /// <param name="login">Selected user; null uses the authenticated user.</param>
    /// <param name="cancellationToken">Cancels network operations.</param>
    public async Task<IReadOnlyList<AgentPullRequestState>> FetchAgentPullRequestsAsync(string? login = null,
        CancellationToken cancellationToken = default) {
        var user = NormalizeOptional(login) ?? await GetAuthenticatedLoginAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(user) || user!.Any(static c => !char.IsLetterOrDigit(c) && c != '-')) {
            throw new ArgumentException("A GitHub login is required.", nameof(login));
        }
        var results = new Dictionary<string, AgentPullRequestState>(StringComparer.OrdinalIgnoreCase);
        foreach (var reviews in new[] { false, true }) {
            string? cursor = null;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            do {
                cancellationToken.ThrowIfCancellationRequested();
                using var document = await QueryAgentPullRequestsAsync(
                    "is:pr is:open " + (reviews ? "review-requested:" : "author:") + user, cursor, cancellationToken).ConfigureAwait(false);
                var search = AgentSessionLogReader.Object(AgentSessionLogReader.Object(document.RootElement, "data"), "search");
                if (search.ValueKind != JsonValueKind.Object || !search.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array) {
                    throw new InvalidOperationException("GitHub returned an incomplete pull request response.");
                }
                if (ReadInt(search, "issueCount") > 1000) throw new InvalidOperationException("GitHub pull request search exceeds the 1,000-result ceiling.");
                foreach (var node in nodes.EnumerateArray()) {
                    if (ReadString(node, "state") != "OPEN") continue;
                    var url = ReadString(node, "url");
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var prUri) || prUri.Scheme != Uri.UriSchemeHttps) continue;
                    var repository = ReadStringFromNested(node, "repository", "nameWithOwner");
                    var sha = ReadString(node, "headRefOid");
                    var checkState = ReadStringFromNested(node, "statusCheckRollup", "state");
                    var pr = new AgentPullRequestState {
                        Url = url, Title = ReadString(node, "title"), Repository = repository,
                        HeadRepository = NormalizeOptional(ReadStringFromNested(node, "headRepository", "nameWithOwner")),
                        Branch = ReadString(node, "headRefName"),
                        IsAuthor = string.Equals(ReadStringFromNested(node, "author", "login"), user, StringComparison.OrdinalIgnoreCase),
                        ReviewRequested = reviews,
                        Checks = checkState switch {
                            "SUCCESS" => AgentCheckState.Success,
                            "FAILURE" or "ERROR" => AgentCheckState.Failure,
                            "PENDING" or "EXPECTED" => AgentCheckState.Pending,
                            _ => AgentCheckState.Unknown
                        },
                        ChecksUrl = string.IsNullOrEmpty(sha) ? null : new Uri(prUri, "/" + repository + "/commit/" + sha + "/checks").AbsoluteUri
                    };
                    if (results.TryGetValue(url, out var existing)) pr = pr with { ReviewRequested = existing.ReviewRequested || reviews };
                    results[url] = pr;
                }
                var pageInfo = AgentSessionLogReader.Object(search, "pageInfo");
                if (!ReadBool(pageInfo, "hasNextPage")) break;
                cursor = NormalizeOptional(ReadString(pageInfo, "endCursor"));
                if (cursor is null || !cursors.Add(cursor)) throw new InvalidOperationException("GitHub returned an invalid pagination cursor.");
            } while (true);
        }
        return results.Values.OrderBy(static pr => pr.Url, StringComparer.Ordinal).ToArray();
    }

    private async Task<JsonDocument> QueryAgentPullRequestsAsync(string query, string? cursor, CancellationToken cancellationToken) {
        // Keep this path usable with JSON reflection disabled without changing unrelated dashboard serialization.
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteString("query", AgentPullRequestQuery);
            writer.WriteStartObject("variables");
            writer.WriteString("query", query);
            writer.WriteString("cursor", cursor);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "/graphql") {
            Content = new StringContent(Encoding.UTF8.GetString(buffer.ToArray()), Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("GitHub agent work request failed with HTTP " + (int)response.StatusCode + ".");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (document.RootElement.TryGetProperty("errors", out _)) {
            document.Dispose();
            throw new InvalidOperationException("GitHub returned pull request query errors.");
        }
        return document;
    }
}
