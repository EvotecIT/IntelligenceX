using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

/// <summary>
/// Who started which session. A link is keyed by the child's provider and native id, so an ask's helper can be linked
/// before its machine reports it, and the first parent seen for a child stays: a later observation never re-parents it.
/// Takeovers are kept beside the links, so both the session that stopped and the one that continued show the handover.
/// </summary>
public sealed partial class LedgerStore {
    internal const int TreeNodeLimit = 200;
    private const int TreeDepthLimit = 64;
    private const int TrailEntryLimit = 300;

    private sealed record LinkRow(string ParentProvider, string ParentNative, string ChildProvider, string ChildNative, string Via,
        DateTimeOffset AtUtc, string? RequestId);
    /// <summary>One takeover. Moves pairs each reservation id From held with the id it has under By, so a later takeover of
    /// By can tell which of its reservations continue this work.</summary>
    private sealed record TakeoverRow(string FromProvider, string FromNative, string ByProvider, string ByNative,
        DateTimeOffset AtUtc, string Trigger, string? Reason, string[] Leases) {
        public LeaseMove[] Moves { get; init; } = [];
    }
    private sealed record LeaseMove(string Previous, string Lease);

    /// <summary>
    /// Where the work last moved away from a session is now: the takeover that last moved it and the reservation ids it
    /// has there. It follows later takeovers of each holder that moved this work on (when no reservation moved, any later
    /// takeover of the holder carries it). Null when the session never handed work away or the work came back to it.
    /// </summary>
    private static (TakeoverRow Row, HashSet<string> Leases)? Continuation(IReadOnlyList<TakeoverRow> takeovers, string provider, string native) {
        var last = takeovers.Select((row, index) => (row, index)).LastOrDefault(pair => pair.row.FromProvider == provider && pair.row.FromNative == native);
        if (last.row is null) return null;
        var holding = last.row;
        var leases = last.row.Moves.Select(move => move.Lease).ToHashSet(StringComparer.Ordinal);
        var carriesLeases = leases.Count > 0;
        foreach (var row in takeovers.Skip(last.index + 1)) {
            if (row.FromProvider != holding.ByProvider || row.FromNative != holding.ByNative) continue;
            var next = row.Moves.Where(move => leases.Contains(move.Previous)).Select(move => move.Lease).ToHashSet(StringComparer.Ordinal);
            if (carriesLeases && next.Count == 0) continue;
            holding = row;
            if (carriesLeases) leases = next;
        }
        return holding.ByProvider == provider && holding.ByNative == native ? null : (holding, leases);
    }
    private sealed record TreeNodeRow(string Provider, string Native, int Depth, LinkRow? Link, LedgerItem? Item);

    private static async Task InitializeLineageAsync(SQLiteAsyncSession db, CancellationToken token) {
        await db.ExecuteNonQueryAsync("""
            CREATE TABLE IF NOT EXISTS session_lineage(child_provider TEXT NOT NULL,child_native TEXT NOT NULL,
              parent_provider TEXT NOT NULL,parent_native TEXT NOT NULL,via TEXT NOT NULL,observed TEXT NOT NULL,request TEXT,
              PRIMARY KEY(child_provider,child_native));
            CREATE INDEX IF NOT EXISTS session_lineage_parent ON session_lineage(parent_provider,parent_native);
            CREATE TABLE IF NOT EXISTS session_takeovers(ordinal INTEGER PRIMARY KEY AUTOINCREMENT,
              from_provider TEXT NOT NULL,from_native TEXT NOT NULL,by_provider TEXT NOT NULL,by_native TEXT NOT NULL,
              recorded TEXT NOT NULL,json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS session_takeovers_from ON session_takeovers(from_provider,from_native);
            CREATE INDEX IF NOT EXISTS session_takeovers_by ON session_takeovers(by_provider,by_native);
            """, cancellationToken: token);
    }

    private static string? Native(string? value) => Guid.TryParse(value, out var id) ? id.ToString() : null;

    private static Task<int> RecordLinkAsync(SQLiteAsyncSession db, SessionLink link, CancellationToken token) =>
        db.ExecuteNonQueryAsync("""
            INSERT OR IGNORE INTO session_lineage(child_provider,child_native,parent_provider,parent_native,via,observed,request)
            VALUES(@childProvider,@child,@parentProvider,@parent,@via,@observed,@request)
            """, new Dictionary<string, object?> { ["childProvider"] = link.ChildProvider, ["child"] = link.ChildSessionId,
                ["parentProvider"] = link.ParentProvider, ["parent"] = link.ParentSessionId, ["via"] = link.Via,
                ["observed"] = link.AtUtc.ToUniversalTime().ToString("O"), ["request"] = link.RequestId }, token);

    /// <summary>Records links seen outside the reporter, such as an ask's helper bound to its request. Links whose ids
    /// aren't provider session ids, or that point at themselves, are skipped. Returns how many were new.</summary>
    public async Task<int> RecordLinksAsync(IEnumerable<SessionLink> links, CancellationToken token = default) {
        var valid = links.Select(Normalize).OfType<SessionLink>().ToArray();
        if (valid.Length == 0) return 0;
        await using var db = await _database.OpenSessionAsync(path, token);
        return await db.RunInTransactionAsync(async (tx, ct) => {
            var added = 0;
            foreach (var link in valid) added += await RecordLinkAsync(tx, link, ct);
            return added;
        }, token);
    }

