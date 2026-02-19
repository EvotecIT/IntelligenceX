using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Cli.ReleaseNotes;

internal static partial class ReleaseNotesRunner {
    private static string? _temporaryAuthPathFromEnv;

    private static string NormalizeReleaseNotes(string raw, out bool hasChanges) {
        var summary = new List<string>();
        var added = new List<string>();
        var changed = new List<string>();
        var fixedItems = new List<string>();

        var section = ReleaseSection.None;
        var changeSection = ChangeSection.None;

        var lines = raw.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines) {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) {
                continue;
            }

            if (IsSummaryHeading(trimmed)) {
                section = ReleaseSection.Summary;
                changeSection = ChangeSection.None;
                continue;
            }
            if (IsChangesHeading(trimmed)) {
                section = ReleaseSection.Changes;
                changeSection = ChangeSection.None;
                continue;
            }

            if (section == ReleaseSection.Changes) {
                var parsed = TryParseChangeSection(trimmed);
                if (parsed.HasValue) {
                    changeSection = parsed.Value;
                    continue;
                }
            }

            if (!IsBullet(trimmed)) {
                continue;
            }

            var item = trimmed.TrimStart('-', '*').Trim();
            if (string.IsNullOrWhiteSpace(item)) {
                continue;
            }

            if (section == ReleaseSection.Summary) {
                summary.Add(item);
            } else if (section == ReleaseSection.Changes && changeSection != ChangeSection.None) {
                GetBucket(changeSection, added, changed, fixedItems).Add(item);
            }
        }

        hasChanges = added.Count > 0 || changed.Count > 0 || fixedItems.Count > 0;

        var output = new StringBuilder();
        output.AppendLine("## Summary");
        if (summary.Count == 0) {
            output.AppendLine("- No summary provided.");
        } else {
            foreach (var item in summary) {
                output.AppendLine($"- {item}");
            }
        }

        output.AppendLine("## Changes");
        AppendChangeSection(output, "Added", added);
        AppendChangeSection(output, "Changed", changed);
        AppendChangeSection(output, "Fixed", fixedItems);

        return output.ToString().TrimEnd();
    }

    private static void AppendChangeSection(StringBuilder output, string label, List<string> items) {
        output.AppendLine($"- {label}:");
        if (items.Count == 0) {
            output.AppendLine("  - None.");
            return;
        }
        foreach (var item in items) {
            output.AppendLine($"  - {item}");
        }
    }

    private static bool IsSummaryHeading(string line) {
        return Regex.IsMatch(line, "^#{1,6}\\s*Summary\\b", RegexOptions.IgnoreCase);
    }

    private static bool IsChangesHeading(string line) {
        return Regex.IsMatch(line, "^#{1,6}\\s*Changes\\b", RegexOptions.IgnoreCase);
    }

    private static ChangeSection? TryParseChangeSection(string line) {
        if (Regex.IsMatch(line, "^(?:#{1,6}\\s*)?Added\\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(line, "^[-*]\\s*Added\\s*:", RegexOptions.IgnoreCase)) {
            return ChangeSection.Added;
        }
        if (Regex.IsMatch(line, "^(?:#{1,6}\\s*)?Changed\\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(line, "^[-*]\\s*Changed\\s*:", RegexOptions.IgnoreCase)) {
            return ChangeSection.Changed;
        }
        if (Regex.IsMatch(line, "^(?:#{1,6}\\s*)?Fixed\\b", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(line, "^[-*]\\s*Fixed\\s*:", RegexOptions.IgnoreCase)) {
            return ChangeSection.Fixed;
        }
        return null;
    }

    private static bool IsBullet(string line) {
        return line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal);
    }

    private static List<string> GetBucket(ChangeSection section, List<string> added, List<string> changed, List<string> fixedItems) {
        return section switch {
            ChangeSection.Added => added,
            ChangeSection.Changed => changed,
            ChangeSection.Fixed => fixedItems,
            _ => added
        };
    }

    private static string? ResolveChangelogPath(ReleaseNotesOptions options, string repoPath) {
        if (!string.IsNullOrWhiteSpace(options.ChangelogPath)) {
            return options.ChangelogPath;
        }
        if (options.UpdateChangelog) {
            return Path.Combine(repoPath, "CHANGELOG.md");
        }
        return null;
    }

    private static void UpdateChangelog(string path, string content, string? versionLabel) {
        var version = string.IsNullOrWhiteSpace(versionLabel) ? "Unreleased" : versionLabel!.Trim();
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var header = $"## {version} - {date}";
        var section = header + Environment.NewLine + Environment.NewLine + content.Trim() + Environment.NewLine + Environment.NewLine;

        if (!File.Exists(path)) {
            var initial = "# Changelog" + Environment.NewLine + Environment.NewLine + section;
            File.WriteAllText(path, initial);
            return;
        }

        var existing = File.ReadAllText(path);
        if (existing.Contains(header, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Changelog already contains section: {header}");
        }

        var newline = existing.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalizedSection = section.Replace(Environment.NewLine, newline);

        if (existing.StartsWith("#", StringComparison.Ordinal)) {
            var firstLineEnd = existing.IndexOf(newline, StringComparison.Ordinal);
            if (firstLineEnd >= 0) {
                var insertAt = firstLineEnd + newline.Length;
                if (insertAt < existing.Length && existing.AsSpan(insertAt).StartsWith(newline, StringComparison.Ordinal)) {
                    insertAt += newline.Length;
                }
                var updated = existing.Insert(insertAt, normalizedSection);
                File.WriteAllText(path, updated);
                return;
            }
        }

        File.WriteAllText(path, normalizedSection + existing);
    }

    private static async Task HandleGitOutputAsync(string repoPath, string? changelogPath, string versionLabel, ReleaseNotesOptions options) {
        if (!HasGitChanges(repoPath)) {
            Console.WriteLine("No git changes detected. Skipping commit/PR.");
            return;
        }

        EnsureGitIdentity(repoPath);
        var commitMessage = $"Release notes for {versionLabel}";

        if (options.CreatePr) {
            var repoSlug = ResolveRepoSlug(options);
            if (!TryParseRepoSlug(repoSlug, out var owner, out var repo)) {
                throw new InvalidOperationException("Missing or invalid --repo-slug (expected owner/name).");
            }
            var token = ResolveGitHubToken();
            if (string.IsNullOrWhiteSpace(token)) {
                throw new InvalidOperationException("Missing GitHub token. Set INTELLIGENCEX_GITHUB_TOKEN, GITHUB_TOKEN, or GH_TOKEN.");
            }

            using var client = new GitHubReleaseClient(token!);
            var baseBranch = ResolveCurrentBranch(repoPath);
            if (string.IsNullOrWhiteSpace(baseBranch) || string.Equals(baseBranch, "HEAD", StringComparison.OrdinalIgnoreCase)) {
                baseBranch = await client.GetDefaultBranchAsync(owner, repo).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(baseBranch)) {
                    throw new InvalidOperationException("Unable to resolve default branch for PR creation.");
                }
            }

            RunGit(repoPath, "checkout", baseBranch!);
            var branchName = BuildPrBranch(options.PrBranch, versionLabel);
            RunGit(repoPath, "checkout", "-B", branchName);
            RunGit(repoPath, "add", "-A");
            RunGit(repoPath, "commit", "-m", commitMessage);
            RunGit(repoPath, "push", "-u", "origin", branchName);

            var skipReview = options.SkipReviewSet ? options.SkipReview : false;
            var title = ResolvePrTitle(options.PrTitle, versionLabel, skipReview);
            var body = ResolvePrBody(options.PrBody);
            var labels = ResolvePrLabels(options.PrLabels, skipReview);

            var pr = await client.CreatePullRequestAsync(owner, repo, title, branchName, baseBranch!, body)
                .ConfigureAwait(false);
            if (pr is null) {
                Console.WriteLine("PR already exists or could not be created.");
                return;
            }
            Console.WriteLine($"PR created: {pr.Value.Url}");
            await client.AddLabelsAsync(owner, repo, pr.Value.Number, labels).ConfigureAwait(false);
            return;
        }

        if (options.Commit) {
            var branch = ResolveCurrentBranch(repoPath);
            if (string.IsNullOrWhiteSpace(branch) || string.Equals(branch, "HEAD", StringComparison.OrdinalIgnoreCase)) {
                var defaultBranch = ResolveDefaultBranch(repoPath);
                if (string.IsNullOrWhiteSpace(defaultBranch)) {
                    throw new InvalidOperationException("Cannot resolve default branch for commit.");
                }
                EnsureLocalBranch(repoPath, defaultBranch);
                branch = defaultBranch;
            }
            RunGit(repoPath, "add", "-A");
            RunGit(repoPath, "commit", "-m", commitMessage);
            RunGit(repoPath, "push", "origin", branch!);
        }
    }

    private static bool HasGitChanges(string repoPath) {
        try {
            var output = RunGit(repoPath, "status", "--porcelain");
            return !string.IsNullOrWhiteSpace(output);
        } catch {
            return false;
        }
    }

    private static void EnsureGitIdentity(string repoPath) {
        var name = TryRunGit(repoPath, "config", "--get", "user.name");
        var email = TryRunGit(repoPath, "config", "--get", "user.email");
        if (string.IsNullOrWhiteSpace(name)) {
            RunGit(repoPath, "config", "user.name", "github-actions[bot]");
        }
        if (string.IsNullOrWhiteSpace(email)) {
            RunGit(repoPath, "config", "user.email", "41898282+github-actions[bot]@users.noreply.github.com");
        }
    }

    private static string? ResolveCurrentBranch(string repoPath) {
        try {
            var branch = RunGit(repoPath, "rev-parse", "--abbrev-ref", "HEAD");
            return string.IsNullOrWhiteSpace(branch) ? null : branch.Trim();
        } catch {
            return null;
        }
    }

    private static string? ResolveDefaultBranch(string repoPath) {
        var fromEnv = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
        var refType = Environment.GetEnvironmentVariable("GITHUB_REF_TYPE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && string.Equals(refType, "branch", StringComparison.OrdinalIgnoreCase)) {
            return fromEnv.Trim();
        }

        var symbolic = TryRunGit(repoPath, "symbolic-ref", "--short", "refs/remotes/origin/HEAD");
        if (!string.IsNullOrWhiteSpace(symbolic)) {
            var name = symbolic.Trim();
            if (name.StartsWith("origin/", StringComparison.OrdinalIgnoreCase)) {
                return name.Substring("origin/".Length);
            }
            return name;
        }

        return "main";
    }

    private static void EnsureLocalBranch(string repoPath, string branch) {
        var existing = TryRunGit(repoPath, "show-ref", "--verify", $"refs/heads/{branch}");
        if (string.IsNullOrWhiteSpace(existing)) {
            RunGit(repoPath, "checkout", "-B", branch, $"origin/{branch}");
            return;
        }
        RunGit(repoPath, "checkout", branch);
    }

    private static string? ResolveRepoSlug(ReleaseNotesOptions options) {
        if (!string.IsNullOrWhiteSpace(options.RepoSlug)) {
            return options.RepoSlug;
        }
        return Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
    }

    private static bool TryParseRepoSlug(string? value, out string owner, out string repo) {
        owner = string.Empty;
        repo = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        var parts = value.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        repo = parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
    }

    private static string? ResolveGitHubToken() {
        return Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
               ?? Environment.GetEnvironmentVariable("GH_TOKEN");
    }

    private static string BuildPrBranch(string? proposed, string versionLabel) {
        var raw = string.IsNullOrWhiteSpace(proposed) ? $"release-notes/{versionLabel}" : proposed!;
        var fallback = $"release-notes/{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        return SanitizeBranchName(raw, fallback);
    }

    private static string SanitizeBranchName(string raw, string fallback) {
        var sanitized = raw.Trim().ToLowerInvariant();
        sanitized = Regex.Replace(sanitized, "[^a-z0-9._/-]+", "-");
        sanitized = Regex.Replace(sanitized, "/+", "-");
        sanitized = Regex.Replace(sanitized, "\\.\\.+", ".");
        sanitized = sanitized.Replace("@{", "-");
        sanitized = sanitized.Trim('-');
        sanitized = sanitized.Trim('/', '.', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string ResolvePrTitle(string? title, string versionLabel, bool skipReview) {
        var resolved = string.IsNullOrWhiteSpace(title) ? $"Release notes for {versionLabel}" : title!.Trim();
        if (skipReview && !resolved.Contains("[skip-review]", StringComparison.OrdinalIgnoreCase)) {
            resolved = $"[skip-review] {resolved}";
        }
        return resolved;
    }

    private static string ResolvePrBody(string? body) {
        return string.IsNullOrWhiteSpace(body) ? "Automated release notes update." : body!;
    }

    private static List<string> ResolvePrLabels(string? labels, bool skipReview) {
        var result = new List<string>();
        if (!string.IsNullOrWhiteSpace(labels)) {
            var parts = labels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts) {
                var value = part.Trim();
                if (string.IsNullOrWhiteSpace(value)) {
                    continue;
                }
                if (!result.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase))) {
                    result.Add(value);
                }
            }
        }
        if (skipReview && !result.Any(existing => string.Equals(existing, "skip-review", StringComparison.OrdinalIgnoreCase))) {
            result.Add("skip-review");
        }
        return result;
    }

    private static string RunGit(string repoPath, params string[] arguments) {
        var psi = new ProcessStartInfo {
            FileName = "git",
            WorkingDirectory = repoPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in arguments) {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null) {
            throw new InvalidOperationException("Failed to start git process.");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Git command failed." : error.Trim());
        }
        return output.Trim();
    }

    private static string? TryRunGit(string repoPath, params string[] arguments) {
        try {
            return RunGit(repoPath, arguments);
        } catch {
            return null;
        }
    }

    private static bool TryWriteAuthFromEnv() {
        var authJson = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_JSON");
        var authB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        if (string.IsNullOrWhiteSpace(authJson) && string.IsNullOrWhiteSpace(authB64)) {
            return true;
        }

        string content;
        if (!string.IsNullOrWhiteSpace(authJson)) {
            content = authJson!;
        } else {
            try {
                var bytes = Convert.FromBase64String(authB64!);
                content = Encoding.UTF8.GetString(bytes);
            } catch {
                Console.Error.WriteLine("Failed to decode INTELLIGENCEX_AUTH_B64.");
                return false;
            }
        }

        var path = ResolveAuthWritePathForEnvImport();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
        return true;
    }

    private static string ResolveAuthWritePathForEnvImport() {
        var configuredPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath)) {
            return configuredPath;
        }
        if (!string.IsNullOrWhiteSpace(_temporaryAuthPathFromEnv)) {
            return _temporaryAuthPathFromEnv!;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "intelligencex-release-notes");
        Directory.CreateDirectory(tempDir);
        var tempPath = Path.Combine(tempDir, $"auth-{Guid.NewGuid():N}.json");
        _temporaryAuthPathFromEnv = tempPath;
        Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", tempPath);
        return tempPath;
    }

    private static void CleanupTempAuthPathFromEnv() {
        var tempPath = _temporaryAuthPathFromEnv;
        if (string.IsNullOrWhiteSpace(tempPath)) {
            return;
        }
        try {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        } catch {
            // Best-effort cleanup.
        }

        try {
            var tempDir = Path.GetDirectoryName(tempPath);
            if (!string.IsNullOrWhiteSpace(tempDir)
                && Directory.Exists(tempDir)
                && !Directory.EnumerateFileSystemEntries(tempDir).Any()) {
                Directory.Delete(tempDir);
            }
        } catch {
            // Best-effort cleanup.
        }

        var currentPath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH");
        if (string.Equals(currentPath, tempPath, StringComparison.OrdinalIgnoreCase)) {
            Environment.SetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH", null);
        }
        _temporaryAuthPathFromEnv = null;
    }
}
