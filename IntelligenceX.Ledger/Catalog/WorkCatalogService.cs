using System.Collections.Concurrent;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Storage;
using IntelligenceX.Telemetry.GitHub;
using V = IntelligenceX.Ledger.Contracts.TaskVerdicts;

namespace IntelligenceX.Ledger.Catalog;

/// <summary>
/// Keeps the work catalog current in the service process, persists it with its page ETags so a restart costs
/// no full re-read, and answers project and task views by joining it with session verdicts and checkouts.
/// </summary>
internal sealed class WorkCatalogService : BackgroundService {
    private sealed record Persisted(WorkCatalogSnapshot Snapshot, Dictionary<string, GitHubCatalogClient.Page> Pages);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan WorktreeWindow = TimeSpan.FromDays(30);
    private readonly WorkCatalogSettings _settings;
    private readonly LedgerStore _store;
    private readonly LedgerSettings _ledger;
    private readonly string _file;
    private readonly TimeProvider _clock;
    private readonly Func<CancellationToken, Task<HttpClient?>> _github;
    private readonly GitHubRequestBudget _budget;
    private WorkCatalogSnapshot? _snapshot;
    private string? _lastError;

    internal WorkCatalogService(WorkCatalogSettings settings, LedgerStore store, LedgerSettings ledger, TimeProvider? clock = null,
        Func<CancellationToken, Task<HttpClient?>>? github = null, GitHubUsageMeter? usage = null) {
        _settings = settings; _store = store; _ledger = ledger; _clock = clock ?? TimeProvider.System;
        _file = Path.Combine(ledger.DataDirectory, "work-catalog.json");
        _github = github ?? (token => CreateGitHubClientAsync(usage, token));
        _budget = new(settings.HourlyRequestBudget, settings.MinimumRemaining, _clock);
        if (File.Exists(_file)) {
            try {
                var persisted = JsonSerializer.Deserialize<Persisted>(File.ReadAllBytes(_file), Json)!;
                _snapshot = persisted.Snapshot;
                foreach (var (url, page) in persisted.Pages) _pages[url] = page;
            } catch (JsonException) { _lastError = "persisted-catalog-unreadable"; }
        }
    }

    private readonly ConcurrentDictionary<string, GitHubCatalogClient.Page> _pages = new(StringComparer.Ordinal);

