using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Configured field identities keep project and machine conventions out of source.</summary>
public sealed record ProjectClaimsSettings {
    public required string Organization { get; init; }
    public required int ProjectNumber { get; init; }
    public required long AgentFieldId { get; init; }
    public required long MachineFieldId { get; init; }
    public required long StatusFieldId { get; init; }
    public required long PullRequestFieldId { get; init; }
    public Dictionary<string, string> MachineAliases { get; init; } = [];
    public string[] InactiveStatuses { get; init; } = ["Done", "Planned"];
}

internal sealed record ClaimSnapshot(HashSet<string> ActiveItems, HashSet<string> ItemsWithPullRequests, DateTimeOffset CapturedAtUtc,
    Dictionary<string, string[]> PullRequestUrlsByItem, HashSet<string> IncompletePullRequestItems);

/// <summary>
/// Read-only, paginated claim evidence from a configured private GitHub project. Each page keeps its ETag and the
/// claim fields read from it, so an unchanged page answers 304 and costs no rate limit.
/// </summary>
internal sealed class ProjectClaimReader(HttpClient client, ProjectClaimsSettings settings) {
    private sealed record Row(string NodeId, bool Archived, string Agent, string Machine, string Status, string PullRequest);
    private sealed record Page(string? ETag, Row[] Rows, string? Next);
    private Dictionary<string, Page> _pages = new(StringComparer.Ordinal);
    internal int Requests { get; private set; }
    internal int NotModified { get; private set; }

    internal async Task<ClaimSnapshot> ReadAsync(CancellationToken token) {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var linked = new HashSet<string>(StringComparer.Ordinal);
        var urls = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var incomplete = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var visited = new Dictionary<string, Page>(StringComparer.Ordinal);
        var hadRows = false;
        var fields = new[] { settings.AgentFieldId, settings.MachineFieldId, settings.StatusFieldId, settings.PullRequestFieldId };
        var basePath = $"https://api.github.com/orgs/{Uri.EscapeDataString(settings.Organization)}/projectsV2/{settings.ProjectNumber}";
        string? next = $"{basePath}/items?per_page=100&fields={string.Join(',', fields)}";
        for (var page = 1; next is not null; page++) {
            if (page > 100) throw new InvalidDataException("Project claim pagination exceeded its bound.");
            if (visited.ContainsKey(next)) throw new InvalidDataException("Project claim pagination repeated a page.");
            var result = await ReadPageAsync(next, token);
            visited[next] = result;
            if (hadRows && result.Rows.Length == 0) throw new InvalidDataException("Empty continuation page; claim completeness is unknown.");
            hadRows |= result.Rows.Length > 0;
            foreach (var row in result.Rows) {
                if (!seen.Add(row.NodeId)) throw new InvalidDataException("Duplicate project item.");
                if (row.Archived) continue;
                var actor = row.Agent.Split(':');
                if (actor.Length < 2 || actor[0] is not ("codex" or "claude") || !Guid.TryParse(actor[1], out var sessionId)) continue;
                if (!settings.MachineAliases.TryGetValue(row.Machine, out var machine)) continue;
                var id = LocalReporter.ItemId(machine, actor[0], sessionId.ToString("D"));
                if (!settings.InactiveStatuses.Contains(row.Status)) active.Add(id);
                var link = row.PullRequest.Trim();
                if (link.Length > 0) {
                    linked.Add(id);
                    urls.TryAdd(id, []);
                    if (PullRequestCatalog.TryParse(link, out _, out _)) urls[id] = urls[id].Append(link).Distinct(StringComparer.Ordinal).ToArray();
                    else incomplete.Add(id);
                }
            }
            next = result.Next;
        }
        _pages = visited; // Only pages of the latest complete read stay cached, so retired cursors do not accumulate.
        return new(active, linked, DateTimeOffset.UtcNow, urls, incomplete);
    }

    private async Task<Page> ReadPageAsync(string url, CancellationToken token) {
        _pages.TryGetValue(url, out var cached);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (cached?.ETag is { } etag) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        using var response = await client.SendAsync(request, token);
        Requests++;
        if (response.StatusCode == HttpStatusCode.NotModified && cached is not null) { NotModified++; return cached; }
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var document = await JsonDocument.ParseAsync(body, cancellationToken: token);
        var root = document.RootElement;
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
        return new(response.Headers.ETag?.ToString(), rows.EnumerateArray().Select(ReadRow).ToArray(), NextLink(response));
    }

    private Row ReadRow(JsonElement row) {
        var nodeId = row.GetProperty("node_id").GetString()!;
        var archived = row.TryGetProperty("archived_at", out var archivedAt) && archivedAt.ValueKind != JsonValueKind.Null;
        if (archived) return new(nodeId, true, "", "", "", "");
        var values = row.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetInt64());
        string Value(long id) {
            if (!values.TryGetValue(id, out var field)) throw new InvalidDataException("Project claim fields are incomplete.");
            if (!field.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null) return "";
            return field.GetProperty("data_type").GetString() == "single_select"
                ? value.GetProperty("name").GetProperty("raw").GetString() ?? ""
                : value.GetProperty("raw").GetString() ?? "";
        }
        return new(nodeId, false, Value(settings.AgentFieldId), Value(settings.MachineFieldId), Value(settings.StatusFieldId), Value(settings.PullRequestFieldId));
    }

    private string? NextLink(HttpResponseMessage response) {
        if (!response.Headers.TryGetValues("Link", out var links)) return null;
        string? next = null;
        foreach (var link in links.SelectMany(value => value.Split(','))) {
            if (!link.Contains("rel=\"next\"", StringComparison.Ordinal)) continue;
            var start = link.IndexOf('<'); var end = link.IndexOf('>');
            if (start < 0 || end <= start || !Uri.TryCreate(link[(start + 1)..end], UriKind.Absolute, out var uri) ||
                uri.Scheme != "https" || uri.Host != "api.github.com" || !uri.IsDefaultPort ||
                uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || !uri.AbsolutePath.EndsWith($"/projectsV2/{settings.ProjectNumber}/items", StringComparison.Ordinal) ||
                !(uri.AbsolutePath.StartsWith("/orgs/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/organizations/", StringComparison.Ordinal)))
                throw new InvalidDataException("Invalid project continuation link.");
            next = uri.AbsoluteUri;
        }
        return next;
    }
}
