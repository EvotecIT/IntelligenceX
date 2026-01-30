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

internal static class ReleaseNotesRunner {
    public static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    public static void PrintHelp() {
        Console.WriteLine("Release commands:");
        Console.WriteLine("  intelligencex release notes [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --from <tag>            Start tag (defaults to latest tag)");
        Console.WriteLine("  --to <tag|ref>          End tag/ref (defaults to HEAD)");
        Console.WriteLine("  --version <version>     Version label for changelog section");
        Console.WriteLine("  --output <path>         Write release notes to file");
        Console.WriteLine("  --changelog <path>      Update changelog at path");
        Console.WriteLine("  --update-changelog      Update CHANGELOG.md in repo root");
        Console.WriteLine("  --repo <path>           Repository path (default: current directory)");
        Console.WriteLine("  --max-commits <n>       Max commit subjects to include (default 200)");
        Console.WriteLine("  --model <model>         OpenAI model (default from OPENAI_MODEL)");
        Console.WriteLine("  --transport <kind>      native or appserver (default from OPENAI_TRANSPORT)");
        Console.WriteLine("  --reasoning-effort <v>  minimal|low|medium|high|xhigh");
        Console.WriteLine("  --reasoning-summary <v> auto|concise|detailed|off");
        Console.WriteLine("  --retry-count <n>       Retry OpenAI requests (default 3)");
        Console.WriteLine("  --retry-delay-seconds   Initial retry delay (default 5)");
        Console.WriteLine("  --retry-max-delay-seconds Max retry delay (default 30)");
        Console.WriteLine("  --commit               Commit changelog changes (uses git)");
        Console.WriteLine("  --create-pr [true|false] Create a PR with changelog changes");
        Console.WriteLine("  --pr-branch <name>      Branch name for PR (default: release-notes/<version>)");
        Console.WriteLine("  --pr-title <text>       PR title");
        Console.WriteLine("  --pr-body <text>        PR body");
        Console.WriteLine("  --pr-labels <list>      Comma-separated PR labels");
        Console.WriteLine("  --skip-review [true|false] Add skip-review label/title prefix");
        Console.WriteLine("  --repo-slug <owner/name> GitHub repository for PR creation");
        Console.WriteLine("  --dry-run               Show output but don't write files");
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ReleaseNotesOptions.Parse(args);
        ReleaseNotesOptions.ApplyEnvDefaults(options);
        if (options.ShowHelp) {
            PrintHelp();
            return 1;
        }

        try {
            TryWriteAuthFromEnv();
            var repoPath = options.RepoPath ?? Environment.CurrentDirectory;
            if (!Directory.Exists(repoPath)) {
                Console.Error.WriteLine($"Repository path not found: {repoPath}");
                return 1;
            }
            if (!Directory.Exists(Path.Combine(repoPath, ".git"))) {
                Console.Error.WriteLine($"Not a git repository: {repoPath}");
                return 1;
            }

            var defaults = ResolveDefaultRefs(options, repoPath);
            var fromTag = defaults.FromTag;
            var toRef = defaults.ToRef;
            var versionLabel = defaults.VersionLabel;
            ValidateRef(fromTag, "--from");
            ValidateRef(toRef, "--to");
            EnsureRefExists(repoPath, toRef, "--to");
            if (!string.IsNullOrWhiteSpace(fromTag)) {
                EnsureRefExists(repoPath, fromTag, "--from");
            }

            var ranges = ResolveRanges(repoPath, fromTag, toRef);
            var commitSubjects = ReadCommitSubjects(repoPath, ranges.CommitRange, options.MaxCommits);
            if (commitSubjects.Count == 0) {
                Console.WriteLine("No commits found for the specified range. Skipping release notes.");
                return 0;
            }

            var areaSummary = BuildAreaSummary(repoPath, ranges.DiffRange);
            var prompt = BuildPrompt(fromTag, toRef, commitSubjects, areaSummary);

            var output = await OpenAiReleaseNotesClient.GenerateAsync(prompt, options, CancellationToken.None)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(output)) {
                Console.Error.WriteLine("Release notes generation returned no content.");
                return 1;
            }

            var normalized = NormalizeReleaseNotes(output, out var hasChanges);
            if (string.IsNullOrWhiteSpace(normalized)) {
                Console.Error.WriteLine("Release notes output was empty after normalization.");
                return 1;
            }

            Console.WriteLine(normalized);

            string? changelogPath = null;
            if (!options.DryRun) {
                if (!string.IsNullOrWhiteSpace(options.OutputPath)) {
                    File.WriteAllText(options.OutputPath!, normalized.TrimEnd() + Environment.NewLine);
                }

                changelogPath = ResolveChangelogPath(options, repoPath);
                if (!string.IsNullOrWhiteSpace(changelogPath)) {
                    if (!hasChanges) {
                        Console.Error.WriteLine("No change items detected; skipping changelog update.");
                    } else {
                        UpdateChangelog(changelogPath!, normalized, versionLabel);
                    }
                }
            }

            if (!options.DryRun && (options.Commit || options.CreatePr)) {
                await HandleGitOutputAsync(repoPath, changelogPath, versionLabel, options).ConfigureAwait(false);
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? ResolveLatestTag(string repoPath) {
        try {
            return ResolveLatestTagByVersion(repoPath);
        } catch {
            return null;
        }
    }

    private static (string? FromTag, string ToRef, string VersionLabel) ResolveDefaultRefs(ReleaseNotesOptions options, string repoPath) {
        var toRef = NormalizeRef(options.ToRef);
        if (string.IsNullOrWhiteSpace(toRef)) {
            var envRef = Environment.GetEnvironmentVariable("GITHUB_REF_NAME");
            toRef = string.IsNullOrWhiteSpace(envRef) ? "HEAD" : envRef.Trim();
        }

        var versionLabel = !string.IsNullOrWhiteSpace(options.Version)
            ? options.Version!.Trim()
            : toRef;

        var fromTag = NormalizeRef(options.FromTag);
        if (string.IsNullOrWhiteSpace(fromTag)) {
            if (IsGitHubTagRef()) {
                fromTag = ResolvePreviousTag(repoPath, toRef);
            } else {
                fromTag = ResolveLatestTagByVersion(repoPath);
            }
        }

        return (fromTag, toRef, versionLabel);
    }

    private static bool IsGitHubTagRef() {
        var refType = Environment.GetEnvironmentVariable("GITHUB_REF_TYPE");
        if (string.Equals(refType, "tag", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        var fullRef = Environment.GetEnvironmentVariable("GITHUB_REF");
        return !string.IsNullOrWhiteSpace(fullRef) && fullRef.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveLatestTagByVersion(string repoPath) {
        try {
            var output = RunGit(repoPath, "tag", "--sort=-v:refname");
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
        } catch {
            return null;
        }
    }

    private static string? ResolvePreviousTag(string repoPath, string currentTag) {
        try {
            var output = RunGit(repoPath, "tag", "--sort=-v:refname");
            var tags = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            if (tags.Count == 0) {
                return null;
            }
            foreach (var tag in tags) {
                if (!string.Equals(tag, currentTag, StringComparison.OrdinalIgnoreCase)) {
                    return tag;
                }
            }
            return null;
        } catch {
            return null;
        }
    }

    private static string? NormalizeRef(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateRef(string? value, string argName) {
        if (string.IsNullOrWhiteSpace(value)) {
            return;
        }
        if (!IsAllowedRef(value)) {
            throw new InvalidOperationException($"Invalid {argName} value: {value}");
        }
    }

    private static void EnsureRefExists(string repoPath, string refName, string argName) {
        if (string.IsNullOrWhiteSpace(refName)) {
            return;
        }
        try {
            RunGit(repoPath, "rev-parse", "--verify", $"{refName}^{{}}");
        } catch {
            throw new InvalidOperationException($"{argName} not found: {refName}");
        }
    }

    private static bool IsAllowedRef(string value) {
        if (IsHeadExpression(value) || IsSha(value)) {
            return true;
        }
        return IsSafeRefName(value);
    }

    private static bool IsHeadExpression(string value) {
        return Regex.IsMatch(value, "^HEAD([~^][0-9]+)*$", RegexOptions.IgnoreCase);
    }

    private static bool IsSha(string value) {
        return Regex.IsMatch(value, "^[0-9a-fA-F]{7,40}$", RegexOptions.CultureInvariant);
    }

    private static bool IsSafeRefName(string value) {
        if (value.Length == 0) {
            return false;
        }
        if (value.StartsWith("-", StringComparison.Ordinal) || value.StartsWith("/", StringComparison.Ordinal)) {
            return false;
        }
        if (value.EndsWith("/", StringComparison.Ordinal) || value.EndsWith(".", StringComparison.Ordinal)) {
            return false;
        }
        if (value.Contains("..", StringComparison.Ordinal) || value.Contains("@{", StringComparison.Ordinal)) {
            return false;
        }
        if (value.Contains("//", StringComparison.Ordinal)) {
            return false;
        }
        foreach (var ch in value) {
            if (char.IsWhiteSpace(ch)) {
                return false;
            }
            if (ch is '~' or '^' or ':' or '?' or '*' or '[' or '\\') {
                return false;
            }
        }
        return true;
    }

    private static (string CommitRange, string DiffRange) ResolveRanges(string repoPath, string? fromTag, string toRef) {
        if (!string.IsNullOrWhiteSpace(fromTag)) {
            var range = $"{fromTag}..{toRef}";
            return (range, range);
        }

        var root = ResolveRootCommit(repoPath, toRef);
        if (string.IsNullOrWhiteSpace(root)) {
            return (toRef, toRef);
        }

        return (toRef, $"{root}..{toRef}");
    }

    private static string? ResolveRootCommit(string repoPath, string toRef) {
        try {
            var output = RunGit(repoPath, "rev-list", "--max-parents=0", toRef);
            var first = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(first) ? null : first;
        } catch {
            return null;
        }
    }

    private static IReadOnlyList<string> ReadCommitSubjects(string repoPath, string range, int maxCommits) {
        var limit = Math.Max(1, maxCommits);
        var output = RunGit(repoPath, "log", range, "--pretty=format:%s", $"--max-count={limit}");
        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static string BuildAreaSummary(string repoPath, string range) {
        var output = RunGit(repoPath, "diff", "--name-only", range);
        var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (files.Length == 0) {
            return "No files changed.";
        }

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            var normalized = file.Replace('\\', '/');
            var top = normalized.Split('/').FirstOrDefault();
            if (string.IsNullOrWhiteSpace(top)) {
                top = "(root)";
            }
            counts[top] = counts.TryGetValue(top, out var current) ? current + 1 : 1;
        }

        var lines = counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"- {pair.Key}: {pair.Value} file(s)");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildPrompt(string? fromTag, string toRef, IReadOnlyList<string> commits, string areaSummary) {
        var sb = new StringBuilder();
        sb.AppendLine("You are generating release notes for a Git repository.");
        sb.AppendLine("Provide a concise, high-level summary for non-technical readers.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Do NOT list file paths, commit hashes, or PR numbers.");
        sb.AppendLine("- Do NOT mention authors.");
        sb.AppendLine("- Use Markdown headings and bullet lists.");
        sb.AppendLine("- Keep it brief and focus on outcomes.");
        sb.AppendLine();
        sb.AppendLine("Output format (exact order):");
        sb.AppendLine("## Summary");
        sb.AppendLine("- 2-4 bullets");
        sb.AppendLine("## Changes");
        sb.AppendLine("- Added:");
        sb.AppendLine("  - bullets");
        sb.AppendLine("- Changed:");
        sb.AppendLine("  - bullets");
        sb.AppendLine("- Fixed:");
        sb.AppendLine("  - bullets");
        sb.AppendLine();
        sb.AppendLine($"Range: {(string.IsNullOrWhiteSpace(fromTag) ? "<start>" : fromTag)}..{toRef}");
        sb.AppendLine();
        sb.AppendLine("Areas touched:");
        sb.AppendLine(areaSummary);
        sb.AppendLine();
        sb.AppendLine("Commit subjects:");
        foreach (var commit in commits) {
            sb.AppendLine($"- {commit}");
        }
        return sb.ToString();
    }

    private enum ReleaseSection {
        None,
        Summary,
        Changes
    }

    private enum ChangeSection {
        None,
        Added,
        Changed,
        Fixed
    }

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
                throw new InvalidOperationException("Missing GitHub token. Set GITHUB_TOKEN or INTELLIGENCEX_GITHUB_TOKEN.");
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
                throw new InvalidOperationException("Cannot commit in detached HEAD. Checkout a branch or use --create-pr.");
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

    private static void TryWriteAuthFromEnv() {
        var authJson = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_JSON");
        var authB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        if (string.IsNullOrWhiteSpace(authJson) && string.IsNullOrWhiteSpace(authB64)) {
            return;
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
                return;
            }
        }

        var path = AuthPaths.ResolveAuthPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }
}

internal sealed class ReleaseNotesOptions {
    public string? FromTag { get; set; }
    public string? ToRef { get; set; }
    public string? Version { get; set; }
    public string? OutputPath { get; set; }
    public string? ChangelogPath { get; set; }
    public bool UpdateChangelog { get; set; }
    public int MaxCommits { get; set; } = 200;
    public string? Model { get; set; }
    public OpenAITransportKind? Transport { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public int RetryCount { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;
    public int RetryMaxDelaySeconds { get; set; } = 30;
    public bool Commit { get; set; }
    public bool CommitSet { get; set; }
    public bool CreatePr { get; set; }
    public bool CreatePrSet { get; set; }
    public string? PrBranch { get; set; }
    public string? PrTitle { get; set; }
    public string? PrBody { get; set; }
    public string? PrLabels { get; set; }
    public bool SkipReview { get; set; }
    public bool SkipReviewSet { get; set; }
    public string? RepoSlug { get; set; }
    public bool DryRun { get; set; }
    public bool ShowHelp { get; set; }
    public string? RepoPath { get; set; }

    public static ReleaseNotesOptions Parse(string[] args) {
        var options = new ReleaseNotesOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg is "-h" or "--help") {
                options.ShowHelp = true;
                return options;
            }
            switch (arg) {
                case "--from":
                    options.FromTag = ReadValue(args, ref i);
                    break;
                case "--to":
                    options.ToRef = ReadValue(args, ref i);
                    break;
                case "--version":
                    options.Version = ReadValue(args, ref i);
                    break;
                case "--output":
                    options.OutputPath = ReadValue(args, ref i);
                    break;
                case "--changelog":
                    options.ChangelogPath = ReadValue(args, ref i);
                    break;
                case "--update-changelog":
                    options.UpdateChangelog = true;
                    break;
                case "--max-commits":
                    options.MaxCommits = ReadIntValue(args, ref i, options.MaxCommits);
                    break;
                case "--model":
                    options.Model = ReadValue(args, ref i);
                    break;
                case "--transport":
                    options.Transport = ParseTransportValue(ReadValue(args, ref i));
                    break;
                case "--reasoning-effort":
                    options.ReasoningEffort = ChatEnumParser.ParseReasoningEffort(ReadValue(args, ref i));
                    break;
                case "--reasoning-summary":
                    options.ReasoningSummary = ChatEnumParser.ParseReasoningSummary(ReadValue(args, ref i));
                    break;
                case "--retry-count":
                    options.RetryCount = ReadIntValue(args, ref i, options.RetryCount);
                    break;
                case "--retry-delay-seconds":
                    options.RetryDelaySeconds = ReadIntValue(args, ref i, options.RetryDelaySeconds);
                    break;
                case "--retry-max-delay-seconds":
                    options.RetryMaxDelaySeconds = ReadIntValue(args, ref i, options.RetryMaxDelaySeconds);
                    break;
                case "--commit":
                    options.CommitSet = true;
                    options.Commit = ReadBoolFlag(args, ref i, "--commit", true);
                    break;
                case "--create-pr":
                    options.CreatePrSet = true;
                    options.CreatePr = ReadBoolFlag(args, ref i, "--create-pr", true);
                    break;
                case "--pr-branch":
                    options.PrBranch = ReadValue(args, ref i);
                    break;
                case "--pr-title":
                    options.PrTitle = ReadValue(args, ref i);
                    break;
                case "--pr-body":
                    options.PrBody = ReadValue(args, ref i);
                    break;
                case "--pr-labels":
                    options.PrLabels = ReadValue(args, ref i);
                    break;
                case "--skip-review":
                    options.SkipReviewSet = true;
                    options.SkipReview = ReadBoolFlag(args, ref i, "--skip-review", true);
                    break;
                case "--repo-slug":
                    options.RepoSlug = ReadValue(args, ref i);
                    break;
                case "--repo":
                    options.RepoPath = ReadValue(args, ref i);
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
            }
        }

        return options;
    }

    private static string ReadValue(string[] args, ref int index) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"Missing value for {args[index]}.");
        }
        index++;
        return args[index];
    }

    private static int ReadIntValue(string[] args, ref int index, int fallback) {
        var value = ReadValue(args, ref index);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static bool ReadBoolFlag(string[] args, ref int index, string flagName, bool defaultValue) {
        if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) {
            var raw = args[++index];
            if (string.IsNullOrWhiteSpace(raw)) {
                return defaultValue;
            }
            if (bool.TryParse(raw, out var parsed)) {
                return parsed;
            }
            throw new InvalidOperationException($"Invalid value for {flagName}: {raw}");
        }
        return defaultValue;
    }

    internal static OpenAITransportKind? ParseTransportValue(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch {
            "native" => OpenAITransportKind.Native,
            "appserver" or "app-server" or "codex" => OpenAITransportKind.AppServer,
            _ => null
        };
    }

    public static void ApplyEnvDefaults(ReleaseNotesOptions options) {
        if (options is null) {
            return;
        }

        options.FromTag ??= ReadEnv("INTELLIGENCEX_RELEASE_FROM");
        options.ToRef ??= ReadEnv("INTELLIGENCEX_RELEASE_TO");
        options.Version ??= ReadEnv("INTELLIGENCEX_RELEASE_VERSION");
        options.OutputPath ??= ReadEnv("INTELLIGENCEX_RELEASE_OUTPUT");
        options.ChangelogPath ??= ReadEnv("INTELLIGENCEX_RELEASE_CHANGELOG");
        if (!options.UpdateChangelog) {
            options.UpdateChangelog = ReadEnvBool("INTELLIGENCEX_RELEASE_UPDATE_CHANGELOG") ?? options.UpdateChangelog;
        }
        options.PrBranch ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_BRANCH");
        options.PrTitle ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_TITLE");
        options.PrBody ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_BODY");
        options.PrLabels ??= ReadEnv("INTELLIGENCEX_RELEASE_PR_LABELS");
        options.RepoSlug ??= ReadEnv("INTELLIGENCEX_RELEASE_REPO_SLUG");

        if (!options.CommitSet) {
            var commit = ReadEnvBool("INTELLIGENCEX_RELEASE_COMMIT");
            if (commit.HasValue) {
                options.Commit = commit.Value;
            }
        }
        if (!options.CreatePrSet) {
            var create = ReadEnvBool("INTELLIGENCEX_RELEASE_CREATE_PR");
            if (create.HasValue) {
                options.CreatePr = create.Value;
            }
        }
        if (!options.SkipReviewSet) {
            var skip = ReadEnvBool("INTELLIGENCEX_RELEASE_SKIP_REVIEW");
            if (skip.HasValue) {
                options.SkipReview = skip.Value;
                options.SkipReviewSet = true;
            }
        }
    }

    private static string? ReadEnv(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool? ReadEnvBool(string name) {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return bool.TryParse(value, out var parsed) ? parsed : null;
    }
}

internal static class OpenAiReleaseNotesClient {
    public static async Task<string> GenerateAsync(string prompt, ReleaseNotesOptions options, CancellationToken cancellationToken) {
        var attempts = Math.Max(1, options.RetryCount);
        var delaySeconds = Math.Max(1, options.RetryDelaySeconds);
        var maxDelaySeconds = Math.Max(delaySeconds, options.RetryMaxDelaySeconds);
        var delay = TimeSpan.FromSeconds(delaySeconds);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= attempts; attempt++) {
            try {
                return await GenerateOnceAsync(prompt, options, cancellationToken).ConfigureAwait(false);
            } catch (Exception ex) when (IsTransient(ex) && attempt < attempts && !cancellationToken.IsCancellationRequested) {
                lastError = ex;
                var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(200, 800));
                var wait = delay + jitter;
                Console.Error.WriteLine($"OpenAI request failed (attempt {attempt}/{attempts}): {ex.Message}. Retrying in {wait.TotalSeconds:0.0}s.");
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
                var nextDelaySeconds = Math.Min(maxDelaySeconds, delay.TotalSeconds * 2);
                delay = TimeSpan.FromSeconds(nextDelaySeconds);
            }
        }

        if (lastError is not null) {
            throw lastError;
        }
        return string.Empty;
    }

    private static async Task<string> GenerateOnceAsync(string prompt, ReleaseNotesOptions options, CancellationToken cancellationToken) {
        var model = options.Model
                    ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
                    ?? "gpt-5.2-codex";
        var transport = options.Transport
                        ?? ReleaseNotesOptions.ParseTransportValue(Environment.GetEnvironmentVariable("OPENAI_TRANSPORT"))
                        ?? OpenAITransportKind.AppServer;

        var clientOptions = new IntelligenceXClientOptions {
            DefaultModel = model,
            TransportKind = transport
        };

        if (clientOptions.TransportKind == OpenAITransportKind.AppServer) {
            var codexPath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH");
            var codexArgs = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS");
            var codexCwd = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_CWD");
            if (!string.IsNullOrWhiteSpace(codexPath)) {
                clientOptions.AppServerOptions.ExecutablePath = codexPath;
            }
            if (!string.IsNullOrWhiteSpace(codexArgs)) {
                clientOptions.AppServerOptions.Arguments = codexArgs;
            }
            if (!string.IsNullOrWhiteSpace(codexCwd)) {
                clientOptions.AppServerOptions.WorkingDirectory = codexCwd;
            }
        }

        await using var client = await IntelligenceXClient.ConnectAsync(clientOptions, cancellationToken)
            .ConfigureAwait(false);

        var deltas = new StringBuilder();
        var lastDelta = DateTimeOffset.UtcNow;
        using var subscription = client.SubscribeDelta(text => {
            if (!string.IsNullOrWhiteSpace(text)) {
                lock (deltas) {
                    deltas.Append(text);
                    lastDelta = DateTimeOffset.UtcNow;
                }
            }
        });

        var chatOptions = new ChatOptions {
            Model = model,
            NewThread = true,
            ReasoningEffort = options.ReasoningEffort,
            ReasoningSummary = options.ReasoningSummary
        };

        var input = ChatInput.FromText(prompt);
        var turn = await client.ChatAsync(input, chatOptions, cancellationToken).ConfigureAwait(false);
        var output = ExtractOutputs(turn.Outputs);
        if (!string.IsNullOrWhiteSpace(output)) {
            return output;
        }

        return await WaitForDeltasAsync(deltas, () => lastDelta, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> WaitForDeltasAsync(StringBuilder deltas, Func<DateTimeOffset> getLastDelta,
        CancellationToken cancellationToken) {
        var start = DateTimeOffset.UtcNow;
        var max = TimeSpan.FromSeconds(90);
        var idle = TimeSpan.FromSeconds(3);

        while (DateTimeOffset.UtcNow - start < max) {
            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            var last = getLastDelta();
            if (DateTimeOffset.UtcNow - last > idle) {
                break;
            }
        }

        lock (deltas) {
            return deltas.ToString();
        }
    }

    private static string ExtractOutputs(IReadOnlyList<TurnOutput> outputs) {
        if (outputs.Count == 0) {
            return string.Empty;
        }
        var builder = new StringBuilder();
        foreach (var output in outputs.Where(o => o.IsText && !string.IsNullOrWhiteSpace(o.Text))) {
            builder.AppendLine(output.Text);
        }
        return builder.ToString().Trim();
    }

    private static bool IsTransient(Exception ex) {
        if (ex is OperationCanceledException) {
            return false;
        }
        if (ex is HttpRequestException || ex is IOException || ex is TimeoutException) {
            return true;
        }
        return ex.InnerException is not null && IsTransient(ex.InnerException);
    }
}
