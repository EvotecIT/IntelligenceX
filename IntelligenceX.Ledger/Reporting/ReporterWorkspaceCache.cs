using System.Diagnostics;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Reads each current workspace once per minute and attaches Git metadata to existing session facts.</summary>
internal sealed class ReporterWorkspaceCache {
    private sealed record Workspace(DateTimeOffset CheckedAtUtc, string? Branch, string? Root, string[]? Files, bool Capped, string? BaseRef, string State, int? FileCount = null);
    private readonly Dictionary<string, Workspace> _workspaces = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Provider, string Id, string Repo), (string[] Urls, bool Known)> _pullRequests = new();
    private readonly object _gate = new();

    internal async Task RefreshAsync(IEnumerable<AgentSessionState> sessions, DateTimeOffset now, CancellationToken token) {
        var paths = sessions.Where(s => s.Repository is not null && s.WorkingDirectory is not null &&
            (s.TurnOpen == true || s.LastActivityAtUtc >= now.AddDays(-1))).Select(s => s.WorkingDirectory!).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var path in paths) {
            lock (_gate) { if (_workspaces.TryGetValue(path, out var old) && now - old.CheckedAtUtc < TimeSpan.FromMinutes(1)) continue; }
            var observed = await ReadAsync(path, now, token);
            lock (_gate) _workspaces[path] = observed;
        }
        var identities = sessions.Select(session => (session.Provider, session.Id, session.Repository ?? "")).ToHashSet();
        lock (_gate) {
            foreach (var old in _workspaces.Keys.Except(paths, StringComparer.Ordinal).ToArray()) _workspaces.Remove(old);
            foreach (var old in _pullRequests.Keys.Except(identities).ToArray()) _pullRequests.Remove(old);
        }
    }

    internal RepositoryObservation? For(AgentSessionState session, bool pullRequestsKnown = false) {
        if (session.Repository is null || session.TurnOpen != true &&
            (session.LastActivityAtUtc is not { } activity || activity < DateTimeOffset.UtcNow.AddDays(-1))) return null;
        Workspace? workspace;
        (string[] Urls, bool Known) pullRequests;
        lock (_gate) {
            _workspaces.TryGetValue(session.WorkingDirectory ?? "", out workspace);
            var key = (session.Provider, session.Id, session.Repository);
            if (!_pullRequests.TryGetValue(key, out pullRequests) || pullRequestsKnown) {
                pullRequests = (session.PullRequests.Where(pr => pr.IsOpen).Select(pr => pr.Url)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(10).ToArray(), pullRequestsKnown);
                _pullRequests[key] = pullRequests;
            }
        }
        return new(session.Repository, workspace?.Branch ?? session.Branch, workspace?.Root ?? session.WorkingDirectory,
            session.Title is { Length: > 240 } title ? title[..240] : session.Title,
            pullRequests.Urls, workspace?.Files?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            workspace?.Capped ?? false, workspace?.BaseRef, workspace?.State ?? "not-observed", pullRequests.Known ? true : null, workspace?.FileCount);
    }

    private static async Task<Workspace> ReadAsync(string path, DateTimeOffset now, CancellationToken token) {
        if (!Directory.Exists(path)) return new(now, null, null, null, false, null, "workspace-missing");
        var root = (await GitAsync(path, ["rev-parse", "--show-toplevel"], token))?.Trim();
        if (string.IsNullOrEmpty(root)) return new(now, null, null, null, false, null, "git-unavailable");
        var branch = (await GitAsync(root, ["symbolic-ref", "--quiet", "--short", "HEAD"], token))?.Trim();
        var defaultBranch = (await GitAsync(root, ["symbolic-ref", "--quiet", "--short", "refs/remotes/origin/HEAD"], token))?.Trim();
        string? baseRef = null, baseCommit = null;
        foreach (var candidate in new[] { defaultBranch, "origin/main", "origin/master", "main", "master" }.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal)) {
            var commit = (await GitAsync(root, ["merge-base", "HEAD", candidate!], token))?.Trim();
            if (commit is { Length: 40 or 64 } && commit.All(Uri.IsHexDigit)) { baseRef = candidate; baseCommit = commit; break; }
        }
        if (baseCommit is null) return new(now, branch, root, null, false, null, "base-unknown");
        var tracked = await GitAsync(root, ["diff", "--no-ext-diff", "--name-only", "-z", baseCommit, "--"], token);
        var untracked = await GitAsync(root, ["ls-files", "--others", "--exclude-standard", "-z"], token);
        if (tracked is null || untracked is null) return new(now, branch, root, null, false, baseRef, "git-unavailable");
        var files = (tracked + untracked).Split('\0', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(now, branch, root, files.Take(20).ToArray(), files.Length > 20, baseRef, "observed", files.Length);
    }

    private static async Task<string?> GitAsync(string directory, string[] arguments, CancellationToken token) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(3));
        using var process = new Process { StartInfo = new("git") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true } };
        process.StartInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var arg in new[] { "--no-optional-locks", "-c", "core.fsmonitor=false", "-C", directory }.Concat(arguments)) process.StartInfo.ArgumentList.Add(arg);
        Task<string>? output = null, errorOutput = null;
        try {
            process.Start();
            output = BoundedOutputAsync(process.StandardOutput, deadline.Token);
            errorOutput = BoundedOutputAsync(process.StandardError, deadline.Token);
            await process.WaitForExitAsync(deadline.Token);
            var text = await output;
            await errorOutput;
            return process.ExitCode == 0 ? text : null;
        } catch (Exception error) when (!token.IsCancellationRequested && error is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) {
            return null;
        } finally {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
            if (output is not null && errorOutput is not null) {
                try { await Task.WhenAll(output, errorOutput); }
                catch (Exception error) when (error is IOException or OperationCanceledException) { }
            }
        }
    }

    private static async Task<string> BoundedOutputAsync(StreamReader reader, CancellationToken token) {
        var result = new System.Text.StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, token)) != 0) {
            if (result.Length + count > 1024 * 1024) throw new IOException("Git metadata exceeded its observation bound.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}
