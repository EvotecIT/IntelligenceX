using System.Text.Json;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Ledger.Catalog;

/// <summary>
/// Builds the work catalog from GitHub metadata: repositories, open issues and PRs, board items, addressed-issue
/// cross-references and the state of PRs that tasks link to. Bodies and comments are never kept. A failed or
/// budget-limited read keeps that part of the previous snapshot and names the gap in coverage.
/// </summary>
internal sealed class WorkCatalogReader(GitHubCatalogClient github, WorkCatalogSettings settings, TimeProvider? clock = null) {
    private const string Api = "https://api.github.com/";
    private const string ProjectsApiVersion = "2026-03-10";
    private const int MaximumTaskIssues = 300;
    private const int MaximumPullStateReads = 400;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    internal async Task<WorkCatalogSnapshot> RefreshAsync(WorkCatalogSnapshot previous, CancellationToken token) {
        var coverage = new List<string>();
        var budgetExhausted = false;
        void Gap(string gap) { lock (coverage) coverage.Add(gap); }
        async Task<T?> Guard<T>(string gap, Func<Task<T?>> read) where T : class {
            if (Volatile.Read(ref budgetExhausted)) { Gap(gap); return null; }
            try { return await read(); }
            catch (GitHubBudgetExhaustedException) { Volatile.Write(ref budgetExhausted, true); Gap(gap); return null; }
            catch (Exception error) when (!token.IsCancellationRequested && error is HttpRequestException or InvalidDataException or
                JsonException or KeyNotFoundException or InvalidOperationException or OperationCanceledException or FormatException) {
                Gap(gap + ":" + Code(error)); return null;
            }
        }

        // Repositories, per owner. A failed owner keeps its previous repositories.
        var repositories = new List<CatalogRepository>();
        // /users/{name}/repos lists only public repositories; /user/repos adds the token owner's private ones.
        foreach (var (owner, paths) in settings.Organizations.Select(o => (o, new[] { "orgs/" + o + "/repos?type=all&per_page=100" }))
                     .Concat(settings.Users.Select(u => (u, new[] { "users/" + u + "/repos?type=owner&per_page=100",
                         "user/repos?affiliation=owner&visibility=all&per_page=100" })))) {
            var rows = await Guard("repositories-incomplete:" + owner, async () => {
                var all = new List<CatalogRepository>();
                foreach (var path in paths)
                    all.AddRange((await github.ReadAllAsync(Api + path, MapRepository, 30, token)).Select(row => row.Deserialize<CatalogRepository>(Json)!));
                return all.Where(repo => repo.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)).ToList();
            });
            repositories.AddRange(rows ?? previous.Repositories.Where(repo => repo.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase)));
        }
        repositories = repositories.DistinctBy(repo => repo.Repo, StringComparer.OrdinalIgnoreCase).OrderBy(repo => repo.Repo, StringComparer.OrdinalIgnoreCase).ToList();

        // Open issues and PRs, only for repositories GitHub says have any.
        var issues = new List<CatalogIssue>();
        var pulls = new List<CatalogPull>();
        using (var limit = new SemaphoreSlim(4)) {
            var repositoryReads = repositories.Where(repo => !repo.Archived && repo.OpenIssuesAndPullRequests > 0).Select(async repo => {
                await limit.WaitAsync(token);
                try {
                    var openIssues = await Guard("issues-incomplete:" + repo.Repo, async () =>
                        (await github.ReadAllAsync(Api + "repos/" + repo.Repo + "/issues?state=open&per_page=100", row => MapIssue(repo.Repo, row), 30, token))
                            .Select(row => row.Deserialize<CatalogIssue>(Json)!).ToList());
                    openIssues ??= previous.Issues.Where(issue => issue.Repo.Equals(repo.Repo, StringComparison.OrdinalIgnoreCase)).ToList();
                    List<CatalogPull>? openPulls = [];
                    if (openIssues.Any(issue => issue.PullRequest)) {
                        openPulls = await Guard("pull-requests-incomplete:" + repo.Repo, async () =>
                            (await github.ReadAllAsync(Api + "repos/" + repo.Repo + "/pulls?state=open&per_page=100", row => MapPull(repo.Repo, row), 30, token))
                                .Select(row => row.Deserialize<CatalogPull>(Json)!).ToList());
                        openPulls ??= previous.PullRequests.Where(pull => pull.Repo.Equals(repo.Repo, StringComparison.OrdinalIgnoreCase))
                            .Select(pull => pull with { Retained = true }).ToList();
                    }
                    return (openIssues, openPulls);
                } finally { limit.Release(); }
            }).ToArray();
            foreach (var (openIssues, openPulls) in await Task.WhenAll(repositoryReads)) {
                issues.AddRange(openIssues.DistinctBy(issue => issue.Number));
                pulls.AddRange(openPulls.DistinctBy(pull => pull.Number));
            }
        }

        // Board items, all pages. A failed read keeps the previous board with its read time.
        var board = settings.Board is null ? [] : previous.Board;
        var boardReadAt = settings.Board is null ? null : previous.BoardReadAtUtc;
        if (settings.Board is { } boardSettings) {
            var path = "orgs/" + boardSettings.Organization + "/projectsV2/" + boardSettings.ProjectNumber + "/items?per_page=100&fields=" +
                string.Join("%2C", boardSettings.FieldIds());
            var rows = await Guard("board-incomplete", async () =>
                (await github.ReadAllAsync(Api + path, row => MapBoardItem(boardSettings, row), 60, token, ProjectsApiVersion))
                    .Select(row => row.Deserialize<CatalogBoardItem>(Json)!).ToArray());
            if (rows is not null) {
                if (rows.Select(row => row.Id).Distinct().Count() != rows.Length) Gap("board-incomplete:duplicate-items");
                else { board = rows; boardReadAt = _clock.GetUtcNow(); }
            }
        }

        // Addressed issues: which PRs referenced them. Metadata of the referencing PR only.
        var references = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var unconfirmed = new List<string>();
        var taskIssues = issues.Where(issue => IsTaskIssue(settings, issue)).OrderByDescending(issue => issue.UpdatedAtUtc).ToArray();
        if (taskIssues.Length > MaximumTaskIssues) coverage.Add("task-issues-capped:" + MaximumTaskIssues);
        foreach (var issue in taskIssues.Skip(MaximumTaskIssues)) {
            unconfirmed.Add(issue.Key);
            if (previous.IssueReferences.TryGetValue(issue.Key, out var retained)) references[issue.Key] = retained;
        }
        foreach (var issue in taskIssues.Take(MaximumTaskIssues)) {
            var urls = await Guard("issue-references-incomplete:" + issue.Key, async () =>
                (await github.ReadAllAsync(Api + "repos/" + issue.Repo + "/issues/" + issue.Number + "/timeline?per_page=100", MapReference, 30, token))
                    .Select(row => row.GetString()!).Distinct(StringComparer.Ordinal).ToArray());
            if (urls is null) {
                unconfirmed.Add(issue.Key);
                if (previous.IssueReferences.TryGetValue(issue.Key, out var kept)) urls = kept;
            }
            if (urls is { Length: > 0 }) references[issue.Key] = urls;
        }

        // State of every PR a task links to that is not in the open list. Merged is terminal.
        var open = pulls.Where(pull => !pull.Retained).Select(pull => pull.Url).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var done = settings.Board?.DoneStatuses ?? [];
        var linked = board.Where(item => !item.Archived && !(item.Status is { } status && done.Contains(status, StringComparer.OrdinalIgnoreCase)))
            .SelectMany(item => PullRequestLinks(item.PullRequestLinks)
                .Concat(item.ContentType == "PullRequest" && item.ContentUrl is { } url && PullRequestCatalog.TryParse(url, out _, out _) ? [url] : []))
            .Concat(references.Values.SelectMany(urls => urls)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var states = new Dictionary<string, CatalogPullState>(StringComparer.OrdinalIgnoreCase);
        var reads = 0;
        foreach (var url in linked) {
            if (open.Contains(url)) continue; // The open list already describes it.
            if (previous.PullStates.TryGetValue(url, out var known) && known.State == "merged") { states[url] = known; continue; }
            if (++reads > MaximumPullStateReads) { coverage.Add("pull-request-states-capped"); if (known is not null) states[url] = known with { Retained = true }; continue; }
            if (!PullRequestCatalog.TryParse(url, out var repo, out var number)) continue;
            var gap = "pull-request-state-unavailable:" + repo + "#" + number;
            var row = await Guard(gap, async () => {
                if (await github.ReadOneAsync(Api + "repos/" + repo + "/pulls/" + number, MapPullState, token) is not { } element) {
                    Gap(gap + ":not-found"); return null;
                }
                // A 304 returns the cached row; the check time is when this read confirmed it.
                return element.Deserialize<CatalogPullState>(Json)! with { CheckedAtUtc = _clock.GetUtcNow() };
            });
            if (row is not null) states[url] = row;
            else if (known is not null) states[url] = known with { Retained = true }; // Not confirmed by this pass.
        }

        if (budgetExhausted) coverage.Insert(0, "github-budget-exhausted");
        return new() {
            CapturedAtUtc = _clock.GetUtcNow(), Repositories = repositories.ToArray(),
            Issues = issues.OrderBy(issue => issue.Repo, StringComparer.OrdinalIgnoreCase).ThenBy(issue => issue.Number).ToArray(),
            PullRequests = pulls.OrderBy(pull => pull.Repo, StringComparer.OrdinalIgnoreCase).ThenBy(pull => pull.Number).ToArray(),
            Board = board, BoardReadAtUtc = boardReadAt, IssueReferences = references, UnconfirmedReferences = unconfirmed.ToArray(), PullStates = states,
            Coverage = coverage.Distinct(StringComparer.Ordinal).ToArray(), Requests = github.Requests, NotModified = github.NotModified
        };
    }

    internal static bool IsTaskIssue(WorkCatalogSettings settings, CatalogIssue issue) => !issue.PullRequest &&
        issue.Labels.Any(label => label.StartsWith(settings.TaskLabelPrefix, StringComparison.OrdinalIgnoreCase)) &&
        (settings.TaskRepositories.Length == 0 || settings.TaskRepositories.Contains(issue.Repo, StringComparer.OrdinalIgnoreCase));

    /// <summary>Board "PR link" text may hold several links; only canonical GitHub PR URLs count.</summary>
    internal static IEnumerable<string> PullRequestLinks(string? text) => (text ?? "")
        .Split([' ', ',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Trim().TrimEnd('/', '.', ')')).Where(part => PullRequestCatalog.TryParse(part, out _, out _));

    private static string Code(Exception error) => error switch {
        HttpRequestException { StatusCode: { } status } => "http-" + (int)status,
        HttpRequestException => "http",
        OperationCanceledException => "timeout",
        InvalidDataException => "incomplete",
        _ => "schema"
    };

    private static JsonElement Compact<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    private static DateTimeOffset? Time(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetDateTimeOffset() : null;
    private static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static JsonElement? MapRepository(JsonElement row) {
        var name = row.GetProperty("full_name").GetString()!;
        return Compact(new CatalogRepository(name, row.GetProperty("owner").GetProperty("login").GetString()!,
            row.GetProperty("archived").GetBoolean(), row.GetProperty("fork").GetBoolean(),
            row.GetProperty("open_issues_count").GetInt32(), Time(row, "pushed_at")));
    }

    internal static JsonElement? MapIssue(string repo, JsonElement row) => Compact(new CatalogIssue(repo,
        row.GetProperty("number").GetInt32(), Bound(row.GetProperty("title").GetString()),
        row.GetProperty("labels").EnumerateArray().Select(label => label.GetProperty("name").GetString()!).ToArray(),
        Time(row, "created_at")!.Value, Time(row, "updated_at")!.Value, row.TryGetProperty("pull_request", out _),
        row.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True));

    internal static JsonElement? MapPull(string repo, JsonElement row) => Compact(new CatalogPull(repo,
        row.GetProperty("number").GetInt32(), Bound(row.GetProperty("title").GetString()),
        row.TryGetProperty("head", out var head) ? Text(head, "ref") : null,
        row.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True, Time(row, "updated_at")!.Value) { HeadRepo = HeadRepo(row) });

    internal static JsonElement? MapPullState(JsonElement row) => Compact(new CatalogPullState(
        Time(row, "merged_at") is not null ? "merged" : Text(row, "state") ?? "unknown", Time(row, "updated_at"), DateTimeOffset.UtcNow) {
        Title = Bound(Text(row, "title")), Draft = row.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True,
        HeadBranch = row.TryGetProperty("head", out var head) ? Text(head, "ref") : null, HeadRepo = HeadRepo(row)
    });

    private static string? HeadRepo(JsonElement row) =>
        row.TryGetProperty("head", out var head) && head.TryGetProperty("repo", out var repo) && repo.ValueKind == JsonValueKind.Object
            ? Text(repo, "full_name") : null;

    /// <summary>Only cross-references from PRs count; the referencing PR's body is dropped here.</summary>
    internal static JsonElement? MapReference(JsonElement row) =>
        Text(row, "event") == "cross-referenced" && row.TryGetProperty("source", out var source) &&
        source.TryGetProperty("issue", out var issue) && issue.TryGetProperty("pull_request", out _) &&
        Text(issue, "html_url") is { } url && PullRequestCatalog.TryParse(url, out _, out _)
            ? Compact(url) : null;

    internal static JsonElement? MapBoardItem(WorkBoardSettings board, JsonElement row) {
        var values = row.TryGetProperty("fields", out var fields)
            ? fields.EnumerateArray().Where(field => field.TryGetProperty("id", out _)).ToDictionary(field => field.GetProperty("id").GetInt64())
            : [];
        // A requested field missing from the response is not the same as an empty one: the read is incomplete.
        if (board.FieldIds().Any(id => !values.ContainsKey(id))) throw new InvalidDataException("Board fields are incomplete.");
        string? Value(long? id, int bound = 300) {
            if (id is null || !values.TryGetValue(id.Value, out var field) || !field.TryGetProperty("value", out var value) ||
                value.ValueKind == JsonValueKind.Null) return null;
            var raw = Text(field, "data_type") switch {
                "single_select" => value.GetProperty("name").GetProperty("raw").GetString(),
                "repository" => Text(value, "full_name"),
                _ => value.ValueKind == JsonValueKind.Object ? Text(value, "raw") : value.ValueKind == JsonValueKind.String ? value.GetString() : null
            };
            return string.IsNullOrWhiteSpace(raw) ? null : Bound(raw.Trim(), bound);
        }
        var content = row.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        var hasContent = content.ValueKind == JsonValueKind.Object;
        var state = hasContent ? Time(content, "merged_at") is not null ? "merged" : Text(content, "state") : null;
        return Compact(new CatalogBoardItem(row.GetProperty("id").GetInt64(), Text(row, "content_type") ?? "unknown",
            Bound(hasContent ? Text(content, "title") : null), hasContent ? Text(content, "html_url") : null, state,
            Time(row, "updated_at") ?? DateTimeOffset.MinValue,
            row.TryGetProperty("archived_at", out var archived) && archived.ValueKind != JsonValueKind.Null) {
            Status = Value(board.StatusFieldId), Agent = Value(board.AgentFieldId), Machine = Value(board.MachineFieldId),
            WorkRepo = Value(board.WorkRepoFieldId), Branch = Value(board.BranchFieldId), PullRequestLinks = Value(board.PullRequestFieldId, MaximumLinkText + 1),
            NextActor = Value(board.NextActorFieldId), HoldReason = Value(board.HoldReasonFieldId)
        });
    }

    private static string Bound(string? text, int bound = 300) => text is null ? "" : text.Length <= bound ? text : text[..bound];

    /// <summary>Links are associations, not display text, so they are kept whole up to this size. Longer link text
    /// marks the item capped, and a capped item is never called finished on the links that fit.</summary>
    internal const int MaximumLinkText = 8000;
}
