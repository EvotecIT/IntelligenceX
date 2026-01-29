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
        Console.WriteLine("  --dry-run               Show output but don't write files");
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ReleaseNotesOptions.Parse(args);
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

            var fromTag = NormalizeRef(options.FromTag ?? ResolveLatestTag(repoPath));
            var toRef = NormalizeRef(options.ToRef) ?? "HEAD";
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

            if (!options.DryRun) {
                if (!string.IsNullOrWhiteSpace(options.OutputPath)) {
                    File.WriteAllText(options.OutputPath!, normalized.TrimEnd() + Environment.NewLine);
                }

                var changelogPath = ResolveChangelogPath(options, repoPath);
                if (!string.IsNullOrWhiteSpace(changelogPath)) {
                    if (!hasChanges) {
                        Console.Error.WriteLine("No change items detected; skipping changelog update.");
                    } else {
                        UpdateChangelog(changelogPath!, normalized, options.Version ?? toRef);
                    }
                }
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string? ResolveLatestTag(string repoPath) {
        try {
            var tag = RunGit(repoPath, "describe", "--tags", "--abbrev=0");
            return string.IsNullOrWhiteSpace(tag) ? null : tag.Trim();
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
