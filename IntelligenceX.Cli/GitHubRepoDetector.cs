using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace IntelligenceX.Cli;

internal static class GitHubRepoDetector {
    internal static string? TryDetectRepo(string workspace, Action<string>? debug = null) {
        var fromEnv = TryDetectRepoFromEnvironment();
        if (!string.IsNullOrWhiteSpace(fromEnv)) {
            return fromEnv;
        }

        if (string.IsNullOrWhiteSpace(workspace) || !Directory.Exists(workspace)) {
            return null;
        }

        // Prefer calling git for correct behavior across worktrees/submodules (.git can be a file with gitdir:).
        var url = TryGetRemoteUrlFromGit(workspace, "origin", debug)
               ?? TryGetRemoteUrlFromGitConfig(workspace, "origin", debug);
        return string.IsNullOrWhiteSpace(url) ? null : ParseRepoFromRemoteUrl(url!);
    }

    internal static string? TryDetectRepoFromEnvironment() {
        var envRepo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO")
                   ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(envRepo) && envRepo.Contains('/')) {
            return envRepo.Trim();
        }
        return null;
    }

    internal static string? ParseRepoFromRemoteUrl(string url) {
        if (string.IsNullOrWhiteSpace(url)) {
            return null;
        }

        var u = url.Trim();

        // git@host:owner/repo(.git)
        var scpLike = Regex.Match(u, @"^git@(?<host>[^:]+):(?<path>.+)$", RegexOptions.IgnoreCase);
        if (scpLike.Success) {
            return ParseRepoFromPath(scpLike.Groups["path"].Value);
        }

        // ssh://git@host/owner/repo(.git) or https://host/owner/repo(.git)
        if (Uri.TryCreate(u, UriKind.Absolute, out var uri)) {
            // Path is "/owner/repo(.git)" or "/some/prefix/owner/repo(.git)" (rare). Prefer last 2 segments.
            var path = uri.AbsolutePath.Trim('/');
            return ParseRepoFromPath(path);
        }

        return null;
    }

    internal static string? TryReadRemoteUrlFromGitConfigText(string configText, string remoteName) {
        if (string.IsNullOrWhiteSpace(configText) || string.IsNullOrWhiteSpace(remoteName)) {
            return null;
        }

        var wanted = remoteName.Trim();
        var inRemote = false;
        using var reader = new StringReader(configText);
        string? line;
        while ((line = reader.ReadLine()) is not null) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || trimmed.StartsWith(";", StringComparison.Ordinal)) {
                continue;
            }

            // Any section header changes parsing scope.
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) {
                inRemote = IsRemoteSection(trimmed, wanted);
                continue;
            }

            if (!inRemote) {
                continue;
            }

            // url = <value>
            if (trimmed.StartsWith("url", StringComparison.OrdinalIgnoreCase)) {
                var idx = trimmed.IndexOf('=', StringComparison.Ordinal);
                if (idx < 0) {
                    continue;
                }
                var value = trimmed.Substring(idx + 1).Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private static bool IsRemoteSection(string sectionHeader, string remoteName) {
        // [remote "origin"]
        // Keep parsing simple: tolerate extra whitespace.
        var header = sectionHeader.Trim();
        var needle = $"[remote \"{remoteName}\"]";
        return header.Equals(needle, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseRepoFromPath(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        var p = path.Trim();
        if (p.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
            p = p.Substring(0, p.Length - 4);
        }
        // Prefer last two segments to tolerate prefixes.
        var parts = p.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) {
            return null;
        }
        var owner = parts[^2];
        var repo = parts[^1];
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)) {
            return null;
        }
        return $"{owner}/{repo}";
    }

    private static string? TryGetRemoteUrlFromGit(string workspace, string remoteName, Action<string>? debug) {
        try {
            var psi = new ProcessStartInfo {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-C");
            psi.ArgumentList.Add(workspace);
            psi.ArgumentList.Add("remote");
            psi.ArgumentList.Add("get-url");
            psi.ArgumentList.Add(remoteName);

            using var proc = Process.Start(psi);
            if (proc is null) {
                return null;
            }
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(2500);
            if (proc.ExitCode != 0) {
                if (!string.IsNullOrWhiteSpace(stderr)) {
                    debug?.Invoke($"git remote get-url failed: {stderr.Trim()}");
                }
                return null;
            }
            var url = (stdout ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(url) ? null : url;
        } catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) {
            debug?.Invoke($"git invocation failed: {ex.Message}");
            return null;
        }
    }

    private static string? TryGetRemoteUrlFromGitConfig(string workspace, string remoteName, Action<string>? debug) {
        try {
            var gitDir = ResolveDotGitDir(workspace, debug);
            if (string.IsNullOrWhiteSpace(gitDir)) {
                return null;
            }
            var configPath = Path.Combine(gitDir, "config");
            if (!File.Exists(configPath)) {
                return null;
            }
            var text = File.ReadAllText(configPath, Encoding.UTF8);
            return TryReadRemoteUrlFromGitConfigText(text, remoteName);
        } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) {
            debug?.Invoke($"git config read failed: {ex.Message}");
            return null;
        }
    }

    private static string? ResolveDotGitDir(string workspace, Action<string>? debug) {
        var gitPath = Path.Combine(workspace, ".git");
        if (Directory.Exists(gitPath)) {
            return gitPath;
        }
        if (!File.Exists(gitPath)) {
            return null;
        }

        // Worktrees/submodules can use a file `.git` containing: gitdir: /path/to/actual/dir
        try {
            var text = File.ReadAllText(gitPath, Encoding.UTF8).Trim();
            const string prefix = "gitdir:";
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            var dir = text.Substring(prefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(dir)) {
                return null;
            }
            if (!Path.IsPathRooted(dir)) {
                dir = Path.GetFullPath(Path.Combine(workspace, dir));
            }
            return Directory.Exists(dir) ? dir : null;
        } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
            debug?.Invoke($"gitdir pointer read failed: {ex.Message}");
            return null;
        }
    }
}
