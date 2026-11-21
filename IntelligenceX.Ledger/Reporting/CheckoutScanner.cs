using System.Text.RegularExpressions;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>
/// Lists every checkout under the machine's repository folders: main checkouts, their worktrees wherever they live,
/// branches nobody pushed, stashes and uncommitted files. It reads local Git state only and never fetches.
/// </summary>
internal static partial class CheckoutScanner {
    internal const int MaximumRoots = 16, MaximumRepositories = 400, MaximumCheckouts = 500, MaximumUnpushed = 20;
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
        var truncated = false;
        var repositories = roots.SelectMany(Children).Distinct(PathComparer).ToArray();
        if (repositories.Length > MaximumRepositories) { truncated = true; repositories = repositories[..MaximumRepositories]; }
        foreach (var repository in repositories) {
            if (budget.IsCancellationRequested || checkouts.Count >= MaximumCheckouts) { truncated = true; break; }
            try { checkouts.AddRange(await RepositoryAsync(repository, budget.Token)); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { truncated = true; break; }
        }
        if (checkouts.Count > MaximumCheckouts) { truncated = true; checkouts = checkouts[..MaximumCheckouts]; }
        return new(now, roots, checkouts.ToArray(), truncated);
    }

    private static IEnumerable<string> Children(string root) {
        try {
            return Directory.EnumerateDirectories(root).Where(child => Directory.Exists(Path.Combine(child, ".git")))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return []; }
    }

    private static async Task<CheckoutObservation[]> RepositoryAsync(string main, CancellationToken token) {
        if (RepoFromRemote((await GitAsync(main, ["remote", "get-url", "origin"], token))?.Trim()) is not { } repo) return [];
        var list = await GitAsync(main, ["worktree", "list", "--porcelain"], token);
        if (list is null) return [new(repo, main, "git-unavailable", true)];
        var unpushed = Unpushed(await GitAsync(main, ["log", "--branches", "--not", "--remotes", "--simplify-by-decoration",
            "--decorate-refs=refs/heads/", "--format=%D", "-n", "200"], token));
        var stash = await GitAsync(main, ["stash", "list", "--format=%gd"], token);
        var stashes = stash?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        var result = new List<CheckoutObservation>();
        foreach (var (entry, index) in Worktrees(list).Select((entry, index) => (entry, index))) {
            var primary = index == 0;
            var observation = entry.Prunable || !Directory.Exists(entry.Path)
                ? new CheckoutObservation(repo, entry.Path, "missing", primary, entry.Branch, entry.Commit)
                : await CheckoutAsync(repo, entry.Path, primary, entry.Branch, entry.Commit, token);
            if (primary) observation = observation with { Stashes = stashes, UnpushedBranches = unpushed };
            result.Add(observation);
        }
        // A main checkout always lists itself first; keep the repository facts even if that line could not be read.
        if (!result.Any(r => r.Primary)) result.Insert(0, new(repo, main, "git-unavailable", true) { Stashes = stashes, UnpushedBranches = unpushed });
        return result.ToArray();
    }

    private static async Task<CheckoutObservation> CheckoutAsync(string repo, string path, bool primary, string? branch, string? commit, CancellationToken token) {
        var status = await GitAsync(path, ["status", "--porcelain=v2", "--branch", "-z", "--untracked-files=normal"], token);
        if (status is null) return new(repo, path, "git-unavailable", primary, branch, commit);
        string? upstream = null;
        int? ahead = null, behind = null;
        int tracked = 0, untracked = 0;
        foreach (var line in status.Split('\0', StringSplitOptions.RemoveEmptyEntries)) {
            if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal)) upstream = line[18..];
            else if (line.StartsWith("# branch.ab ", StringComparison.Ordinal) && AheadBehind().Match(line) is { Success: true } match) {
                ahead = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                behind = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            } else if (line.StartsWith("? ", StringComparison.Ordinal)) untracked++;
            else if (line.Length > 2 && line[1] == ' ' && line[0] is '1' or '2' or 'u') tracked++;
        }
        var changed = 0;
        // Status counts files whose only change is line endings. Ask diff, which can ignore them, before reporting any.
        if (tracked > 0) {
            var names = await GitAsync(path, ["diff", "--no-ext-diff", "--name-only", "--ignore-cr-at-eol", "HEAD", "--"], token);
            changed = names?.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length ?? tracked;
        }
        return new(repo, path, "observed", primary, branch, commit, upstream, ahead, behind, changed, untracked);
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

    internal static string[] Unpushed(string? decorations) {
        if (decorations is null) return [];
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
        var name = parts[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[^1][..^4] : parts[^1];
        var repo = parts[^2] + "/" + name;
        return RepositoryOverlapEvaluator.NormalizeRepo(host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? repo : host + "/" + repo);
    }

    private static Task<string?> GitAsync(string directory, string[] arguments, CancellationToken token) =>
        ReporterWorkspaceCache.GitAsync(directory, arguments, token, GitTimeout);


    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    [GeneratedRegex(@"^# branch\.ab \+(\d+) -(\d+)$")] private static partial Regex AheadBehind();
    [GeneratedRegex(@"^(?:[^@/]+@)?(?<host>[^:/\\]{2,}):(?<path>[^/].*)$")] private static partial Regex ScpRemote();
}
