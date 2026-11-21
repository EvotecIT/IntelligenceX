using System.Text.RegularExpressions;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>
/// Lists every checkout under the machine's repository folders: main checkouts, their worktrees wherever they live,
/// branches nobody pushed, stashes and uncommitted files. It reads local Git state only and never fetches.
/// </summary>
internal static partial class CheckoutScanner {
    internal const int MaximumRoots = 16, MaximumRepositories = 400, MaximumCheckouts = 500, MaximumUnpushed = 20;
    /// <summary>Well under the service's 1 MiB request limit, leaving room for the rest of the health report.</summary>
    internal const int MaximumCensusBytes = 512 * 1024;
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(15), Budget = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Configured folders first, then the folders holding the main checkouts that agent sessions work in, so a machine
    /// needs no setting to be scanned.
    /// </summary>
    internal static async Task<string[]> RootsAsync(IEnumerable<string> configured, IEnumerable<string> sessionDirectories, CancellationToken token) {
        var roots = new List<string>();
        void Add(string? path) {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!roots.Contains(full, PathComparer)) roots.Add(full);
        }
        foreach (var path in configured) Add(path);
        foreach (var directory in sessionDirectories.Where(Directory.Exists).Distinct(PathComparer).Take(200)) {
            if (roots.Count >= MaximumRoots) break;
            var common = (await ReporterWorkspaceCache.GitAsync(directory, ["rev-parse", "--path-format=absolute", "--git-common-dir"], token))?.Trim();
            // A main checkout keeps its repository in <checkout>/.git, so the folder holding it is two levels up.
            if (string.IsNullOrEmpty(common) || !string.Equals(Path.GetFileName(common), ".git", StringComparison.OrdinalIgnoreCase)) continue;
            Add(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(common))));
        }
        return roots.Take(MaximumRoots).ToArray();
    }

    internal static async Task<CheckoutCensus> ScanAsync(string[] roots, DateTimeOffset now, CancellationToken token) {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(Budget);
        var checkouts = new List<CheckoutObservation>();
        // An incomplete scan only adds to what the service knows; it never makes a missing checkout look gone.
        var truncated = false;
        var repositories = new List<string>();
        foreach (var root in roots) {
            if (Children(root) is { } children) repositories.AddRange(children);
            else truncated = true;
        }
        var unique = repositories.Distinct(PathComparer).ToArray();
        if (unique.Length > MaximumRepositories) { truncated = true; unique = unique[..MaximumRepositories]; }
        foreach (var repository in unique) {
            if (budget.IsCancellationRequested || checkouts.Count >= MaximumCheckouts) { truncated = true; break; }
            try {
                var (rows, complete) = await RepositoryAsync(repository, budget.Token);
                checkouts.AddRange(rows);
                truncated |= !complete;
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) { truncated = true; break; }
        }
        // A row the service would refuse would make it refuse the whole health report, so leave such rows out here.
        var valid = checkouts.Where(LedgerStore.ValidCheckout).Take(MaximumCheckouts).ToList();
        truncated |= valid.Count != checkouts.Count;
        var census = new CheckoutCensus(now, roots, valid.ToArray(), truncated);
        while (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(census, LedgerJson.Options).Length > MaximumCensusBytes && census.Checkouts.Length > 0)
            census = census with { Checkouts = census.Checkouts[..(census.Checkouts.Length * 3 / 4)], Truncated = true };
        return census;
    }

    /// <summary>Repositories directly under a root, or null when the root could not be read.</summary>
    private static string[]? Children(string root) {
        try {
            return Directory.EnumerateDirectories(root).Where(child => Directory.Exists(Path.Combine(child, ".git")))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>The repository's checkouts, and whether every Git read needed to decide its membership succeeded.</summary>
    private static async Task<(CheckoutObservation[] Rows, bool Complete)> RepositoryAsync(string main, CancellationToken token) {
        var remotes = await GitAsync(main, ["remote"], token);
        if (remotes is null) return ([], false);
        // No origin is a fact, not a failure: such repositories are not listed.
        if (!remotes.Split('\n', StringSplitOptions.TrimEntries).Contains("origin", StringComparer.Ordinal)) return ([], true);
        var url = await GitAsync(main, ["remote", "get-url", "origin"], token);
        if (url is null) return ([], false);
        if (RepoFromRemote(url.Trim()) is not { } repo) return ([], true);
        var list = await GitAsync(main, ["worktree", "list", "--porcelain"], token);
        // Without the worktree list the scan cannot say which worktrees exist, so it is incomplete.
        if (list is null) return ([new(repo, main, "git-unavailable", true)], false);
        var log = await GitAsync(main, ["log", "--branches", "--not", "--remotes", "--simplify-by-decoration",
            "--decorate-refs=refs/heads/", "--format=%D", "-n", "200"], token);
        var stash = await GitAsync(main, ["stash", "list", "--format=%gd"], token);
        var unpushed = log is null ? null : Unpushed(log);
        var stashes = stash?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var result = new List<CheckoutObservation>();
        foreach (var (entry, index) in Worktrees(list).Select((entry, index) => (entry, index))) {
            var primary = index == 0;
            var observation = entry.Prunable || !Directory.Exists(entry.Path)
                ? new CheckoutObservation(repo, entry.Path, "missing", primary, entry.Branch, entry.Commit)
                : await CheckoutAsync(repo, entry.Path, primary, entry.Branch, entry.Commit, token);
            if (primary) observation = RepositoryFacts(observation, unpushed, stashes);
            result.Add(observation);
        }
        // A main checkout always lists itself first; keep the repository facts even if that line could not be read.
        if (!result.Any(r => r.Primary)) result.Insert(0, RepositoryFacts(new(repo, main, "git-unavailable", true), unpushed, stashes));
        return (result.ToArray(), true);
    }

    // Repository-wide facts that could not be read leave the main checkout partial rather than looking clean.
    private static CheckoutObservation RepositoryFacts(CheckoutObservation row, string[]? unpushed, int? stashes) => row with {
        UnpushedBranches = unpushed, Stashes = stashes,
        State = row.State == "observed" && (unpushed is null || stashes is null) ? "partial" : row.State
    };

    private static async Task<CheckoutObservation> CheckoutAsync(string repo, string path, bool primary, string? branch, string? commit, CancellationToken token) {
        var status = await GitAsync(path, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=all"], token);
        if (status is null) return new(repo, path, "git-unavailable", primary, branch, commit);
        string? upstream = null;
        int? ahead = null, behind = null;
        int tracked = 0, untracked = 0;
        var records = status.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < records.Length; i++) {
            var line = records[i];
            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal)) upstream = line[18..];
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal) && AheadBehind().Match(line) is { Success: true } match) {
                ahead = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                behind = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            } else if (line.StartsWith("? ", StringComparison.Ordinal)) untracked++;
            else if (line.StartsWith("1 ", StringComparison.Ordinal) || line.StartsWith("u ", StringComparison.Ordinal)) tracked++;
            // A rename or copy record is followed by its original path as a separate record.
            else if (line.StartsWith("2 ", StringComparison.Ordinal)) { tracked++; i++; }
        }
        int changed = 0;
        var state = "observed";
        // Status counts files whose only change is line endings. Ask diff, which can ignore them, before reporting any.
        if (tracked > 0) {
            var names = await GitAsync(path, ["diff", "--no-ext-diff", "--name-only", "--ignore-cr-at-eol", "HEAD", "--"], token);
            if (names is null) { changed = tracked; state = "partial"; }
            else changed = names.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        }
        return new(repo, path, state, primary, branch, commit, upstream, ahead, behind, changed, untracked);
    }
    internal sealed record WorktreeEntry(string Path, string? Commit, string? Branch, bool Prunable);

    /// <summary>Parses <c>git worktree list --porcelain</c>. Git lists the main checkout first.</summary>
    internal static IEnumerable<WorktreeEntry> Worktrees(string porcelain) {
        string? path = null, commit = null, branch = null;
        bool prunable = false, bare = false;
        foreach (var raw in porcelain.Replace("\r", "").Split('\n').Append("")) {
            if (raw.Length == 0) {
                if (path is not null && !bare) yield return new(Path.GetFullPath(path), commit, branch, prunable);
                path = commit = branch = null; prunable = bare = false;
            } else if (raw.StartsWith("worktree ", StringComparison.Ordinal)) path = raw[9..];
            else if (raw.StartsWith("HEAD ", StringComparison.Ordinal)) commit = raw[5..];
            else if (raw.StartsWith("branch refs/heads/", StringComparison.Ordinal)) branch = raw[18..];
            else if (raw.StartsWith("prunable", StringComparison.Ordinal)) prunable = true;
            else if (raw == "bare") bare = true;
        }
    }

    internal static string[] Unpushed(string decorations) {
        return decorations.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(line => line.Split(", ", StringSplitOptions.RemoveEmptyEntries))
            .Select(name => name.StartsWith("HEAD -> ", StringComparison.Ordinal) ? name[8..] : name.Trim())
            .Where(name => name.Length > 0 && name != "HEAD")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaximumUnpushed).ToArray();
    }

    /// <summary>owner/name from an HTTPS, SSH or scp-style GitHub remote. Other hosts keep host/owner/name.</summary>
    internal static string? RepoFromRemote(string? url) {
        if (string.IsNullOrWhiteSpace(url)) return null;
        string host, path;
        if (ScpRemote().Match(url) is { Success: true } scp) { host = scp.Groups["host"].Value; path = scp.Groups["path"].Value; }
        else if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0) { host = uri.Host; path = uri.AbsolutePath; }
        else return null;
        var parts = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        if (parts[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)) parts[^1] = parts[^1][..^4];
        var repo = string.Join('/', parts);
        // GitHub repositories are owner/name, as sessions report them. Other hosts keep every namespace level.
        return RepositoryOverlapEvaluator.NormalizeRepo(host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? parts[0] + "/" + parts[1] : host + "/" + repo);
    }

    private static Task<string?> GitAsync(string directory, string[] arguments, CancellationToken token) =>
        ReporterWorkspaceCache.GitAsync(directory, arguments, token, GitTimeout);


    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [GeneratedRegex(@"^# branch\.ab \+(\d+) -(\d+)$")] private static partial Regex AheadBehind();
    [GeneratedRegex(@"^(?:[^@/]+@)?(?<host>[^:/\\]{2,}):(?<path>[^/].*)$")] private static partial Regex ScpRemote();
}