    internal WorkCatalogSnapshot? Snapshot => Volatile.Read(ref _snapshot);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_settings.RefreshMinutes), _clock);
        do {
            try { await RefreshAsync(stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested && error is HttpRequestException or IOException or
                InvalidOperationException or InvalidDataException or JsonException or OperationCanceledException) {
                Volatile.Write(ref _lastError, "refresh-failed:" + error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One full catalog pass. Unchanged pages answer 304 and keep their earlier rows.</summary>
    internal async Task RefreshAsync(CancellationToken token) {
        using var http = await _github(token);
        if (http is null) { Volatile.Write(ref _lastError, "github-credentials-unavailable"); return; }
        var client = new GitHubCatalogClient(http, _budget, _pages);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromMinutes(8));
        var snapshot = await new WorkCatalogReader(client, _settings, _clock).RefreshAsync(Snapshot ?? new(), deadline.Token);
        Prune(snapshot);
        Volatile.Write(ref _snapshot, snapshot);
        Volatile.Write(ref _lastError, null);
        Save(snapshot);
    }

    // Cached pages for repositories that no longer have open work, or a board page that moved, are never read again.
    private void Prune(WorkCatalogSnapshot snapshot) {
        var live = snapshot.Repositories.Where(repo => !repo.Archived && repo.OpenIssuesAndPullRequests > 0)
            .Select(repo => "/repos/" + repo.Repo + "/").ToArray();
        foreach (var key in _pages.Keys) {
            if (!key.Contains("/repos/", StringComparison.Ordinal)) continue;
            if (!live.Any(prefix => key.Contains(prefix, StringComparison.OrdinalIgnoreCase)) && !key.Contains("/pulls/", StringComparison.Ordinal))
                _pages.TryRemove(key, out _);
        }
    }

    private void Save(WorkCatalogSnapshot snapshot) {
        var temporary = _file + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) {
            JsonSerializer.Serialize(output, new Persisted(snapshot, new(_pages, StringComparer.Ordinal)), Json);
            output.Flush(true);
        }
        File.Move(temporary, _file, true);
    }

    private static async Task<HttpClient?> CreateGitHubClientAsync(GitHubUsageMeter? usage, CancellationToken token) {
        var credentials = await GitHubCredentialResolver.ResolveAsync(token);
        if (!credentials.HasToken) return null;
        HttpMessageHandler handler = new HttpClientHandler { AllowAutoRedirect = false };
        if (usage is not null) handler = new GitHubUsageHandler(usage, "ledger-catalog", handler);
        var client = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntelligenceX-Ledger/1");
        client.DefaultRequestHeaders.Authorization = new("Bearer", credentials.Token);
        return client;
    }

    internal async Task<WorkReconcileView> ReconcileAsync(Func<DateTimeOffset, CancellationToken, Task<LedgerItem[]>> items, string? verdict,
        string? repo, int limit, CancellationToken token) {
        if (verdict is not null && !V.All.Contains(verdict)) throw new ArgumentException("Unknown task verdict.");
        var now = _clock.GetUtcNow();
        var (rows, coverage, catalogAt) = await RowsAsync(items, now, token, Snapshot);
        if (repo is not null) rows = rows.Where(row => string.Equals(row.Repo, repo, StringComparison.OrdinalIgnoreCase)).ToArray();
        var counts = rows.GroupBy(row => row.Verdict).ToDictionary(group => group.Key, group => group.Count());
        var selected = rows.Where(row => verdict is null ? row.Verdict != V.Archived : row.Verdict == verdict).ToArray();
        return new(now, catalogAt, counts, selected.Take(limit).ToArray(),
            verdict is null ? coverage.Append("archived-tasks:counted-not-listed").ToArray() : coverage, selected.Length > limit);
    }

    internal async Task<ProjectStatusView> ProjectsAsync(Func<DateTimeOffset, CancellationToken, Task<LedgerItem[]>> items, string? repo,
        int limit, CancellationToken token) {
        var now = _clock.GetUtcNow();
        var catalog = Snapshot ?? new();
        var (rows, coverage, catalogAt) = await RowsAsync(items, now, token, catalog);
        var worktrees = (await _store.CatalogWorktreesAsync(now, now - WorktreeWindow, token))
            .GroupBy(row => row.Repo, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var tasks = rows.Where(row => row.Repo is not null && row.Verdict != V.Archived)
            .GroupBy(row => row.Repo!, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var names = catalog.Repositories.Select(r => r.Repo).Concat(worktrees.Keys).Concat(tasks.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(name => repo is null || name.Equals(repo, StringComparison.OrdinalIgnoreCase));
        var projects = names.Select(name => {
            var info = catalog.Repositories.FirstOrDefault(r => r.Repo.Equals(name, StringComparison.OrdinalIgnoreCase));
            var issues = catalog.Issues.Where(issue => issue.Repo.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var pulls = catalog.PullRequests.Where(pull => pull.Repo.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            var checkouts = Checkouts(worktrees.GetValueOrDefault(name, []));
            var taskRows = tasks.GetValueOrDefault(name, []);
            return new ProjectStatusRow(info?.Repo ?? name, issues.Count(issue => !issue.PullRequest), pulls.Length, pulls.Count(pull => pull.Draft),
                checkouts.Length, checkouts.Count(worktree => worktree.Live),
                taskRows.GroupBy(row => row.Verdict).ToDictionary(group => group.Key, group => group.Count()), info?.PushedAtUtc) {
                Archived = info?.Archived == true,
                WorktreeRows = repo is null ? null : checkouts,
                PullRequestRows = repo is null ? null : pulls.Select(pull => new CatalogPullRequest(pull.Number, pull.Title, pull.HeadBranch, pull.Draft, pull.UpdatedAtUtc)).ToArray()
            };
        }).Where(project => repo is not null || project.OpenIssues + project.OpenPullRequests + project.Worktrees + project.Tasks.Values.Sum() > 0)
          .OrderByDescending(project => Attention(project.Tasks)).ThenByDescending(project => project.OpenPullRequests)
          .ThenByDescending(project => project.LiveWorktrees).ThenBy(project => project.Repo, StringComparer.OrdinalIgnoreCase).ToArray();
        var quiet = catalog.Repositories.Length - projects.Length;
        return new(now, catalogAt, catalog.Repositories.Length,
            rows.GroupBy(row => row.Verdict).ToDictionary(group => group.Key, group => group.Count()), projects.Take(limit).ToArray(),
            repo is null && quiet > 0 ? coverage.Append("repositories-without-open-work:" + quiet).ToArray() : coverage, projects.Length > limit);
    }

    private async Task<(TaskVerdictRow[] Rows, string[] Coverage, DateTimeOffset? CatalogAt)> RowsAsync(
        Func<DateTimeOffset, CancellationToken, Task<LedgerItem[]>> items, DateTimeOffset now, CancellationToken token,
        WorkCatalogSnapshot? catalog) {
        if (catalog is { CapturedAtUtc.Ticks: 0 }) catalog = null; // The empty placeholder used before the first read.
        var coverage = new List<string> { "worktrees:session-observed", "github:metadata-only" };
        if (Volatile.Read(ref _lastError) is { } error) coverage.Add(error);
        if (catalog is null) { coverage.Add("catalog-not-read-yet"); return ([], coverage.ToArray(), null); }
        coverage.AddRange(catalog.Coverage);
        if (now - catalog.CapturedAtUtc > TimeSpan.FromMinutes(_settings.RefreshMinutes * 3)) coverage.Add("catalog-stale");
        if (_settings.Board is not null && catalog.BoardReadAtUtc is null) coverage.Add("board-not-read");
        var worktrees = (await _store.CatalogWorktreesAsync(now, now - WorktreeWindow, token))
            .Select(row => new TaskReconciler.Worktree(row.Repo, row.Session)).ToArray();
        var reconciler = new TaskReconciler(_settings, _ledger.ExpectedReporters.Keys.ToArray());
        return (reconciler.Reconcile(catalog, await items(now, token), worktrees, now), coverage.ToArray(), catalog.CapturedAtUtc);
    }

    internal static CatalogWorktree[] Checkouts(IEnumerable<(string Repo, RepositorySession Session)> rows) => rows
        .GroupBy(row => (row.Session.Machine, Path: row.Session.Worktree ?? row.Session.AgentId))
        .Select(group => new CatalogWorktree(group.Key.Machine, group.First().Session.Worktree,
            group.OrderByDescending(row => row.Session.LastActivityAtUtc).First().Session.Branch,
            group.Any(row => row.Session.Live), group.Max(row => row.Session.LastActivityAtUtc),
            group.SelectMany(row => row.Session.PullRequests).Distinct(StringComparer.Ordinal).Take(5).ToArray(), group.Count()))
        .OrderByDescending(worktree => worktree.Live).ThenByDescending(worktree => worktree.LastActivityAtUtc).ToArray();

    private static int Attention(Dictionary<string, int> tasks) =>
        tasks.Where(pair => pair.Key is V.WaitingOnYou or V.NeedsReplacement or V.DoneClosable or V.Stale).Sum(pair => pair.Value);
}
