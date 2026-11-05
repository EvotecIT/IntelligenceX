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

/// <summary>Read-only, paginated claim evidence from a configured private GitHub project.</summary>
internal sealed class ProjectClaimReader(HttpClient client, ProjectClaimsSettings settings) {
    internal async Task<ClaimSnapshot> ReadAsync(CancellationToken token) {
        var active = new HashSet<string>(StringComparer.Ordinal);
        var linked = new HashSet<string>(StringComparer.Ordinal);
        var urls = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var incomplete = new HashSet<string>(StringComparer.Ordinal);
        var fields = new[] { settings.AgentFieldId, settings.MachineFieldId, settings.StatusFieldId, settings.PullRequestFieldId };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hadRows = false;
        var basePath = $"https://api.github.com/orgs/{Uri.EscapeDataString(settings.Organization)}/projectsV2/{settings.ProjectNumber}";
        string? next = $"{basePath}/items?per_page=100&fields={string.Join(',', fields)}";
        for (var page = 1; ; page++) {
            if (page > 100) throw new InvalidDataException("Project claim pagination exceeded its bound.");
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            using var response = await client.SendAsync(request, token);
            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: token);
            var root = document.RootElement;
            var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
            if (hadRows && rows.GetArrayLength() == 0) throw new InvalidDataException("Empty continuation page; claim completeness is unknown.");
            hadRows |= rows.GetArrayLength() > 0;
            foreach (var row in rows.EnumerateArray()) {
                if (!seen.Add(row.GetProperty("node_id").GetString()!)) throw new InvalidDataException("Duplicate project item.");
                if (row.TryGetProperty("archived_at", out var archived) && archived.ValueKind != JsonValueKind.Null) continue;
                var values = row.GetProperty("fields").EnumerateArray().ToDictionary(f => f.GetProperty("id").GetInt64());
                if (fields.Any(id => !values.ContainsKey(id))) throw new InvalidDataException("Project claim fields are incomplete.");
                string Value(long id) {
                    var field = values[id];
                    if (!field.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Null) return "";
                    return field.GetProperty("data_type").GetString() == "single_select"
                        ? value.GetProperty("name").GetProperty("raw").GetString() ?? ""
                        : value.GetProperty("raw").GetString() ?? "";
                }
                var actor = Value(settings.AgentFieldId).Split(':');
                if (actor.Length < 2 || actor[0] is not ("codex" or "claude") || !Guid.TryParse(actor[1], out var sessionId)) continue;
                var machineName = Value(settings.MachineFieldId);
                if (!settings.MachineAliases.TryGetValue(machineName, out var machine)) continue;
                var id = LocalReporter.ItemId(machine, actor[0], sessionId.ToString("D"));
                if (!settings.InactiveStatuses.Contains(Value(settings.StatusFieldId))) active.Add(id);
                var link = Value(settings.PullRequestFieldId).Trim();
                if (link.Length > 0) {
                    linked.Add(id);
                    urls.TryAdd(id, []);
                    if (PullRequestCatalog.TryParse(link, out _, out _)) urls[id] = urls[id].Append(link).Distinct(StringComparer.Ordinal).ToArray();
                    else incomplete.Add(id);
                }
            }
            next = null;
            if (response.Headers.TryGetValues("Link", out var links)) {
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
            }
            if (next is null) break;
        }
        return new(active, linked, DateTimeOffset.UtcNow, urls, incomplete);
    }
}
