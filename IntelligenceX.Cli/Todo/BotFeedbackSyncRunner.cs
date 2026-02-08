using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class BotFeedbackSyncRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex TaskLine = new(@"^\s*[-*]\s+\[(?<state>[ xX])\]\s+(?<text>.+?)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex TodoTaskLine = new(@"^\s*-\s+\[(?<state>[ xX])\]\s+(?<text>.+?)\s*$",
        RegexOptions.Compiled);

    internal sealed record TaskItem(bool Checked, string Text, string Url);
    internal sealed record PrTasks(int Number, string Title, string Url, IReadOnlyList<TaskItem> Tasks);

    private sealed class Options {
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public string TodoPath { get; set; } = "TODO.md";
        public int MaxPrs { get; set; } = 30;
        public List<string> Bots { get; } = new() { "intelligencex-review" };
        public bool CreateIssues { get; set; }
        public string IssueLabel { get; set; } = "ix-bot-feedback";
        public int MaxIssues { get; set; } = 20;
        public bool ShowHelp { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        List<PrTasks> prs;
        try {
            prs = await FetchOpenPrTasksAsync(options).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        if (prs.Count == 0) {
            Console.WriteLine("No bot checklist items found in open PRs.");
            return 0;
        }

        var updated = UpdateTodo(options.TodoPath, prs, out var changed);
        if (changed) {
            File.WriteAllText(options.TodoPath, updated, Utf8NoBom);
            Console.WriteLine($"Updated {options.TodoPath} with {prs.Count} PR block(s).");
        } else {
            Console.WriteLine($"{options.TodoPath} already up to date.");
        }

        if (!options.CreateIssues) {
            return 0;
        }

        var uncheckedTasks = prs
            .SelectMany(pr => pr.Tasks.Select(t => (Pr: pr, Task: t)))
            .Where(x => !x.Task.Checked)
            .ToList();
        if (uncheckedTasks.Count == 0) {
            Console.WriteLine("No unchecked bot tasks found (no issues to create).");
            return 0;
        }

        await EnsureLabelAsync(options.Repo, options.IssueLabel).ConfigureAwait(false);

        var created = 0;
        foreach (var (pr, task) in uncheckedTasks) {
            if (created >= options.MaxIssues) {
                Console.WriteLine($"Reached --max-issues={options.MaxIssues}; stopping.");
                break;
            }
            var id = BuildTaskId(pr.Number, task.Url, task.Text);
            if (await IssueExistsAsync(options.Repo, id).ConfigureAwait(false)) {
                continue;
            }
            var title = BuildIssueTitle(pr.Number, pr.Title, task.Text);
            var body = BuildIssueBody(pr, task, id);
            var (code, stdout, stderr) = await GhCli.RunAsync(
                "issue", "create",
                "--repo", options.Repo,
                "--title", title,
                "--body", body,
                "--label", options.IssueLabel
            ).ConfigureAwait(false);
            if (code != 0) {
                Console.Error.WriteLine($"Failed to create issue for PR #{pr.Number}: {task.Text}");
                if (!string.IsNullOrWhiteSpace(stderr)) {
                    Console.Error.WriteLine(stderr.Trim());
                }
                continue;
            }
            created++;
            var url = stdout.Trim();
            if (!string.IsNullOrWhiteSpace(url)) {
                Console.WriteLine($"Created issue: {url}");
            }
        }

        Console.WriteLine($"Issue creation done. Created {created} issue(s).");
        return 0;
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();
        options.Bots.Clear();
        options.Bots.Add("intelligencex-review");
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    }
                    break;
                case "--todo":
                    if (i + 1 < args.Length) {
                        options.TodoPath = args[++i];
                    }
                    break;
                case "--max-prs":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var maxPrs) && maxPrs > 0) {
                        options.MaxPrs = maxPrs;
                    }
                    break;
                case "--bot":
                    if (i + 1 < args.Length) {
                        var bot = args[++i];
                        if (!string.IsNullOrWhiteSpace(bot)) {
                            options.Bots.Add(bot.Trim());
                        }
                    }
                    break;
                case "--create-issues":
                    options.CreateIssues = true;
                    break;
                case "--label":
                    if (i + 1 < args.Length) {
                        options.IssueLabel = args[++i];
                    }
                    break;
                case "--max-issues":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var maxIssues) && maxIssues > 0) {
                        options.MaxIssues = maxIssues;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }
        var normalized = options.Bots
            .Where(b => !string.IsNullOrWhiteSpace(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        options.Bots.Clear();
        options.Bots.AddRange(normalized);
        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ShowHelp = true;
        }
        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo sync-bot-feedback [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>      GitHub repo (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --todo <path>            Path to TODO.md (default: TODO.md)");
        Console.WriteLine("  --max-prs <n>            Max open PRs to scan (default: 30)");
        Console.WriteLine("  --bot <login>            Bot login to include (repeatable; default: intelligencex-review)");
        Console.WriteLine("  --create-issues          Create GitHub issues for unchecked tasks (opt-in)");
        Console.WriteLine("  --label <name>           Issue label (default: ix-bot-feedback)");
        Console.WriteLine("  --max-issues <n>         Max issues to create (default: 20)");
    }

    private static async Task<List<PrTasks>> FetchOpenPrTasksAsync(Options options) {
        var (owner, name) = SplitRepo(options.Repo);
        var bots = new HashSet<string>(options.Bots.Select(b => b.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

        var query = """
query($owner: String!, $name: String!, $n: Int!) {
  repository(owner: $owner, name: $name) {
    pullRequests(states: OPEN, first: $n, orderBy: { field: UPDATED_AT, direction: DESC }) {
      nodes {
        number
        title
        url
        comments(last: 50) { nodes { author { login } body url } }
        reviews(last: 50)  { nodes { author { login } body url } }
      }
    }
  }
}
""";

        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90),
            "api", "graphql",
            "-f", $"query={query}",
            "-F", $"owner={owner}",
            "-F", $"name={name}",
            "-F", $"n={options.MaxPrs}"
        ).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
        }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0) {
            var first = errors[0];
            var message = first.TryGetProperty("message", out var msg) ? (msg.GetString() ?? "GraphQL error") : "GraphQL error";
            throw new InvalidOperationException($"GitHub GraphQL returned errors: {message}");
        }

        if (!TryGetProperty(root, "data", out var data) ||
            !TryGetProperty(data, "repository", out var repoObj) ||
            repoObj.ValueKind == JsonValueKind.Null) {
            throw new InvalidOperationException("GitHub GraphQL response missing repository data.");
        }
        if (!TryGetProperty(repoObj, "pullRequests", out var prConn) ||
            !TryGetProperty(prConn, "nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("GitHub GraphQL response missing pull request list.");
        }

        var results = new List<PrTasks>();
        foreach (var pr in nodes.EnumerateArray()) {
            if (!TryGetProperty(pr, "number", out var numProp) || numProp.ValueKind != JsonValueKind.Number || !numProp.TryGetInt32(out var number)) {
                continue;
            }
            var title = pr.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
            var prUrl = pr.TryGetProperty("url", out var u) ? (u.GetString() ?? "") : "";

            var tasks = new List<TaskItem>();
            ExtractTasks(pr, "comments", bots, tasks);
            ExtractTasks(pr, "reviews", bots, tasks);
            if (tasks.Count > 0) {
                results.Add(new PrTasks(number, title, prUrl, tasks));
            }
        }
        return results;
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static void ExtractTasks(JsonElement pr, string field, HashSet<string> bots, List<TaskItem> tasks) {
        if (!pr.TryGetProperty(field, out var conn) || !conn.TryGetProperty("nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            return;
        }
        foreach (var n in nodes.EnumerateArray()) {
            var author = TryReadLogin(n);
            if (!bots.Contains(author)) {
                continue;
            }
            var body = ReadStringOrEmpty(n, "body");
            var url = ReadStringOrEmpty(n, "url");
            tasks.AddRange(ParseTasks(body, url));
        }
    }

    private static string TryReadLogin(JsonElement obj) {
        if (!TryGetProperty(obj, "author", out var author) || author.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(author, "login", out var loginProp) || loginProp.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return loginProp.GetString() ?? string.Empty;
    }

    private static string ReadStringOrEmpty(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static IEnumerable<TaskItem> ParseTasks(string body, string url) {
        if (string.IsNullOrWhiteSpace(body)) {
            yield break;
        }
        foreach (var line in body.Split('\n')) {
            var m = TaskLine.Match(line);
            if (!m.Success) {
                continue;
            }
            var state = m.Groups["state"].Value;
            var text = m.Groups["text"].Value.Trim();
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }
            var isChecked = state.Equals("x", StringComparison.OrdinalIgnoreCase);
            yield return new TaskItem(isChecked, text, url);
        }
    }

    private static string UpdateTodo(string todoPath, IReadOnlyList<PrTasks> prs, out bool changed) {
        changed = false;
        if (!File.Exists(todoPath)) {
            throw new FileNotFoundException("TODO.md not found.", todoPath);
        }
        var original = File.ReadAllText(todoPath);
        const string header = "## Review Feedback Backlog (Bots)";
        var headerIndex = original.IndexOf(header, StringComparison.Ordinal);
        if (headerIndex < 0) {
            throw new InvalidOperationException($"Missing section header in {todoPath}: {header}");
        }

        var newline = DetectNewline(original);
        var sectionStart = headerIndex;
        var sectionEnd = FindNextH2(original, sectionStart + header.Length);
        if (sectionEnd < 0) {
            sectionEnd = original.Length;
        }
        var section = original.Substring(sectionStart, sectionEnd - sectionStart);
        var newSection = UpdateSection(section, prs, newline, out var sectionChanged);
        if (!sectionChanged) {
            return original;
        }
        changed = true;
        return original.Substring(0, sectionStart) + newSection + original.Substring(sectionEnd);
    }

    private static string DetectNewline(string text) {
        return text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    }

    private static int FindNextH2(string text, int startAt) {
        var idx = startAt;
        while (idx < text.Length) {
            var next = text.IndexOf("\n## ", idx, StringComparison.Ordinal);
            if (next < 0) {
                return -1;
            }
            return next + 1; // include newline
        }
        return -1;
    }

    // Internal for tests.
    internal static string UpdateSection(string section, IReadOnlyList<PrTasks> prs, string newline, out bool changed) {
        changed = false;

        var text = section;
        var inserts = new List<string>();
        foreach (var pr in prs) {
            var existing = TryParseExistingPrBlock(text, pr.Number);
            var merged = MergeTasks(existing, pr);
            var block = RenderPrBlock(merged, newline);
            var pattern = new Regex(
                @$"(?s)<details>\s*\n<summary>PR\s*#\s*{pr.Number}\b.*?\n</details>\s*\n",
                RegexOptions.Compiled);
            if (pattern.IsMatch(text)) {
                var replaced = pattern.Replace(text, block, 1);
                if (!string.Equals(replaced, text, StringComparison.Ordinal)) {
                    text = replaced;
                    changed = true;
                }
            } else {
                inserts.Add(block);
            }
        }

        if (inserts.Count > 0) {
            var insertAt = FindInsertPoint(text);
            text = text.Insert(insertAt, string.Join(string.Empty, inserts));
            changed = true;
        }
        return text;
    }

    internal sealed record ExistingPrBlock(int Number, IReadOnlyList<TaskItem> Tasks);

    // Internal for tests.
    internal static ExistingPrBlock? TryParseExistingPrBlock(string section, int prNumber) {
        var pattern = new Regex(
            @$"(?s)<details>\s*\n<summary>PR\s*#\s*{prNumber}\b.*?\n</details>\s*\n",
            RegexOptions.Compiled);
        var match = pattern.Match(section);
        if (!match.Success) {
            return null;
        }

        var tasks = new List<TaskItem>();
        foreach (var line in match.Value.Split('\n')) {
            var m = TodoTaskLine.Match(line);
            if (!m.Success) {
                continue;
            }
            var state = m.Groups["state"].Value;
            var text = m.Groups["text"].Value.Trim();
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            // We render as: "- [ ] <text>. Links: <url>"
            // Preserve anything before ". Links:" as task text key; keep URL if present.
            var url = string.Empty;
            var marker = ". Links:";
            var markerIndex = text.LastIndexOf(marker, StringComparison.Ordinal);
            if (markerIndex >= 0) {
                url = text.Substring(markerIndex + marker.Length).Trim();
                text = text.Substring(0, markerIndex).TrimEnd();
            }

            var isChecked = state.Equals("x", StringComparison.OrdinalIgnoreCase);
            tasks.Add(new TaskItem(isChecked, text, url));
        }
        return new ExistingPrBlock(prNumber, tasks);
    }

    // Internal for tests.
    internal static PrTasks MergeTasks(ExistingPrBlock? existing, PrTasks current) {
        if (existing is null || existing.Tasks.Count == 0) {
            // De-dup current tasks by text; OR checked for repeated occurrences.
            var dedup = new Dictionary<string, TaskItem>(StringComparer.Ordinal);
            foreach (var t in current.Tasks) {
                if (!dedup.TryGetValue(t.Text, out var seen)) {
                    dedup[t.Text] = t;
                } else {
                    dedup[t.Text] = new TaskItem(seen.Checked || t.Checked, t.Text, string.IsNullOrWhiteSpace(seen.Url) ? t.Url : seen.Url);
                }
            }
            return current with { Tasks = dedup.Values.ToList() };
        }

        // Build a map of existing TODO items by text so manual checking in TODO.md is preserved.
        var existingByText = new Dictionary<string, TaskItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in existing.Tasks) {
            if (string.IsNullOrWhiteSpace(t.Text)) {
                continue;
            }
            if (!existingByText.TryGetValue(t.Text, out var seen)) {
                existingByText[t.Text] = t;
            } else {
                existingByText[t.Text] = new TaskItem(seen.Checked || t.Checked, t.Text, string.IsNullOrWhiteSpace(seen.Url) ? t.Url : seen.Url);
            }
        }

        var mergedByText = new Dictionary<string, TaskItem>(StringComparer.OrdinalIgnoreCase);

        // Start with existing tasks to preserve manual edits.
        foreach (var kvp in existingByText) {
            mergedByText[kvp.Key] = kvp.Value;
        }

        // Merge in current bot tasks: never downgrade checked -> unchecked.
        foreach (var t in current.Tasks) {
            if (string.IsNullOrWhiteSpace(t.Text)) {
                continue;
            }
            if (!mergedByText.TryGetValue(t.Text, out var prev)) {
                mergedByText[t.Text] = t;
                continue;
            }
            var url = !string.IsNullOrWhiteSpace(prev.Url) ? prev.Url : t.Url;
            mergedByText[t.Text] = new TaskItem(prev.Checked || t.Checked, t.Text, url);
        }

        // Stable ordering: keep existing order first, then new tasks.
        var ordered = new List<TaskItem>();
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in existing.Tasks) {
            if (mergedByText.TryGetValue(t.Text, out var merged) && seenKeys.Add(merged.Text)) {
                ordered.Add(merged);
            }
        }
        foreach (var t in current.Tasks) {
            if (mergedByText.TryGetValue(t.Text, out var merged) && seenKeys.Add(merged.Text)) {
                ordered.Add(merged);
            }
        }

        return current with { Tasks = ordered };
    }

    private static int FindInsertPoint(string section) {
        var firstDetails = section.IndexOf("<details>", StringComparison.Ordinal);
        if (firstDetails >= 0) {
            return firstDetails;
        }
        // Insert at end of section (keep trailing newline behavior stable).
        return section.Length;
    }

    // Internal for tests.
    internal static string RenderPrBlock(PrTasks pr, string newline) {
        var safeSummary = HtmlEscape($"PR #{pr.Number} {pr.Title}".Trim());
        var sb = new StringBuilder();
        sb.Append("<details>").Append(newline);
        sb.Append("<summary>").Append(safeSummary).Append("</summary>").Append(newline);
        sb.Append(newline);
        foreach (var t in pr.Tasks) {
            var state = t.Checked ? "x" : " ";
            if (string.IsNullOrWhiteSpace(t.Url)) {
                sb.Append("- [").Append(state).Append("] ").Append(t.Text).Append(newline);
            } else {
                sb.Append("- [").Append(state).Append("] ").Append(t.Text).Append(". Links: ").Append(t.Url).Append(newline);
            }
        }
        sb.Append("</details>").Append(newline);
        sb.Append(newline);
        return sb.ToString();
    }

    private static string HtmlEscape(string text) {
        return text
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static (string Owner, string Name) SplitRepo(string repo) {
        var parts = repo.Split('/', 2);
        return (parts[0], parts[1]);
    }

    private static string BuildTaskId(int prNumber, string url, string text) {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes($"{prNumber}|{url}|{text}"));
        return Convert.ToHexString(bytes).Substring(0, 12).ToLowerInvariant();
    }

    private static string BuildIssueTitle(int prNumber, string prTitle, string taskText) {
        var prefix = $"Bot feedback (PR #{prNumber})";
        var trimmedTask = taskText.Trim();
        if (trimmedTask.Length > 90) {
            trimmedTask = trimmedTask.Substring(0, 90) + "…";
        }
        return string.IsNullOrWhiteSpace(prTitle)
            ? $"{prefix}: {trimmedTask}"
            : $"{prefix}: {trimmedTask}";
    }

    private static string BuildIssueBody(PrTasks pr, TaskItem task, string id) {
        var sb = new StringBuilder();
        sb.AppendLine($"Bot checklist item from PR #{pr.Number}");
        sb.AppendLine();
        sb.AppendLine("Task:");
        sb.AppendLine($"- [ ] {task.Text}");
        sb.AppendLine();
        sb.AppendLine($"Source: {task.Url}");
        if (!string.IsNullOrWhiteSpace(pr.Url)) {
            sb.AppendLine($"PR: {pr.Url}");
        }
        sb.AppendLine();
        sb.AppendLine($"ix-bot-feedback-id:{id}");
        return sb.ToString();
    }

    private static async Task<bool> IssueExistsAsync(string repo, string id) {
        var query = $"ix-bot-feedback-id:{id}";
        var (code, stdout, _) = await GhCli.RunAsync(
            "issue", "list",
            "--repo", repo,
            "--search", query,
            "--limit", "1",
            "--json", "number"
        ).ConfigureAwait(false);
        if (code != 0) {
            return false;
        }
        try {
            using var doc = JsonDocument.Parse(stdout);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0;
        } catch {
            return false;
        }
    }

    private static async Task EnsureLabelAsync(string repo, string label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return;
        }
        var (code, stdout, _) = await GhCli.RunAsync("label", "list", "--repo", repo, "--limit", "200", "--json", "name")
            .ConfigureAwait(false);
        if (code == 0) {
            try {
                using var doc = JsonDocument.Parse(stdout);
                if (doc.RootElement.ValueKind == JsonValueKind.Array) {
                    foreach (var item in doc.RootElement.EnumerateArray()) {
                        var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                        if (!string.IsNullOrWhiteSpace(name) && name.Equals(label, StringComparison.OrdinalIgnoreCase)) {
                            return;
                        }
                    }
                }
            } catch {
                // ignore
            }
        }
        // Best-effort label creation. If it fails due to permissions, issue creation will still run without label validation.
        await GhCli.RunAsync(
            "label", "create", label,
            "--repo", repo,
            "--color", "0e8a16",
            "--description", "Checklist items imported from bot PR reviews/comments"
        ).ConfigureAwait(false);
    }
}
