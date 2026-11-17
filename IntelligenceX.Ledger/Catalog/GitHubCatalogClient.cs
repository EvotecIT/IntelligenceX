using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IntelligenceX.Ledger.Catalog;

/// <summary>Raised when the shared hourly request budget or GitHub's own remaining allowance is used up.</summary>
internal sealed class GitHubBudgetExhaustedException() : Exception("GitHub catalog budget exhausted.");

/// <summary>
/// Paginated, conditional GitHub REST reads for the work catalog. Each page keeps its ETag and the compact rows
/// mapped from it, so an unchanged page answers 304 and costs no rate limit. Requests that do count share one
/// hourly budget, and reads stop while GitHub's remaining allowance is below the configured reserve.
/// </summary>
internal sealed class GitHubCatalogClient(HttpClient client, GitHubRequestBudget budget,
    ConcurrentDictionary<string, GitHubCatalogClient.Page> cache) {
    internal const string DefaultApiVersion = "2022-11-28";
    /// <summary>One cached page. Rows are already reduced to metadata, never bodies.</summary>
    internal sealed record Page(string? ETag, JsonElement[] Rows, string? Next, int Received = 0);
    private int _requests, _notModified;
    internal int Requests => Volatile.Read(ref _requests);
    internal int NotModified => Volatile.Read(ref _notModified);

    /// <summary>Reads every page of a list endpoint. <paramref name="map"/> drops everything except metadata.</summary>
    internal async Task<List<JsonElement>> ReadAllAsync(string url, Func<JsonElement, JsonElement?> map, int maximumPages,
        CancellationToken token, string apiVersion = DefaultApiVersion) {
        var rows = new List<JsonElement>();
        var expectedPath = new Uri(url).AbsolutePath;
        string? next = url;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; next is not null; page++) {
            if (page > maximumPages) throw new InvalidDataException("Catalog pagination exceeded its bound.");
            if (!seen.Add(next)) throw new InvalidDataException("Catalog pagination repeated a page.");
            var result = await ReadPageAsync(next, expectedPath, map, token, apiVersion);
            if (page > 1 && result.Received == 0 && result.Rows.Length == 0) throw new InvalidDataException("Empty continuation page; completeness is unknown.");
            rows.AddRange(result.Rows);
            next = result.Next;
        }
        return rows;
    }

    /// <summary>Reads one object endpoint (for example a single PR) through the same cache and budget.</summary>
    internal async Task<JsonElement?> ReadOneAsync(string url, Func<JsonElement, JsonElement?> map, CancellationToken token) {
        var page = await ReadPageAsync(url, new Uri(url).AbsolutePath, map, token, DefaultApiVersion, single: true);
        return page.Rows.Length == 0 ? null : page.Rows[0];
    }

    private async Task<Page> ReadPageAsync(string url, string expectedPath, Func<JsonElement, JsonElement?> map,
        CancellationToken token, string apiVersion, bool single = false) {
        cache.TryGetValue(url, out var cached);
        budget.Reserve();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-GitHub-Api-Version", apiVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (cached?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        Interlocked.Increment(ref _requests);
        budget.Observe(response);
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) {
            Interlocked.Increment(ref _notModified);
            budget.Refund(); // Conditional requests answered with 304 do not count against GitHub's rate limit.
            return cached;
        }
        if (single && response.StatusCode == HttpStatusCode.NotFound) return new(null, [], null);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: token);
        var root = document.RootElement;
        if (!single && root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Catalog list response was not an array.");
        var rows = single
            ? (map(root) is { } one ? [one] : [])
            : root.EnumerateArray().Select(map).OfType<JsonElement>().ToArray();
        // Received counts GitHub's rows before mapping drops irrelevant ones, so completeness checks see the real page.
        var page = new Page(response.Headers.ETag?.ToString(), rows, single ? null : NextLink(response, expectedPath),
            single ? 1 : root.GetArrayLength());
        cache[url] = page;
        return page;
    }

    /// <summary>Follows only a continuation on api.github.com for the same resource, so credentials never leave it.</summary>
    internal static string? NextLink(HttpResponseMessage response, string expectedPath) {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;
        foreach (var link in links.SelectMany(value => value.Split(','))) {
            if (!link.Contains("rel=\"next\"", StringComparison.Ordinal)) continue;
            var start = link.IndexOf('<'); var end = link.IndexOf('>');
            if (start < 0 || end <= start || !Uri.TryCreate(link[(start + 1)..end], UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" || uri.Host != "api.github.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
                uri.Fragment.Length != 0 || !SameResource(uri.AbsolutePath, expectedPath))
                throw new InvalidDataException("Invalid catalog continuation link.");
            return uri.AbsoluteUri;
        }
        return null;
    }

    // GitHub may continue /orgs/{name}, /users/{name} or /repos/{owner}/{name} with the numeric
    // /organizations/{id}, /user/{id} or /repositories/{id} form. The resource after the owner must match.
    internal static bool SameResource(string actual, string expected) {
        if (actual == expected) return true;
        var a = actual.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var e = expected.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (a.Length < 2 || e.Length < 2) return false;
        var kind = (e[0], a[0]) switch {
            ("orgs", "organizations") or ("users", "user") => 2,
            ("repos", "repositories") => 3,
            _ => 0
        };
        if (kind == 0 || !long.TryParse(a[1], out _) || e.Length < kind) return false;
        return a.Skip(2).SequenceEqual(e.Skip(kind), StringComparer.Ordinal);
    }
}

/// <summary>
/// One hourly budget for every catalog read, kept across refreshes. Reads stop while GitHub reports fewer remaining
/// requests than the reserve, so other tools using the same token keep their room.
/// </summary>
internal sealed class GitHubRequestBudget(int hourlyBudget, int minimumRemaining, TimeProvider? clock = null) {
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly object _gate = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _used;
    private int? _remaining;
    private DateTimeOffset? _resetAtUtc;
    internal int UsedThisHour { get { lock (_gate) return _used; } }
    internal int? Remaining { get { lock (_gate) return _remaining; } }

    internal void Reserve() {
        lock (_gate) {
            var now = _clock.GetUtcNow();
            if (now - _windowStart >= TimeSpan.FromHours(1)) { _windowStart = now; _used = 0; }
            if (_remaining is { } remaining && remaining <= minimumRemaining && (_resetAtUtc is null || _resetAtUtc > now))
                throw new GitHubBudgetExhaustedException();
            if (_used >= hourlyBudget) throw new GitHubBudgetExhaustedException();
            _used++;
        }
    }

    internal void Refund() { lock (_gate) if (_used > 0) _used--; }

    internal void Observe(HttpResponseMessage response) {
        lock (_gate) {
            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                int.TryParse(remaining.FirstOrDefault(), out var value)) _remaining = value;
            if (response.Headers.TryGetValues("X-RateLimit-Reset", out var reset) &&
                long.TryParse(reset.FirstOrDefault(), out var seconds)) _resetAtUtc = DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
    }

}