    private static SessionLink? Normalize(SessionLink link) =>
        link.ParentProvider is "codex" or "claude" && link.ChildProvider is "codex" or "claude" && Label(link.Via, 64) &&
        Native(link.ParentSessionId) is { } parent && Native(link.ChildSessionId) is { } child &&
        !(parent == child && link.ParentProvider == link.ChildProvider) && (link.RequestId is null || Label(link.RequestId, 128))
            ? link with { ParentSessionId = parent, ChildSessionId = child } : null;

    private static LinkRow Row(SessionLink link) =>
        new(link.ParentProvider, link.ParentSessionId, link.ChildProvider, link.ChildSessionId, link.Via, link.AtUtc, link.RequestId);

    private const string LinkColumns = "SELECT child_provider,child_native,parent_provider,parent_native,via,observed,request FROM session_lineage ";

    /// <summary>Stored links whose child (or parent, with <paramref name="byParent"/>) is one of the keys, by indexed lookups.</summary>
    private static async Task<List<LinkRow>> StoredLinksAsync(SQLiteAsyncSession db, IEnumerable<(string Provider, string Native)> keys, bool byParent,
        CancellationToken token) {
        var found = new List<LinkRow>();
        var (providerColumn, nativeColumn) = byParent ? ("parent_provider", "parent_native") : ("child_provider", "child_native");
        foreach (var chunk in keys.Distinct().Chunk(100)) {
            var args = new Dictionary<string, object?>();
            var terms = new List<string>();
            for (var i = 0; i < chunk.Length; i++) {
                args["p" + i] = chunk[i].Provider;
                args["n" + i] = chunk[i].Native;
                terms.Add("(" + providerColumn + "=@p" + i + " AND " + nativeColumn + "=@n" + i + ")");
            }
            found.AddRange(await db.QueryAsListAsync(LinkColumns + "WHERE " + string.Join(" OR ", terms),
                row => new LinkRow(row.GetString(2), row.GetString(3), row.GetString(0), row.GetString(1), row.GetString(4),
                    DateTimeOffset.Parse(row.GetString(5), System.Globalization.CultureInfo.InvariantCulture), row.IsDBNull(6) ? null : row.GetString(6)),
                args, cancellationToken: token));
        }
        return found;
    }

    /// <summary>
    /// The links of the tree that holds one session: its ancestors, then its root's descendants level by level, read
    /// through the indexes rather than the whole table. Stored links win; a live link fills only a child that has none.
    /// Unexplored lists the sessions whose children weren't read because the tree is past the node limit.
    /// </summary>
    private static async Task<(Dictionary<(string, string), LinkRow> Links, HashSet<(string, string)> Unexplored)> ComponentAsync(
        SQLiteAsyncSession db, (string Provider, string Native) start, IEnumerable<SessionLink> live, CancellationToken token) {
        var liveRows = live.Select(Normalize).OfType<SessionLink>().Select(Row).ToArray();
        var liveByChild = liveRows.GroupBy(row => (row.ChildProvider, row.ChildNative)).ToDictionary(group => group.Key, group => group.First());
        var liveByParent = liveRows.ToLookup(row => (row.ParentProvider, row.ParentNative));
        var links = new Dictionary<(string, string), LinkRow>();
        var root = start;
        var climbed = new HashSet<(string, string)> { root };
        while (climbed.Count < TreeDepthLimit) {
            var up = (await StoredLinksAsync(db, [root], false, token)).FirstOrDefault() ?? liveByChild.GetValueOrDefault(root);
            if (up is null) break;
            links[root] = up;
            if (!climbed.Add((up.ParentProvider, up.ParentNative))) break;
            root = (up.ParentProvider, up.ParentNative);
        }
        var visited = new HashSet<(string, string)> { root };
        var frontier = new List<(string, string)> { root };
        while (frontier.Count > 0 && visited.Count <= TreeNodeLimit) {
            var stored = await StoredLinksAsync(db, frontier, true, token);
            var storedChildren = stored.Select(row => (row.ChildProvider, row.ChildNative)).ToHashSet();
            var candidates = frontier.SelectMany(key => liveByParent[key]).Where(row => !storedChildren.Contains((row.ChildProvider, row.ChildNative))).ToArray();
            // A live child that already has a stored parent elsewhere stays under that parent.
            var elsewhere = (await StoredLinksAsync(db, candidates.Select(row => (row.ChildProvider, row.ChildNative)), false, token))
                .Select(row => (row.ChildProvider, row.ChildNative)).ToHashSet();
            var next = new List<(string, string)>();
            foreach (var row in stored.Concat(candidates.Where(row => !elsewhere.Contains((row.ChildProvider, row.ChildNative))))) {
                var child = (row.ChildProvider, row.ChildNative);
                links.TryAdd(child, row);
                if (visited.Add(child)) next.Add(child);
            }
            frontier = next;
        }
        return (links, frontier.ToHashSet());
    }

