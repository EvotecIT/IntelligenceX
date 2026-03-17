using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace IntelligenceX.Tray.Services;

public sealed class GitHubService {
    private string? _cachedLogin;

    public async Task<GitHubDashboardData?> FetchAsync(CancellationToken ct = default) {
        var login = await GetLoginAsync(ct);
        if (login == null) return null;

        var contribTask = FetchContributionsAsync(login, ct);
        var reposTask = FetchTopReposAsync(login, ct);

        await Task.WhenAll(contribTask, reposTask);

        return new GitHubDashboardData {
            Login = login,
            Contributions = contribTask.Result,
            TopRepos = reposTask.Result
        };
    }

    private async Task<string?> GetLoginAsync(CancellationToken ct) {
        if (_cachedLogin != null) return _cachedLogin;
        var result = await RunGhAsync(["api", "user", "--jq", ".login"], ct);
        if (result == null) return null;
        _cachedLogin = result.Trim();
        return _cachedLogin;
    }

    private async Task<GitHubContribData> FetchContributionsAsync(string login, CancellationToken ct) {
        var data = new GitHubContribData();
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-30).ToString("yyyy-MM-ddT00:00:00Z");
        var to = now.ToString("yyyy-MM-ddT23:59:59Z");

        var query = @"query($login:String!,$from:DateTime!,$to:DateTime!){user(login:$login){contributionsCollection(from:$from,to:$to){totalCommitContributions totalIssueContributions totalPullRequestContributions totalPullRequestReviewContributions contributionCalendar{totalContributions weeks{contributionDays{date contributionCount color}}}}}}";

        var json = await RunGhAsync([
            "api", "graphql",
            "-f", $"query={query}",
            "-F", $"login={login}",
            "-F", $"from={from}",
            "-F", $"to={to}"
        ], ct);

        if (json == null) return data;

        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var d) || !d.TryGetProperty("user", out var user))
                return data;
            if (!user.TryGetProperty("contributionsCollection", out var coll))
                return data;

            data.TotalCommits = ReadInt(coll, "totalCommitContributions");
            data.TotalIssues = ReadInt(coll, "totalIssueContributions");
            data.TotalPRs = ReadInt(coll, "totalPullRequestContributions");
            data.TotalReviews = ReadInt(coll, "totalPullRequestReviewContributions");

            if (coll.TryGetProperty("contributionCalendar", out var cal)) {
                data.TotalContributions = ReadInt(cal, "totalContributions");
                if (cal.TryGetProperty("weeks", out var weeks)) {
                    foreach (var week in weeks.EnumerateArray()) {
                        if (!week.TryGetProperty("contributionDays", out var days)) continue;
                        foreach (var day in days.EnumerateArray()) {
                            var dateStr = day.TryGetProperty("date", out var dv) ? dv.GetString() : null;
                            var count = ReadInt(day, "contributionCount");
                            var color = day.TryGetProperty("color", out var cv) ? cv.GetString() : null;
                            if (dateStr != null && DateTime.TryParseExact(dateStr, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) {
                                data.DailyContributions.Add(new GitHubDailyContrib {
                                    Date = parsed,
                                    Count = count,
                                    Color = color
                                });
                            }
                        }
                    }
                }
            }
        } catch {
            // Parsing failed
        }

        return data;
    }

    private async Task<List<GitHubRepoInfo>> FetchTopReposAsync(string login, CancellationToken ct) {
        // Fetch user's orgs first, then get repos from user + orgs
        var owners = new List<string> { login };
        owners.AddRange(await FetchUserOrgsAsync(ct));

        var allRepos = new List<GitHubRepoInfo>();
        foreach (var owner in owners.Distinct(StringComparer.OrdinalIgnoreCase)) {
            var repos = await FetchOwnerReposAsync(owner, ct);
            allRepos.AddRange(repos);
        }

        return allRepos
            .OrderByDescending(r => r.Stars)
            .ThenByDescending(r => r.Forks)
            .Take(8)
            .ToList();
    }

    private async Task<List<string>> FetchUserOrgsAsync(CancellationToken ct) {
        var orgs = new List<string>();
        var json = await RunGhAsync(["api", "user/orgs", "--jq", ".[].login"], ct);
        if (json == null) return orgs;
        foreach (var line in json.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (!string.IsNullOrWhiteSpace(line)) orgs.Add(line);
        }
        return orgs;
    }

    private async Task<List<GitHubRepoInfo>> FetchOwnerReposAsync(string owner, CancellationToken ct) {
        var repos = new List<GitHubRepoInfo>();
        var query = @"query($login:String!){repositoryOwner(login:$login){repositories(first:10,orderBy:{field:STARGAZERS,direction:DESC},privacy:PUBLIC){nodes{nameWithOwner stargazerCount forkCount description primaryLanguage{name color}}}}}";

        var json = await RunGhAsync([
            "api", "graphql",
            "-f", $"query={query}",
            "-F", $"login={owner}"
        ], ct);

        if (json == null) return repos;

        try {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("data", out var d) ||
                !d.TryGetProperty("repositoryOwner", out var ownerEl) ||
                ownerEl.ValueKind == JsonValueKind.Null ||
                !ownerEl.TryGetProperty("repositories", out var repoConn) ||
                !repoConn.TryGetProperty("nodes", out var nodes))
                return repos;

            foreach (var node in nodes.EnumerateArray()) {
                var name = node.TryGetProperty("nameWithOwner", out var nv) ? nv.GetString() : null;
                if (name == null) continue;

                string? lang = null, langColor = null;
                if (node.TryGetProperty("primaryLanguage", out var pl) && pl.ValueKind == JsonValueKind.Object) {
                    lang = pl.TryGetProperty("name", out var ln) ? ln.GetString() : null;
                    langColor = pl.TryGetProperty("color", out var lc) ? lc.GetString() : null;
                }

                repos.Add(new GitHubRepoInfo {
                    NameWithOwner = name,
                    Stars = ReadInt(node, "stargazerCount"),
                    Forks = ReadInt(node, "forkCount"),
                    Description = node.TryGetProperty("description", out var desc) ? desc.GetString() : null,
                    Language = lang,
                    LanguageColor = langColor
                });
            }
        } catch {
            // Parsing failed
        }

        return repos;
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
            if (proc == null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0 ? output : null;
        } catch {
            return null;
        }
    }

    private static int ReadInt(JsonElement el, string prop) {
        return el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetInt32() : 0;
    }
}

public sealed class GitHubDashboardData {
    public string Login { get; set; } = "";
    public GitHubContribData Contributions { get; set; } = new();
    public List<GitHubRepoInfo> TopRepos { get; set; } = [];
}

public sealed class GitHubContribData {
    public int TotalContributions { get; set; }
    public int TotalCommits { get; set; }
    public int TotalIssues { get; set; }
    public int TotalPRs { get; set; }
    public int TotalReviews { get; set; }
    public List<GitHubDailyContrib> DailyContributions { get; set; } = [];
}

public sealed class GitHubDailyContrib {
    public DateTime Date { get; set; }
    public int Count { get; set; }
    public string? Color { get; set; }
}

public sealed class GitHubRepoInfo {
    public string NameWithOwner { get; set; } = "";
    public int Stars { get; set; }
    public int Forks { get; set; }
    public string? Description { get; set; }
    public string? Language { get; set; }
    public string? LanguageColor { get; set; }
}
