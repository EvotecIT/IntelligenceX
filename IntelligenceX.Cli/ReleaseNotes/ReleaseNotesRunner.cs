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
            if (!TryWriteAuthFromEnv()) {
                return 1;
            }
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
        } finally {
            CleanupTempAuthPathFromEnv();
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

}
