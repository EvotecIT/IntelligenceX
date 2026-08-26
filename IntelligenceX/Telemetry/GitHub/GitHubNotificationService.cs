using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Reads and manages the authenticated user's GitHub notification inbox while respecting GitHub polling guidance.
/// </summary>
public sealed class GitHubNotificationService : IDisposable {
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(60);
    private static readonly HttpMethod PatchMethod = new("PATCH");
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly HttpClient _http;
    private readonly bool _disposeHttpClient;

    /// <summary>
    /// Initializes a notification service using a GitHub personal access token (classic).
    /// </summary>
    /// <param name="token">A classic personal access token with notifications or repo scope.</param>
    /// <param name="apiBaseUrl">Optional GitHub-compatible API base URL.</param>
    public GitHubNotificationService(string token, string? apiBaseUrl = null)
        : this(CreateHttpClient(token, apiBaseUrl), disposeHttpClient: true) {
    }

    internal GitHubNotificationService(HttpClient httpClient, bool disposeHttpClient) {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _disposeHttpClient = disposeHttpClient;
    }

    /// <summary>
    /// Returns notification threads for the authenticated user, reusing a fresh snapshot until GitHub's poll interval expires.
    /// </summary>
    public async Task<GitHubNotificationSnapshot> FetchAsync(
        GitHubNotificationQuery? query = null,
        CancellationToken cancellationToken = default) {
        query ??= new GitHubNotificationQuery();
        var limit = Math.Max(0, query.Limit);
        var cacheKey = BuildCacheKey(query.IncludeRead, query.ParticipatingOnly, limit);
        var now = DateTimeOffset.UtcNow;
        var cached = ReadCache(cacheKey);
        if (cached is not null && now < cached.NextPollAtUtc) {
            return cached.Snapshot;
        }

        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            now = DateTimeOffset.UtcNow;
            cached = ReadCache(cacheKey);
            if (cached is not null && now < cached.NextPollAtUtc) {
                return cached.Snapshot;
            }

            var threads = new List<GitHubNotificationThread>();
            var page = 1;
            var pageSize = limit == 0 ? 50 : Math.Min(50, limit);
            var checkedAtUtc = DateTimeOffset.UtcNow;
            var pollInterval = cached?.Snapshot.RecommendedPollInterval ?? DefaultPollInterval;
            var rateLimitRemaining = cached?.Snapshot.RateLimitRemaining;
            DateTimeOffset? lastModifiedUtc = null;
            while (limit == 0 || threads.Count < limit) {
                using var request = new HttpRequestMessage(HttpMethod.Get, BuildNotificationsUrl(query, pageSize, page));
                if (page == 1 && cached?.LastModifiedUtc is DateTimeOffset cachedLastModifiedUtc) {
                    request.Headers.IfModifiedSince = cachedLastModifiedUtc;
                }

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                checkedAtUtc = DateTimeOffset.UtcNow;
                pollInterval = ReadPollInterval(response) ?? pollInterval;
                rateLimitRemaining = ReadIntHeader(response, "X-RateLimit-Remaining") ?? rateLimitRemaining;

                if (page == 1 && response.StatusCode == HttpStatusCode.NotModified && cached is not null) {
                    var unchanged = new GitHubNotificationSnapshot(cached.Snapshot.Threads, checkedAtUtc, pollInterval, rateLimitRemaining);
                    Store(cacheKey, unchanged, checkedAtUtc + pollInterval, cached.LastModifiedUtc);
                    return unchanged;
                }

                if (!response.IsSuccessStatusCode) {
                    throw new GitHubNotificationApiException(
                        (int)response.StatusCode,
                        "GitHub notifications request failed with HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ".");
                }

                if (page == 1) {
                    lastModifiedUtc = ReadLastModified(response);
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var document = JsonDocument.Parse(json);
                threads.AddRange(ParseThreads(document.RootElement));
                if (!HasNextPage(response) || (limit > 0 && threads.Count >= limit)) {
                    break;
                }

                page++;
            }

            IReadOnlyList<GitHubNotificationThread> resultThreads = limit > 0 && threads.Count > limit
                ? threads.Take(limit).ToArray()
                : threads;
            var snapshot = new GitHubNotificationSnapshot(resultThreads, checkedAtUtc, pollInterval, rateLimitRemaining);
            Store(cacheKey, snapshot, checkedAtUtc + pollInterval, lastModifiedUtc);
            return snapshot;
        } finally {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Marks one GitHub notification thread as read and invalidates cached inbox snapshots.
    /// </summary>
    public async Task MarkReadAsync(string threadId, CancellationToken cancellationToken = default) {
        await SendThreadActionAsync(PatchMethod, threadId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Marks one GitHub notification thread as done and invalidates cached inbox snapshots.
    /// </summary>
    public async Task MarkDoneAsync(string threadId, CancellationToken cancellationToken = default) {
        await SendThreadActionAsync(HttpMethod.Delete, threadId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose() {
        _operationGate.Dispose();
        if (_disposeHttpClient) {
            _http.Dispose();
        }
    }

    private async Task SendThreadActionAsync(HttpMethod method, string threadId, CancellationToken cancellationToken) {
        var normalizedId = NormalizeThreadId(threadId);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            using var request = new HttpRequestMessage(method, "notifications/threads/" + normalizedId);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) {
                throw new GitHubNotificationApiException(
                    (int)response.StatusCode,
                    "GitHub notification action failed with HTTP " + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) + ".");
            }

            lock (_cacheGate) {
                _cache.Clear();
            }
        } finally {
            _operationGate.Release();
        }
    }

    private CacheEntry? ReadCache(string cacheKey) {
        lock (_cacheGate) {
            _cache.TryGetValue(cacheKey, out var cached);
            return cached;
        }
    }

    private static string BuildNotificationsUrl(GitHubNotificationQuery query, int pageSize, int page) {
        return "notifications?all=" + query.IncludeRead.ToString().ToLowerInvariant()
               + "&participating=" + query.ParticipatingOnly.ToString().ToLowerInvariant()
               + "&per_page=" + pageSize.ToString(CultureInfo.InvariantCulture)
               + "&page=" + page.ToString(CultureInfo.InvariantCulture);
    }

    private static bool HasNextPage(HttpResponseMessage response) {
        if (!response.Headers.TryGetValues("Link", out var values)) {
            return false;
        }

        return values.SelectMany(static value => value.Split(','))
            .Any(static link => link.IndexOf("rel=\"next\"", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static string NormalizeThreadId(string threadId) {
        var normalized = threadId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Any(static character => character < '0' || character > '9')) {
            throw new ArgumentException("GitHub notification thread id must contain digits only.", nameof(threadId));
        }

        return normalized!;
    }

    private static IReadOnlyList<GitHubNotificationThread> ParseThreads(JsonElement root) {
        if (root.ValueKind != JsonValueKind.Array) {
            return Array.Empty<GitHubNotificationThread>();
        }

        var threads = new List<GitHubNotificationThread>();
        foreach (var item in root.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }

            var id = ReadString(item, "id");
            var updatedAt = ReadTimestamp(item, "updated_at");
            if (string.IsNullOrWhiteSpace(id) || !updatedAt.HasValue) {
                continue;
            }

            var repository = TryGetObject(item, "repository");
            var subject = TryGetObject(item, "subject");
            var repositoryName = repository.HasValue ? ReadString(repository.Value, "full_name") : null;
            var repositoryUrl = repository.HasValue ? ReadString(repository.Value, "html_url") : null;
            var title = subject.HasValue ? ReadString(subject.Value, "title") : null;
            if (string.IsNullOrWhiteSpace(repositoryName) || string.IsNullOrWhiteSpace(repositoryUrl) || string.IsNullOrWhiteSpace(title)) {
                continue;
            }

            var subjectApiUrl = subject.HasValue ? ReadString(subject.Value, "url") : null;
            threads.Add(new GitHubNotificationThread(
                id!,
                repositoryName!,
                title!,
                subject.HasValue ? ReadString(subject.Value, "type") ?? "Unknown" : "Unknown",
                ReadString(item, "reason") ?? "unknown",
                ReadBoolean(item, "unread"),
                updatedAt.Value,
                ReadTimestamp(item, "last_read_at"),
                repositoryUrl!,
                BuildOpenUrl(subjectApiUrl, repositoryUrl!)));
        }

        return threads;
    }

    private static string BuildOpenUrl(string? subjectApiUrl, string repositoryUrl) {
        if (string.IsNullOrWhiteSpace(subjectApiUrl)
            || !Uri.TryCreate(subjectApiUrl, UriKind.Absolute, out var apiUri)
            || !string.Equals(apiUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)) {
            return repositoryUrl;
        }

        var segments = apiUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 5 || !string.Equals(segments[0], "repos", StringComparison.OrdinalIgnoreCase)) {
            return repositoryUrl;
        }

        var owner = Uri.EscapeDataString(Uri.UnescapeDataString(segments[1]));
        var repository = Uri.EscapeDataString(Uri.UnescapeDataString(segments[2]));
        var kind = segments[3];
        var value = Uri.EscapeDataString(Uri.UnescapeDataString(segments[4]));
        if (string.Equals(kind, "pulls", StringComparison.OrdinalIgnoreCase)) {
            return "https://github.com/" + owner + "/" + repository + "/pull/" + value;
        }

        if (string.Equals(kind, "issues", StringComparison.OrdinalIgnoreCase)) {
            return "https://github.com/" + owner + "/" + repository + "/issues/" + value;
        }

        if (string.Equals(kind, "commits", StringComparison.OrdinalIgnoreCase)) {
            return "https://github.com/" + owner + "/" + repository + "/commit/" + value;
        }

        return repositoryUrl;
    }

    private void Store(string cacheKey, GitHubNotificationSnapshot snapshot, DateTimeOffset nextPollAtUtc, DateTimeOffset? lastModifiedUtc) {
        lock (_cacheGate) {
            _cache[cacheKey] = new CacheEntry(snapshot, nextPollAtUtc, lastModifiedUtc);
        }
    }

    private static string BuildCacheKey(bool includeRead, bool participatingOnly, int limit) {
        return includeRead.ToString(CultureInfo.InvariantCulture) + ":"
               + participatingOnly.ToString(CultureInfo.InvariantCulture) + ":"
               + limit.ToString(CultureInfo.InvariantCulture);
    }

    private static TimeSpan? ReadPollInterval(HttpResponseMessage response) {
        var seconds = ReadIntHeader(response, "X-Poll-Interval");
        return seconds.HasValue && seconds.Value > 0 ? TimeSpan.FromSeconds(seconds.Value) : null;
    }

    private static int? ReadIntHeader(HttpResponseMessage response, string name) {
        if (!response.Headers.TryGetValues(name, out var values)) {
            return null;
        }

        return int.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static DateTimeOffset? ReadLastModified(HttpResponseMessage response) {
        if (response.Content.Headers.LastModified.HasValue) {
            return response.Content.Headers.LastModified;
        }

        if (!response.Headers.TryGetValues("Last-Modified", out var values)) {
            return null;
        }

        return DateTimeOffset.TryParse(
            values.FirstOrDefault(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var value)
            ? value
            : null;
    }

    private static JsonElement? TryGetObject(JsonElement parent, string propertyName) {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;
    }

    private static string? ReadString(JsonElement parent, string propertyName) {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool ReadBoolean(JsonElement parent, string propertyName) {
        return parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement parent, string propertyName) {
        var value = ReadString(parent, propertyName);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp)
            ? timestamp
            : null;
    }

    private static HttpClient CreateHttpClient(string token, string? apiBaseUrl) {
        if (string.IsNullOrWhiteSpace(token)) {
            throw new ArgumentException("A GitHub personal access token is required.", nameof(token));
        }

        var baseUrl = string.IsNullOrWhiteSpace(apiBaseUrl) ? "https://api.github.com/" : apiBaseUrl!.TrimEnd('/') + "/";
        var baseUri = new Uri(baseUrl, UriKind.Absolute);
        if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) {
            throw new ArgumentException("The GitHub API base URL must use HTTPS so the personal access token is never sent in plaintext.", nameof(apiBaseUrl));
        }

        var client = new HttpClient {
            BaseAddress = baseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelligenceX", "0.1"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private sealed class CacheEntry {
        public CacheEntry(GitHubNotificationSnapshot snapshot, DateTimeOffset nextPollAtUtc, DateTimeOffset? lastModifiedUtc) {
            Snapshot = snapshot;
            NextPollAtUtc = nextPollAtUtc;
            LastModifiedUtc = lastModifiedUtc;
        }

        public GitHubNotificationSnapshot Snapshot { get; }
        public DateTimeOffset NextPollAtUtc { get; }
        public DateTimeOffset? LastModifiedUtc { get; }
    }
}