    /// <summary>Children of one session, stored first, then live links for children that have no stored parent.</summary>
    private static async Task<string[]> ChildrenAsync(SQLiteAsyncSession db, (string Provider, string Native) parent, IEnumerable<SessionLink> live,
        CancellationToken token) {
        var stored = await StoredLinksAsync(db, [parent], true, token);
        var candidates = live.Select(Normalize).OfType<SessionLink>()
            .Where(link => (link.ParentProvider, link.ParentSessionId) == parent).Select(Row).ToArray();
        var elsewhere = (await StoredLinksAsync(db, candidates.Select(row => (row.ChildProvider, row.ChildNative)), false, token))
            .Select(row => (row.ChildProvider, row.ChildNative)).ToHashSet();
        return stored.Concat(candidates.Where(row => !elsewhere.Contains((row.ChildProvider, row.ChildNative))))
            .Select(row => row.ChildNative).Distinct().ToArray();
    }

    private static async Task<IReadOnlyList<TakeoverRow>> TakeoversAsync(SQLiteAsyncSession db, CancellationToken token) =>
        await db.QueryAsListAsync("SELECT json FROM session_takeovers ORDER BY ordinal", row => Read<TakeoverRow>(row.GetString(0)), cancellationToken: token);

    /// <summary>Resolves a session query the way session_status does: native id, item id, then a unique title fragment.</summary>
    private static async Task<IReadOnlyList<(string Item, SessionMatch Match)>> ResolveSessionAsync(SQLiteAsyncSession db, string query, CancellationToken token) {
        if (!Label(query, 240)) throw new ArgumentException("Invalid session identity or title fragment.");
        var args = new Dictionary<string, object?> { ["query"] = Guid.TryParse(query, out var id) ? id.ToString() : query };
        var matches = await db.QueryAsListAsync("SELECT item,native,machine,provider,title FROM session_lookup WHERE native=@query OR item=@query LIMIT 3",
            row => (row.GetString(0), new SessionMatch(row.IsDBNull(1) ? row.GetString(0) : row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4))),
            args, cancellationToken: token);
        if (matches.Count == 0 && !Guid.TryParse(query, out _)) matches = await db.QueryAsListAsync("""
            SELECT item,native,machine,provider,title FROM session_lookup WHERE instr(lower(title),lower(@query))>0 ORDER BY item LIMIT 3
            """, row => (row.GetString(0), new SessionMatch(row.IsDBNull(1) ? row.GetString(0) : row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4))),
            args, cancellationToken: token);
        return matches;
    }

    private async Task<Dictionary<(string, string), LedgerItem>> SessionItemsAsync(SQLiteAsyncSession db, IEnumerable<(string Provider, string Native)> keys,
        DateTimeOffset now, CancellationToken token) {
        var global = await ReadGlobalAsync(db, token);
        var found = new Dictionary<(string, string), LedgerItem>();
        foreach (var chunk in keys.Distinct().Chunk(100)) {
            var args = new Dictionary<string, object?>();
            var terms = new List<string>();
            for (var i = 0; i < chunk.Length; i++) {
                args["p" + i] = chunk[i].Provider;
                args["n" + i] = chunk[i].Native;
                terms.Add("(s.provider=@p" + i + " AND s.native=@n" + i + ")");
            }
            // Several machines can't share a native id; the newest observation is the session's current home.
            var rows = await db.QueryAsListAsync("SELECT i.json FROM session_lookup s JOIN items i ON i.id=s.item WHERE " +
                string.Join(" OR ", terms) + " ORDER BY json_extract(i.json,'$.session.lastActivityAtUtc')",
                row => Read<LedgerItem>(row.GetString(0)), args, cancellationToken: token);
            foreach (var item in rows)
                if (item.Session?.SourceSessionId is { } native) found[(item.Provider, native.ToString())] = ApplyGlobal(item, global, now);
        }
        return found;
    }

    /// <summary>
    /// The tree around one session: up to its root, then every descendant, depth first. Live links (asks that the
    /// coordination service bound but hasn't handed over yet) fill gaps in the stored ones.
    /// </summary>
    public async Task<SessionTree> SessionTreeAsync(string query, DateTimeOffset now, IEnumerable<SessionLink>? live = null,
        HashSet<string>? silentMachines = null, CancellationToken token = default) {
        var policy = await ReadPolicyAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        var matches = await ResolveSessionAsync(db, query, token);
        if (matches.Count == 0) {
            // A session that hasn't reported yet (another machine, a helper bound moments ago, or one from before this
            // version) is still a node when a link names it, as a parent or as a child.
            if (Native(query) is { } orphan && await LinkedProviderAsync(db, orphan, live ?? [], token) is { } orphanProvider)
                return await TreeFromAsync(db, orphanProvider, orphan, live ?? [], policy, now, silentMachines, query, token);
            return new() { State = "not-found", Query = query };
        }
        if (matches.Count > 1) return new() { State = "ambiguous", Query = query, Matches = matches.Select(m => m.Match).ToArray() };
        var item = Read<LedgerItem>((string)(await db.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
            new Dictionary<string, object?> { ["id"] = matches[0].Item }, token))!);
        if (item.Session?.SourceSessionId is not { } native) return new() { State = "not-found", Query = query };
        return await TreeFromAsync(db, item.Provider, native.ToString(), live ?? [], policy, now, silentMachines, query, token);
    }

    private static async Task<string?> LinkedProviderAsync(SQLiteAsyncSession db, string native, IEnumerable<SessionLink> live, CancellationToken token) {
        var args = new Dictionary<string, object?> { ["native"] = native };
        if (await db.ExecuteScalarAsync("SELECT child_provider FROM session_lineage WHERE child_native=@native LIMIT 1", args, token) is string child) return child;
        if (await db.ExecuteScalarAsync("SELECT parent_provider FROM session_lineage WHERE parent_native=@native LIMIT 1", args, token) is string parent) return parent;
        var link = live.Select(Normalize).OfType<SessionLink>().FirstOrDefault(row => row.ChildSessionId == native || row.ParentSessionId == native);
        return link is null ? null : link.ChildSessionId == native ? link.ChildProvider : link.ParentProvider;
    }

    private async Task<SessionTree> TreeFromAsync(SQLiteAsyncSession db, string provider, string native, IEnumerable<SessionLink> live,
        CoordinationPolicy policy, DateTimeOffset now, HashSet<string>? silentMachines, string query, CancellationToken token) {
        var (links, unexplored) = await ComponentAsync(db, (provider, native), live, token);
        var (nodes, omitted) = await WalkAsync(db, provider, native, links, now, token);
        // A session with descendants left out of the response has an incomplete subtree: its rollup counts them as
        // unknown and it is never offered for archiving.
        var incomplete = new HashSet<(string, string)>(unexplored);
        foreach (var key in omitted.Concat(unexplored)) {
            var current = key;
            for (var depth = 0; depth < TreeDepthLimit && links.TryGetValue(current, out var up); depth++) {
                current = (up.ParentProvider, up.ParentNative);
                if (!incomplete.Add(current)) break;
            }
        }
        var truncated = omitted.Count > 0 || unexplored.Count > 0;
        var reservations = await ReservationsAsync(db, now, token);
        var takeovers = await TakeoversAsync(db, token);
        var verdicts = nodes.ToDictionary(node => (node.Provider, node.Native),
            node => node.Item is null ? null : rules.Evaluate(node.Item, now, silentMachines?.Contains(node.Item.MachineId) == true));
        var children = nodes.Where(node => node.Link is not null)
            .GroupBy(node => (node.Link!.ParentProvider, node.Link.ParentNative))
            .ToDictionary(group => group.Key, group => group.Select(node => (node.Provider, node.Native)).ToArray());
        var subtree = new Dictionary<(string, string), string?>();
        string? Rollup((string, string) key) {
            if (subtree.TryGetValue(key, out var known)) return known;
            subtree[key] = null; // cycle guard
            // A session that hasn't reported yet (a helper bound moments ago) may still be running: it counts as unknown.
            var value = Worse(verdicts.GetValueOrDefault(key)?.Value ?? "unknown", incomplete.Contains(key) ? "unknown" : null);
            foreach (var child in children.GetValueOrDefault(key) ?? []) value = Worse(value, Rollup(child));
            return subtree[key] = value;
        }
        var view = new List<SessionTreeNode>();
        foreach (var node in nodes) {
            var key = (node.Provider, node.Native);
            var fact = node.Item?.Session;
            var verdict = verdicts[key];
            var held = node.Item is null ? [] : reservations.Where(row => row.State == "active" && IsHolder(node.Item, row.Lease.Holder))
                .Select(row => row.Lease.Key).Distinct().Order(StringComparer.Ordinal).ToArray();
            var takenOver = Continuation(takeovers, node.Provider, node.Native)?.Row;
            var tookOver = takeovers.LastOrDefault(row => row.ByProvider == node.Provider && row.ByNative == node.Native);
            var running = (children.GetValueOrDefault(key) ?? []).Any(child => Rollup(child) is "working" or "stuck" or "needs-you");
            var (suggestion, why) = takenOver is not null ? (null, null) : Suggest(fact, verdict, held.Length, running);
            view.Add(new SessionTreeNode {
                Id = node.Native, Provider = node.Provider, Machine = node.Item?.MachineId ?? "unobserved", Depth = node.Depth,
                StartedBy = node.Link?.ParentNative, StartedVia = node.Link?.Via,
                Title = fact?.Title ?? fact?.Repository?.Title, Origin = fact?.Origin ?? "unknown",
                Verdict = verdict?.Value, Reason = verdict?.ReasonCode, SubtreeVerdict = Rollup(key),
                CreatedAtUtc = fact?.CreatedAtUtc ?? node.Item?.FirstObservedAtUtc, LastActivityAtUtc = fact?.LastActivityAtUtc,
                Archived = fact?.Archived == true, UsageLimited = fact?.UsageLimit == true,
                Repo = fact?.Repository?.Repo, Branch = fact?.Repository?.Branch,
                PullRequests = fact?.Repository?.PullRequests is { Length: > 0 } prs ? prs : null,
                Leases = held.Length == 0 ? null : held,
                TakenOverBy = takenOver?.ByNative, TookOverFrom = tookOver?.FromNative,
                Suggestion = suggestion, SuggestionReason = why
            });
        }
        // Archiving is planned for the root's descendants: the root itself is the outcome someone accepts.
        var archive = new List<SessionCleanupEntry>();
        var archiveHeld = new List<SessionCleanupEntry>();
        foreach (var node in nodes.Skip(1)) {
            if (node.Item?.Session is not { } fact || fact.Archived == true) continue;
            var verdict = verdicts[(node.Provider, node.Native)]?.Value;
            var below = Descendants((node.Provider, node.Native)).Select(Rollup).Aggregate((string?)null, Worse);
            var reason = verdict != "done" ? "not-finished:" + (verdict ?? "unknown")
                : incomplete.Contains((node.Provider, node.Native)) ? "subtree-truncated"
                : below is "working" or "stuck" or "needs-you" or "waiting" ? "subtree-still-live"
                : below is "unknown" ? "subtree-unresolved"
                : await CleanupReasonAsync(db, node.Item, reservations, now, token);
            var entry = new SessionCleanupEntry(node.Native, node.Item.MachineId, node.Provider, fact.Title ?? fact.Repository?.Title, fact.Origin,
                fact.LastActivityAtUtc is { } activity ? Math.Round((now - activity).TotalDays, 1) : 0, fact.Repository?.Worktree,
                fact.Repository?.Branch, reason);
            if (reason is "clean-and-pushed" or "workspace-already-missing") archive.Add(entry);
            else if (fact.Origin == "agent") archiveHeld.Add(entry);
        }
        var root = nodes[0];
        return new SessionTree { State = "found", Query = query, Root = root.Native, SubtreeVerdict = Rollup((root.Provider, root.Native)),
            Nodes = view.ToArray(), Total = view.Count, Truncated = truncated,
            Archive = archive.ToArray(), ArchiveHeld = archiveHeld.ToArray() };

        IEnumerable<(string, string)> Descendants((string, string) key) {
            foreach (var child in children.GetValueOrDefault(key) ?? []) {
                yield return child;
                foreach (var grandchild in Descendants(child)) yield return grandchild;
            }
        }
    }

    /// <summary>Climbs to the root, then lists the root and its descendants depth first, children oldest first.</summary>
    private async Task<(List<TreeNodeRow> Nodes, HashSet<(string, string)> Omitted)> WalkAsync(SQLiteAsyncSession db, string provider, string native,
        Dictionary<(string, string), LinkRow> links, DateTimeOffset now, CancellationToken token) {
        var root = (provider, native);
        var climbed = new HashSet<(string, string)> { root };
        while (climbed.Count < TreeDepthLimit && links.TryGetValue(root, out var up) && climbed.Add((up.ParentProvider, up.ParentNative)))
            root = (up.ParentProvider, up.ParentNative);
        var byParent = links.Values.GroupBy(link => (link.ParentProvider, link.ParentNative))
            .ToDictionary(group => group.Key, group => group.OrderBy(link => link.AtUtc).ThenBy(link => link.ChildNative, StringComparer.Ordinal).ToArray());
        var order = new List<(string Provider, string Native, int Depth, LinkRow? Link)>();
        var seen = new HashSet<(string, string)>();
        var omitted = new HashSet<(string, string)>();
        void Visit((string Provider, string Native) key, int depth, LinkRow? link) {
            if (!seen.Add(key)) return;
            if (order.Count >= TreeNodeLimit) { omitted.Add(key); return; }
            order.Add((key.Provider, key.Native, depth, link));
            foreach (var child in byParent.GetValueOrDefault(key) ?? []) Visit((child.ChildProvider, child.ChildNative), depth + 1, child);
        }
        Visit(root, 0, null);
        var items = await SessionItemsAsync(db, order.Select(row => (row.Provider, row.Native)), now, token);
        return (order.Select(row => new TreeNodeRow(row.Provider, row.Native, row.Depth, row.Link,
            items.GetValueOrDefault((row.Provider, row.Native)))).ToList(), omitted);
    }

    private static readonly string[] Urgency = ["stuck", "needs-you", "working", "waiting", "unknown", "done"];

    /// <summary>The more urgent of two verdicts; anything unlisted ranks with unknown.</summary>
    private static string? Worse(string? left, string? right) {
        if (left is null) return right;
        if (right is null) return left;
        int Rank(string value) { var index = Array.IndexOf(Urgency, value); return index < 0 ? Array.IndexOf(Urgency, "unknown") : index; }
        return Rank(left) <= Rank(right) ? left : right;
    }

    private static (string? Suggestion, string? Reason) Suggest(SessionFact? fact, Verdict? verdict, int leases, bool runningChildren) {
        if (fact is null) return (null, null);
        if (verdict?.Value == "stuck") return ("take-over", "stuck:" + verdict.ReasonCode);
        if (fact.UsageLimit && fact.Archived != true) return ("take-over", "usage-limit");
        if (fact.Archived == true && (leases > 0 || runningChildren || verdict?.Value is "working" or "waiting" or "needs-you"))
            return ("take-over", leases > 0 ? "archived-holding-leases" : runningChildren ? "archived-with-running-children" : "archived-unfinished");
        return (null, null);
    }

    /// <summary>
    /// A tree's history, oldest first: each session's start and parent, its branch and pull requests, asks and their stages,
    /// reservations taken and ended, takeovers, verdict changes within the transition window, and archiving.
    /// </summary>
    public async Task<SessionTrail> SessionTrailAsync(string query, DateTimeOffset now, IEnumerable<SessionLink>? live = null,
        IEnumerable<SessionAskStep>? asks = null, CancellationToken token = default) {
        var tree = await SessionTreeAsync(query, now, live, token: token);
        if (tree.State != "found") return new() { State = tree.State, Matches = tree.Matches };
        await using var db = await _database.OpenSessionAsync(path, token);
        var keys = tree.Nodes.Select(node => (node.Provider, node.Id)).ToHashSet();
        var entries = new List<SessionTrailEntry>();
        foreach (var node in tree.Nodes) {
            entries.Add(new(node.CreatedAtUtc, "started", node.Id, node.StartedBy is null ? node.Title
                : "by " + node.StartedBy + (node.StartedVia is null ? "" : " via " + node.StartedVia)));
            if (node.Branch is not null) entries.Add(new(node.CreatedAtUtc, "branch", node.Id, node.Repo + ":" + node.Branch));
            foreach (var pr in node.PullRequests ?? []) entries.Add(new(null, "pull-request", node.Id, pr));
        }
        var items = await SessionItemsAsync(db, keys, now, token);
        // Holders are matched the way the lease service resolves them: the item id exactly, or the session id in any GUID
        // format or letter case. Every format writes the id's first eight digits together, so the query narrows by those
        // and IsHolder decides.
        var leaseArgs = new Dictionary<string, object?>();
        var patterns = items.Values.Select((item, i) => {
            leaseArgs["i" + i] = item.Id;
            leaseArgs["h" + i] = "%" + (item.Session?.SourceSessionId?.ToString("N")[..8] ?? item.Id) + "%";
            return "json_extract(json,'$.holder')=@i" + i + " OR lower(json_extract(json,'$.holder')) LIKE @h" + i;
        }).ToArray();
        var leaseRows = patterns.Length == 0 ? [] : await db.QueryAsListAsync(
            "SELECT started,ended,json FROM lease_history WHERE " + string.Join(" OR ", patterns) + " ORDER BY started",
            row => (Started: DateTimeOffset.Parse(row.GetString(0), System.Globalization.CultureInfo.InvariantCulture),
                Ended: DateTimeOffset.Parse(row.GetString(1), System.Globalization.CultureInfo.InvariantCulture), Lease: Read<Lease>(row.GetString(2))),
            leaseArgs, cancellationToken: token);
        foreach (var lease in leaseRows) {
            if (items.FirstOrDefault(pair => IsHolder(pair.Value, lease.Lease.Holder)).Key.Item2 is not { } owner) continue;
            entries.Add(new(lease.Started, "lease", owner, lease.Lease.Key));
            if (lease.Ended <= now) entries.Add(new(lease.Ended, "lease-ended", owner, lease.Lease.Key));
        }
        foreach (var (key, item) in items) {
            if (item.Session is { Archived: true, ArchiveObservedAtUtc: { } archivedAt }) entries.Add(new(archivedAt, "archived", key.Item2));
        }
        var itemArgs = new Dictionary<string, object?>();
        var sessionOf = new Dictionary<string, string>(StringComparer.Ordinal);
        var itemNames = items.Select((pair, i) => { itemArgs["i" + i] = pair.Value.Id; sessionOf[pair.Value.Id] = pair.Key.Item2; return "@i" + i; }).ToArray();
        var transitions = itemNames.Length == 0 ? [] : await db.QueryAsListAsync(
            "SELECT item,recorded,json FROM transitions WHERE item IN (" + string.Join(",", itemNames) + ") ORDER BY ordinal",
            row => (Item: row.GetString(0), At: DateTimeOffset.Parse(row.GetString(1), System.Globalization.CultureInfo.InvariantCulture), Json: row.GetString(2)),
            itemArgs, cancellationToken: token);
        foreach (var transition in transitions) {
            using var document = System.Text.Json.JsonDocument.Parse(transition.Json);
            var verdict = document.RootElement.TryGetProperty("verdict", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Object ? value : default;
            string? Text(string name) => verdict.ValueKind == System.Text.Json.JsonValueKind.Object && verdict.TryGetProperty(name, out var field) &&
                field.ValueKind == System.Text.Json.JsonValueKind.String ? field.GetString() : null;
            entries.Add(new(transition.At, "verdict", sessionOf[transition.Item], Text("value") + ":" + Text("reasonCode")));
        }
        foreach (var takeover in await TakeoversAsync(db, token))
            if (keys.Contains((takeover.FromProvider, takeover.FromNative)) || keys.Contains((takeover.ByProvider, takeover.ByNative)))
                entries.Add(new(takeover.AtUtc, "took-over", takeover.ByNative, "from " + takeover.FromNative + " (" + takeover.Trigger + ")" +
                    (takeover.Leases.Length > 0 ? " leases " + string.Join(",", takeover.Leases) : "")));
        foreach (var ask in asks ?? []) {
            var requester = (ask.RequesterProvider, Native(ask.RequesterSessionId) ?? "");
            var helper = (ask.HelperProvider ?? "", Native(ask.HelperSessionId) ?? "");
            if (!keys.Contains(requester) && !keys.Contains(helper)) continue;
            entries.Add(new(ask.RequestedAtUtc, "asked", requester.Item2, ask.RequestId + (ask.HelperSessionId is null ? "" : " helper " + helper.Item2)));
            foreach (var stage in ask.Stages.OrderBy(pair => pair.Value))
                entries.Add(new(stage.Value, "ask-" + stage.Key, requester.Item2, ask.RequestId +
                    (stage.Key == "terminal" && ask.ResultRef is not null ? " " + ask.ResultRef : "")));
        }
        // A long trail keeps its newest timed steps, which is what a session picking up the work needs; undated facts
        // (pull requests) follow them.
        var timed = entries.Where(entry => entry.AtUtc is not null).OrderBy(entry => entry.AtUtc).ToArray();
        var undated = entries.Where(entry => entry.AtUtc is null).ToArray();
        var kept = timed.Skip(Math.Max(0, timed.Length - TrailEntryLimit)).Concat(undated).Take(TrailEntryLimit + undated.Length).ToArray();
        return new() { State = "found", Root = tree.Root, Entries = kept, Truncated = timed.Length > TrailEntryLimit || tree.Truncated };
    }

    /// <summary>
    /// A live session takes over a stopped one. From qualifies when its verdict is stuck, it hit a usage limit, it was
    /// archived, or it has been idle past the reservation takeover window (30 minutes for agents, a day for personal
    /// sessions by default). Its active and queued reservations move to By in one transaction, keeping their keys,
    /// machines, expiry and queue places, and the takeover is recorded for both sessions. Both sessions are read and
    /// checked inside that transaction, so a report that archives either one meanwhile can't slip in between.
    /// </summary>
    public async Task<SessionTakeoverReply> TakeOverSessionAsync(SessionTakeoverCommand command, DateTimeOffset now,
        IEnumerable<SessionLink>? live = null, IEnumerable<SessionAskStep>? asks = null, CancellationToken token = default) {
        if (command.Reason is not null && !Label(command.Reason, 240)) throw new ArgumentException("Invalid takeover reason.");
        var policy = await ReadPolicyAsync(token);
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync((tx, ct) => TakeOverAsync(tx, command, policy, now, live ?? [], asks ?? [], ct), cancellationToken: token);
        } finally { _leaseGate.Release(); }
    }

    private async Task<SessionTakeoverReply> TakeOverAsync(SQLiteAsyncSession tx, SessionTakeoverCommand command, CoordinationPolicy policy,
        DateTimeOffset now, IEnumerable<SessionLink> live, IEnumerable<SessionAskStep> asks, CancellationToken token) {
        var fromMatches = await ResolveSessionAsync(tx, command.From, token);
        var byMatches = await ResolveSessionAsync(tx, command.By, token);
        if (fromMatches.Count != 1 || byMatches.Count != 1) {
            var ambiguous = fromMatches.Count > 1 ? fromMatches : byMatches.Count > 1 ? byMatches : null;
            return new(false, ambiguous is null ? "not-found" : "ambiguous") { From = command.From, By = command.By,
                Matches = ambiguous?.Select(m => m.Match).ToArray() };
        }
        var global = await ReadGlobalAsync(tx, token);
        async Task<LedgerItem> Load(string id) => ApplyGlobal(Read<LedgerItem>((string)(await tx.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
            new Dictionary<string, object?> { ["id"] = id }, token))!), global, now);
        var from = await Load(fromMatches[0].Item);
        var by = await Load(byMatches[0].Item);
        if (from.Session?.SourceSessionId is not { } fromId || by.Session?.SourceSessionId is not { } byId)
            return new(false, "not-found") { From = command.From, By = command.By };
        var (fromNative, byNative) = (fromId.ToString(), byId.ToString());
        if (from.Id == by.Id) return new(false, "same-session") { From = fromNative, By = byNative };
        bool Live(SessionFact? fact) => fact is { Archived: not true, LastActivityAtUtc: { } activity } &&
            now - activity <= TimeSpan.FromMinutes(policy.Lease.LiveWithinMinutes);
        if (!Live(by.Session)) return new(false, "by-not-live") { From = fromNative, By = byNative };
        // Reservations name a session, not a machine. If the same session is live on another machine, it isn't stopped.
        var twins = await tx.QueryAsListAsync("SELECT i.json FROM session_lookup s JOIN items i ON i.id=s.item WHERE s.provider=@provider AND s.native=@native AND s.item<>@item",
            row => Read<LedgerItem>(row.GetString(0)), new Dictionary<string, object?> { ["provider"] = from.Provider, ["native"] = fromNative, ["item"] = from.Id },
            cancellationToken: token);
        if (twins.Any(twin => Live(twin.Session))) return new(false, "from-live-elsewhere") { From = fromNative, By = byNative };
        var fact = from.Session;
        var verdict = rules.Evaluate(from, now);
        var idleMinutes = fact.Origin == "agent" ? policy.Lease.TakeoverIdleMinutes : policy.Lease.TakeoverIdleMinutesHuman;
        DateTimeOffset? idleAt = fact.LastActivityAtUtc?.AddMinutes(idleMinutes);
        var trigger = verdict.Value == "stuck" ? "stuck" : fact.UsageLimit ? "usage-limit" : fact.Archived == true ? "archived"
            : idleAt is { } eligible && eligible <= now ? "idle" : null;
        var children = await ChildrenAsync(tx, (from.Provider, fromNative), live, token);
        // An ask stays open until its result was accepted; a failed or cancelled one has nothing left to follow.
        var open = asks.Where(ask => ask.RequesterProvider == from.Provider && Native(ask.RequesterSessionId) == fromNative &&
            ask.State is not ("failed" or "cancelled") && !(ask.State == "succeeded" && ask.Stages.ContainsKey("accepted")))
            .Select(ask => ask.RequestId).ToArray();
        // Work From handed away stays with whoever continues it. If it came back to From, From holds it again; work From
        // took from somebody else doesn't count as its own coming back.
        var takeovers = await TakeoversAsync(tx, token);
        var continuation = Continuation(takeovers, from.Provider, fromNative);
        var earlier = continuation?.Row;
        if (earlier is not null && earlier.ByProvider == by.Provider && earlier.ByNative == byNative)
            return Describe(new(true, "already-taken-over") { Trigger = earlier.Trigger, AtUtc = earlier.AtUtc,
                Leases = (await ReservationsAsync(tx, now, token)).Where(row => continuation!.Value.Leases.Contains(row.Lease.LeaseId))
                    .Select(row => row.Lease).ToArray() });
        if (earlier is not null) {
            // Someone else already continues this work and holds what was moved. While they are live it stays theirs;
            // once they stop too, the work is taken over from them, not from the original session.
            var current = (await SessionItemsAsync(tx, [(earlier.ByProvider, earlier.ByNative)], now, token)).Values.SingleOrDefault()?.Session;
            return new(false, Live(current) ? "taken-over-by-other" : "take-over-successor") {
                From = fromNative, By = earlier.ByNative, AtUtc = earlier.AtUtc, Trigger = earlier.Trigger };
        }
        if (trigger is null) return new(false, "not-eligible") { From = fromNative, By = byNative, Verdict = verdict.Value, EligibleAtUtc = idleAt };
        var holder = by.Provider + ":" + byNative;
        var all = await ReservationsAsync(tx, now, token);
        var moved = new List<Lease>();
        // Reconcile each key and machine as a group with what By already has: every active slot stays and moves, and a
        // queued row survives only when the new holder has no active slot there, the earliest one in line.
        var mine = all.Where(row => row.State is "active" or "queued" && IsHolder(from, row.Lease.Holder)).ToArray();
        var keep = new List<Reservation>();
        foreach (var group in mine.GroupBy(row => (row.Lease.Key, row.Lease.Machine))) {
            var theirs = all.Where(other => other.Lease.Key == group.Key.Key && other.Lease.Machine == group.Key.Machine &&
                other.State is "active" or "queued" && IsHolder(by, other.Lease.Holder)).ToArray();
            var waiting = group.Concat(theirs).Any(row => row.State == "active") ? null
                : group.Concat(theirs).Where(row => row.State == "queued").MinBy(row => row.Order);
            foreach (var row in group.Concat(theirs).Where(row => row.State == "queued" && row != waiting))
                await tx.ExecuteNonQueryAsync("DELETE FROM reservations WHERE id=@id", new Dictionary<string, object?> { ["id"] = row.Lease.LeaseId }, token);
            keep.AddRange(group.Where(row => row.State == "active" || row == waiting));
        }
        foreach (var row in keep) {
            // A new id fences the stopped holder. An active reservation's history ends here and the new holder's starts
            // here, so benchmark budgets count the time once; the row keeps its place in the queue.
            if (row.State == "active") await RememberLeaseAsync(tx, row.Lease, now, token);
            var lease = row.Lease with { Holder = holder, LeaseId = Guid.NewGuid().ToString("N"), PreviousHolder = row.Lease.Holder,
                TakenOverAtUtc = now, PreviousLeaseId = row.Lease.LeaseId, AcquiredAtUtc = row.State == "active" ? now : row.Lease.AcquiredAtUtc };
            await tx.ExecuteNonQueryAsync("UPDATE reservations SET id=@new,holder=@holder,json=@json WHERE id=@old", new Dictionary<string, object?> {
                ["new"] = lease.LeaseId, ["holder"] = lease.Holder, ["json"] = Json(lease), ["old"] = row.Lease.LeaseId }, token);
            if (row.State == "active") await RememberLeaseAsync(tx, lease, lease.ExpiresAtUtc, token);
            moved.Add(lease);
        }
        var record = new TakeoverRow(from.Provider, fromNative, by.Provider, byNative, now, trigger, command.Reason, moved.Select(lease => lease.Key).ToArray()) {
            Moves = moved.Select(lease => new LeaseMove(lease.PreviousLeaseId!, lease.LeaseId)).ToArray()
        };
        await tx.ExecuteNonQueryAsync("""
            INSERT INTO session_takeovers(from_provider,from_native,by_provider,by_native,recorded,json)
            VALUES(@fromProvider,@from,@byProvider,@by,@recorded,@json)
            """, new Dictionary<string, object?> { ["fromProvider"] = record.FromProvider, ["from"] = record.FromNative,
                ["byProvider"] = record.ByProvider, ["by"] = record.ByNative, ["recorded"] = now.ToString("O"), ["json"] = Json(record) }, token);
        return Describe(new(true, "taken-over") { Trigger = trigger, AtUtc = now, Leases = moved.ToArray() });

        SessionTakeoverReply Describe(SessionTakeoverReply reply) {
            var repo = fact.Repository;
            return reply with { From = fromNative, By = byNative, Task = from.Scope ?? fact.Scope,
                Title = fact.Title ?? repo?.Title, Verdict = verdict.Value, Repo = repo?.Repo, Branch = repo?.Branch,
                Worktree = repo?.Worktree, Commit = repo?.Commit,
                PullRequests = repo?.PullRequests is { Length: > 0 } prs ? prs : null,
                Children = children.Length == 0 ? null : children, OpenAsks = open.Length == 0 ? null : open };
        }
    }
}
