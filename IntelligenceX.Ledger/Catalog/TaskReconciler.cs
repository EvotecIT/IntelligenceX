using IntelligenceX.Ledger.Contracts;
using V = IntelligenceX.Ledger.Contracts.TaskVerdicts;

namespace IntelligenceX.Ledger.Catalog;

/// <summary>
/// Grades every task from catalog and session evidence. Pure: the same snapshot, sessions and time always give
/// the same verdicts. Each verdict names its evidence and the facts that were missing, and suggests writes
/// without applying any.
/// </summary>
internal sealed class TaskReconciler(WorkCatalogSettings settings, IReadOnlyCollection<string> machines) {
    internal sealed record Worktree(string Repo, RepositorySession Session);

    internal TaskVerdictRow[] Reconcile(WorkCatalogSnapshot catalog, IReadOnlyCollection<LedgerItem> items,
        IReadOnlyCollection<Worktree> worktrees, DateTimeOffset now) {
        var sessions = items.Where(item => item.Kind == "session").ToDictionary(item => item.Id, StringComparer.Ordinal);
        var openIssues = catalog.Issues.Where(issue => !issue.PullRequest).ToDictionary(issue => issue.Url, StringComparer.OrdinalIgnoreCase);
        var openPulls = catalog.PullRequests.ToDictionary(pull => pull.Url, StringComparer.OrdinalIgnoreCase);
        var context = new Context(catalog, sessions, openIssues, openPulls, worktrees, now, now.AddDays(-settings.StaleAfterDays));
        var rows = new List<TaskVerdictRow>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog.Board) {
            if (item.ContentUrl is { } url && item.ContentType == "Issue") covered.Add(url);
            rows.Add(Board(item, context));
        }
        foreach (var issue in catalog.Issues.Where(issue => WorkCatalogReader.IsTaskIssue(settings, issue)).Where(issue => !covered.Contains(issue.Url)))
            rows.Add(Issue(issue, context));
        return rows.OrderBy(row => Array.IndexOf(V.All, row.Verdict)).ThenByDescending(row => row.UpdatedAtUtc)
            .ThenBy(row => row.Id, StringComparer.Ordinal).ToArray();
    }

    private sealed record Context(WorkCatalogSnapshot Catalog, Dictionary<string, LedgerItem> Sessions,
        Dictionary<string, CatalogIssue> OpenIssues, Dictionary<string, CatalogPull> OpenPulls,
        IReadOnlyCollection<Worktree> Worktrees, DateTimeOffset Now, DateTimeOffset StaleBefore);

    private sealed class Draft {
        public List<string> Evidence { get; } = [];
        public List<string> Missing { get; } = [];
        public List<string> Actions { get; } = [];
    }

    private TaskVerdictRow Issue(CatalogIssue issue, Context context) {
        var draft = new Draft();
        draft.Evidence.Add("issue-open:" + issue.Key);
        draft.Evidence.Add("labels:" + string.Join(",", issue.Labels.Where(label => label.StartsWith(settings.TaskLabelPrefix, StringComparison.OrdinalIgnoreCase))));
        draft.Missing.Add("agent-claim");
        var references = context.Catalog.IssueReferences.GetValueOrDefault(issue.Key, []);
        var row = new TaskVerdictRow {
            Id = "issue:" + issue.Key, Source = "issue", Title = issue.Title, Url = issue.Url, Repo = issue.Repo,
            Verdict = "", Reason = "", PullRequests = references, UpdatedAtUtc = issue.UpdatedAtUtc
        };
        if (PullRequestsDone(references, context, draft, viaReferences: true) is { } done) return Finish(row, V.DoneClosable, done, draft, issue.Url, null);
        if (OpenPullRequest(references, context, draft) is { } pull) return Active(row, pull, context, draft);
        return Finish(row, issue.UpdatedAtUtc < context.StaleBefore ? V.Stale : V.NotStarted,
            issue.UpdatedAtUtc < context.StaleBefore ? "no-activity" : "unclaimed", draft, null, null);
    }

    private TaskVerdictRow Board(CatalogBoardItem item, Context context) {
        var draft = new Draft();
        var board = settings.Board!;
        CatalogIssue? issue = item.ContentUrl is { } contentUrl && context.OpenIssues.TryGetValue(contentUrl, out var found) ? found : null;
        var issueOpen = issue is not null || item.ContentType == "Issue" && item.ContentState == "open";
        var issueClosed = issue is null && item.ContentType == "Issue" && item.ContentState == "closed";
        if (item.ContentType == "Issue" && !issueOpen && !issueClosed) draft.Missing.Add("issue-state");
        var repo = item.WorkRepo ?? issue?.Repo ?? RepoFromUrl(item.ContentUrl);
        var links = WorkCatalogReader.PullRequestLinks(item.PullRequestLinks).ToList();
        if (item.PullRequestLinks?.Length > WorkCatalogReader.MaximumLinkText) draft.Missing.Add("pull-request-links-capped");
        if (issue is not null) links.AddRange(context.Catalog.IssueReferences.GetValueOrDefault(issue.Key, []));
        if (item.ContentType == "PullRequest" && item.ContentUrl is not null) links.Add(item.ContentUrl);
        links = links.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var updated = new[] { item.UpdatedAtUtc, issue?.UpdatedAtUtc ?? DateTimeOffset.MinValue }.Max();
        var status = item.Status;
        var done = status is not null && board.DoneStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);
        var row = new TaskVerdictRow {
            Id = "board:" + item.Id, Source = issue is not null || item.ContentType == "Issue" ? "board+issue" : "board",
            Title = item.Title, Url = item.ContentUrl, Repo = repo, Verdict = "", Reason = "", BoardStatus = status,
            Agent = item.Agent, Machine = item.Machine, PullRequests = links.ToArray(), Branch = item.Branch, UpdatedAtUtc = updated
        };
        draft.Evidence.Add("board-status:" + (status ?? "none"));
        var markDone = done ? null : "mark-board-done:" + item.Id;

        if (item.Archived) return Finish(row, V.Archived, "board-item-archived", draft, null, null);
        if (issueClosed) {
            draft.Evidence.Add("issue-closed");
            return done ? Finish(row, V.Archived, "issue-closed", draft, null, null)
                : Finish(row, V.DoneClosable, "issue-closed-board-open", draft, null, markDone);
        }
        if (item.ContentType == "PullRequest" && item.ContentState is "merged" or "closed") {
            draft.Evidence.Add("pull-request-" + item.ContentState);
            return done ? Finish(row, V.Archived, "pull-request-" + item.ContentState, draft, null, null)
                : Finish(row, V.DoneClosable, "pull-request-" + item.ContentState + "-board-open", draft, null, markDone);
        }
        if (done) return issueOpen
            ? Finish(row, V.DoneClosable, "board-done-issue-open", draft, item.ContentUrl, null)
            : Finish(row, V.Archived, "board-done", draft, null, null);
        if (!draft.Missing.Contains("pull-request-links-capped") &&
            PullRequestsDone(links, context, draft, viaReferences: issue is not null && WorkCatalogReader.PullRequestLinks(item.PullRequestLinks).Count() == 0) is { } merged)
            return Finish(row, V.DoneClosable, merged, draft, issueOpen ? item.ContentUrl : null, markDone);
        if (status is not null && board.WaitingStatuses.Contains(status, StringComparer.OrdinalIgnoreCase) ||
            item.NextActor is { } actor && board.WaitingMarkers.Contains(actor, StringComparer.OrdinalIgnoreCase) ||
            item.HoldReason is { } hold && board.WaitingMarkers.Contains(hold, StringComparer.OrdinalIgnoreCase)) {
            draft.Evidence.Add("board-waiting:" + (item.NextActor ?? item.HoldReason ?? status));
            return Finish(row, V.WaitingOnYou, "board-waiting-for-you", draft, null, null);
        }

        // An agent claim: provider:session-id names the session; anything else is a claim without one.
        var claimed = !string.IsNullOrWhiteSpace(item.Agent);
        if (claimed && SessionFor(item, context, draft) is { } session) {
            row = row with { SessionItemId = session.Id, SessionVerdict = session.Current?.Value };
            if (session.Current?.LastKnown == true) {
                draft.Evidence.Add("session-verdict-last-known");
                draft.Missing.Add("session-current-observation"); // Its reporter is silent; this is the last verdict seen.
            }
            var where = Where(session);
            if (session.Session?.Archived == true) return Finish(row with { Where = where }, V.NeedsReplacement, "agent-archived", draft, null, "reassign:" + row.Id);
            switch (session.Current?.Value) {
                case "needs-you": return Finish(row with { Where = where }, V.WaitingOnYou, "session-" + session.Current.ReasonCode, draft, null, null);
                case "stuck": return Finish(row with { Where = where }, V.NeedsReplacement, "session-" + session.Current.ReasonCode, draft, null, "reassign:" + row.Id);
                case "working" or "waiting": return Finish(row with { Where = where }, V.InProgress, "session-" + session.Current.Value, draft, null, null);
            }
            var last = session.Session?.LastActivityAtUtc ?? updated;
            draft.Evidence.Add("session-finished");
            return last < context.StaleBefore && updated < context.StaleBefore
                ? Finish(row with { Where = where }, V.Stale, "session-finished-task-open", draft, null, "ask-for-update:" + row.Id)
                : Finish(row with { Where = where }, V.WaitingOnYou, "session-finished-task-open", draft, null, null);
        }
        if (OpenPullRequest(links, context, draft) is { } pull) return Active(row, pull, context, draft);
        if (LiveWorktree(repo, item.Branch, context) is { } worktree) {
            draft.Evidence.Add("live-worktree");
            return Finish(row with { Where = worktree }, V.InProgress, "live-worktree", draft, null, null);
        }
        var planned = status is null || board.PlannedStatuses.Contains(status, StringComparer.OrdinalIgnoreCase);
        if (claimed && !planned) {
            if (draft.Missing.Count == 0) draft.Missing.Add("agent-session-id");
            return updated < context.StaleBefore
                ? Finish(row, row.Agent?.Contains(':') == true ? V.NeedsReplacement : V.Stale,
                    row.Agent?.Contains(':') == true ? "claimed-session-missing" : "claimed-no-activity", draft, null, "ask-for-update:" + row.Id)
                : Finish(row with { Where = item.Machine }, V.InProgress, "claimed", draft, null, null);
        }
        if (planned) return Finish(row, V.NotStarted, claimed ? "planned-claimed" : "planned", draft, null, null);
        draft.Missing.Add("agent-claim");
        return updated < context.StaleBefore
            ? Finish(row, V.Stale, "no-activity", draft, null, "ask-for-update:" + row.Id)
            : Finish(row, V.NotStarted, "unclaimed", draft, null, null);
    }

    /// <summary>All linked PRs merged or closed, at least one merged, none open. Unknown states are named, not guessed.</summary>
    private static string? PullRequestsDone(IReadOnlyCollection<string> links, Context context, Draft draft, bool viaReferences) {
        if (links.Count == 0) return null;
        var states = links.Select(url => State(url, context)).ToArray();
        foreach (var url in links.Where((_, index) => states[index] is not ("open" or "merged" or "closed")))
            draft.Missing.Add("pull-request-state:" + url);
        if (states.Any(state => state is not ("merged" or "closed")) || !states.Contains("merged")) return null;
        draft.Evidence.Add("pull-requests-merged:" + states.Count(state => state == "merged") + "/" + states.Length);
        if (viaReferences) draft.Missing.Add("completion-criteria-unverified"); // A referencing PR may not finish the task.
        return viaReferences ? "referencing-pull-requests-merged" : "pull-requests-merged";
    }

    /// <summary>Merged is terminal. Open and closed must have been confirmed by the latest catalog pass; an older
    /// observation kept after a failed read is reported as unknown rather than trusted.</summary>
    private static string? State(string url, Context context) {
        if (context.OpenPulls.ContainsKey(url)) return "open";
        if (!context.Catalog.PullStates.TryGetValue(url, out var state)) return null;
        if (state.State == "merged") return "merged";
        return context.Catalog.CapturedAtUtc - state.CheckedAtUtc <= FreshStateWindow ? state.State : "stale";
    }

    internal static readonly TimeSpan FreshStateWindow = TimeSpan.FromMinutes(15);

    /// <summary>The most recently updated open PR, from the open lists or from an individual read (for example a PR
    /// in a repository outside the configured owners).</summary>
    private static CatalogPull? OpenPullRequest(IEnumerable<string> links, Context context, Draft draft) {
        var pull = links.Select(url => context.OpenPulls.GetValueOrDefault(url) ?? FromState(url, context)).OfType<CatalogPull>()
            .MaxBy(pull => pull.UpdatedAtUtc);
        if (pull is not null) draft.Evidence.Add("open-pull-request:" + pull.Url);
        return pull;
    }

    private static CatalogPull? FromState(string url, Context context) =>
        State(url, context) == "open" && context.Catalog.PullStates[url] is var state &&
        Reporting.PullRequestCatalog.TryParse(url, out var repo, out var number)
            ? new CatalogPull(repo, number, state.Title ?? "", state.HeadBranch, state.Draft, state.UpdatedAtUtc ?? state.CheckedAtUtc)
            : null;

    private static TaskVerdictRow Active(TaskVerdictRow row, CatalogPull pull, Context context, Draft draft) {
        var worktree = LiveWorktree(pull.Repo, pull.HeadBranch, context);
        row = row with { Where = worktree ?? pull.Url, Branch = row.Branch ?? pull.HeadBranch };
        if (worktree is not null) return Finish(row, V.InProgress, "open-pull-request-live-worktree", draft, null, null);
        return pull.UpdatedAtUtc < context.StaleBefore
            ? Finish(row, V.Stale, "pull-request-idle", draft, null, "ask-for-update:" + row.Id)
            : Finish(row, V.InProgress, pull.Draft ? "draft-pull-request" : "open-pull-request", draft, null, null);
    }

    private LedgerItem? SessionFor(CatalogBoardItem item, Context context, Draft draft) {
        var parts = item.Agent!.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts[0] is not ("codex" or "claude") || !Guid.TryParse(parts[1].Split(':')[0], out var id)) {
            draft.Missing.Add("agent-session-id");
            return null;
        }
        var candidates = item.Machine is { } machine && settings.Board!.MachineAliases.TryGetValue(machine, out var alias)
            ? [alias] : machines;
        if (item.Machine is not null && !settings.Board!.MachineAliases.ContainsKey(item.Machine)) draft.Missing.Add("machine-alias:" + item.Machine);
        foreach (var candidate in candidates)
            if (context.Sessions.TryGetValue(LedgerScope.SessionItemId(candidate, parts[0], id.ToString("D")), out var session)) {
                draft.Evidence.Add("session:" + session.Id);
                return session;
            }
        draft.Missing.Add("session-unobserved");
        return null;
    }

    private static string? LiveWorktree(string? repo, string? branch, Context context) {
        if (repo is null || branch is null) return null;
        var match = context.Worktrees.Where(row => row.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(row.Session.Branch, branch, StringComparison.Ordinal) && row.Session.Live)
            .MaxBy(row => row.Session.LastActivityAtUtc);
        return match is null ? null : match.Session.Machine + " " + match.Session.Provider + " " + (match.Session.Worktree ?? "(checkout unknown)");
    }

    private static string Where(LedgerItem session) => session.MachineId + " " + session.Provider +
        (session.Session?.Repository?.Worktree is { } path ? " " + path : "");

    private static string? RepoFromUrl(string? url) =>
        url is not null && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host == "github.com" &&
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries) is [var owner, var name, ..] ? owner + "/" + name : null;

    private static TaskVerdictRow Finish(TaskVerdictRow row, string verdict, string reason, Draft draft, string? closeIssue, string? boardAction) {
        if (closeIssue is not null) draft.Actions.Add("close-issue:" + closeIssue);
        if (boardAction is not null) draft.Actions.Add(boardAction);
        return row with {
            Verdict = verdict, Reason = reason, Evidence = draft.Evidence.Distinct(StringComparer.Ordinal).ToArray(),
            MissingFacts = draft.Missing.Distinct(StringComparer.Ordinal).ToArray(),
            SuggestedActions = draft.Actions.Distinct(StringComparer.Ordinal).ToArray()
        };
    }
}
