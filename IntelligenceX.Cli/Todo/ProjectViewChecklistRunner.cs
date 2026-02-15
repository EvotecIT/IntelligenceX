using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class ProjectViewChecklistRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    internal const string CommentMarker = "<!-- intelligencex:project-view-checklist -->";
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private const string DefaultConfigPath = "artifacts/triage/ix-project-config.json";
    private const string DefaultOutputPath = "artifacts/triage/ix-project-view-checklist.md";
    private const string DefaultIssueTitle = "IX Project View Checklist";

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public string? Owner { get; set; }
        public int? ProjectNumber { get; set; }
        public string ConfigPath { get; set; } = DefaultConfigPath;
        public string OutputPath { get; set; } = DefaultOutputPath;
        public bool Print { get; set; }
        public int? IssueNumber { get; set; }
        public bool CreateIssue { get; set; }
        public string IssueTitle { get; set; } = DefaultIssueTitle;
        public bool ShowHelp { get; set; }
        public bool ParseFailed { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return options.ParseFailed ? 1 : 0;
        }

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        if (!TryResolveProjectTarget(options, out var owner, out var projectNumber, out var repo, out var resolveError)) {
            Console.Error.WriteLine(resolveError);
            return 1;
        }

        var client = new ProjectV2Client();
        ProjectV2Client.ProjectRef project;
        try {
            project = await client.TryGetProjectAsync(owner, projectNumber).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Project {projectNumber} was not found for owner '{owner}'.");
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        IReadOnlyDictionary<string, ProjectV2Client.ProjectView> views;
        try {
            views = await client.GetProjectViewsByNameAsync(owner, projectNumber).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to list project views: {ex.Message}");
            return 1;
        }

        var markdown = BuildChecklistMarkdown(
            repo,
            owner,
            projectNumber,
            project.Url,
            views,
            DateTimeOffset.UtcNow);

        WriteText(options.OutputPath, markdown);

        int? issueNumber = null;
        var issueCreated = false;
        var commentUpserted = false;

        if (options.CreateIssue) {
            var createResult = await CreateIssueAsync(repo, options.IssueTitle, markdown).ConfigureAwait(false);
            if (!createResult.Success) {
                Console.Error.WriteLine(createResult.Error);
                return 1;
            }
            issueNumber = createResult.IssueNumber;
            issueCreated = true;
        } else if (options.IssueNumber.HasValue) {
            issueNumber = options.IssueNumber.Value;
            commentUpserted = await IssueSuggestionCommentManager.UpsertAsync(
                repo,
                issueNumber.Value,
                markdown,
                CommentMarker).ConfigureAwait(false);
        }

        if (options.Print) {
            Console.WriteLine(markdown);
        }

        var missing = ProjectViewCatalog.FindMissingDefaultViews(views);
        var present = ProjectViewCatalog.DefaultViews.Count - missing.Count;

        Console.WriteLine($"Project view checklist target: {owner}#{projectNumber} ({project.Url})");
        Console.WriteLine($"Default view coverage: {present}/{ProjectViewCatalog.DefaultViews.Count}");
        Console.WriteLine($"Views discovered: {views.Count}");
        Console.WriteLine($"Checklist output: {options.OutputPath}");
        if (issueCreated && issueNumber.HasValue) {
            Console.WriteLine($"Checklist issue created: #{issueNumber.Value}");
        } else if (issueNumber.HasValue) {
            Console.WriteLine($"Checklist comment upsert target: issue #{issueNumber.Value}");
            Console.WriteLine(commentUpserted
                ? "Checklist comment upserted."
                : "Warning: checklist comment upsert failed.");
        }

        return 0;
    }

    internal static string BuildChecklistMarkdown(
        string repo,
        string owner,
        int projectNumber,
        string projectUrl,
        IReadOnlyDictionary<string, ProjectV2Client.ProjectView> viewsByName,
        DateTimeOffset generatedAtUtc) {
        var missing = ProjectViewCatalog.FindMissingDefaultViews(viewsByName);
        var present = ProjectViewCatalog.DefaultViews.Count - missing.Count;

        var builder = new StringBuilder();
        builder.AppendLine(CommentMarker);
        builder.AppendLine("# IX Project View Checklist");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {generatedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"- Repo: `{repo}`");
        builder.AppendLine($"- Project: `{owner}#{projectNumber}`");
        if (!string.IsNullOrWhiteSpace(projectUrl)) {
            builder.AppendLine($"- Project URL: {projectUrl}");
        }
        builder.AppendLine($"- Default view coverage: {present}/{ProjectViewCatalog.DefaultViews.Count}");
        builder.AppendLine();
        builder.AppendLine("## Default Views");
        builder.AppendLine();

        foreach (var view in ProjectViewCatalog.DefaultViews) {
            if (viewsByName.TryGetValue(view.Name, out var existing)) {
                builder.AppendLine($"- [x] **{view.Name}** (`{view.Layout}`) - present");
                if (!string.IsNullOrWhiteSpace(existing.Url)) {
                    builder.AppendLine($"  Link: {existing.Url}");
                }
            } else {
                builder.AppendLine($"- [ ] **{view.Name}** (`{view.Layout}`) - missing");
                builder.AppendLine($"  Suggested filter: `{view.Filter}`");
                builder.AppendLine($"  Purpose: {view.Description}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## How To Complete Missing Views");
        builder.AppendLine();
        builder.AppendLine($"1. Open project: {projectUrl}");
        builder.AppendLine("2. Select `+ New view` in GitHub Projects.");
        builder.AppendLine("3. Use the exact view name/layout and suggested filter from checklist items above.");
        builder.AppendLine("4. Re-run `intelligencex todo project-view-checklist` to refresh coverage.");

        return builder.ToString().TrimEnd();
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();
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
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--owner":
                    if (i + 1 < args.Length) {
                        options.Owner = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--project":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var projectNumber) &&
                        projectNumber > 0) {
                        options.ProjectNumber = projectNumber;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--config":
                    if (i + 1 < args.Length) {
                        options.ConfigPath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--out":
                    if (i + 1 < args.Length) {
                        options.OutputPath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--print":
                    options.Print = true;
                    break;
                case "--issue":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var issueNumber) &&
                        issueNumber > 0) {
                        options.IssueNumber = issueNumber;
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--create-issue":
                    options.CreateIssue = true;
                    break;
                case "--issue-title":
                    if (i + 1 < args.Length) {
                        options.IssueTitle = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ParseFailed = true;
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (options.CreateIssue && options.IssueNumber.HasValue) {
            Console.Error.WriteLine("Choose either --issue <n> or --create-issue, not both.");
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (string.IsNullOrWhiteSpace(options.IssueTitle)) {
            options.IssueTitle = DefaultIssueTitle;
        }

        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo project-view-checklist [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --owner <login>         Project owner login (required unless --config resolves it)");
        Console.WriteLine("  --project <n>           Project number (required unless --config resolves it)");
        Console.WriteLine("  --repo <owner/name>     Repository context (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --config <path>         Project config JSON from project-init (default: artifacts/triage/ix-project-config.json)");
        Console.WriteLine("  --out <path>            Checklist markdown output path (default: artifacts/triage/ix-project-view-checklist.md)");
        Console.WriteLine("  --print                 Print checklist markdown to stdout");
        Console.WriteLine("  --issue <n>             Upsert checklist comment on an existing issue");
        Console.WriteLine("  --create-issue          Create a checklist issue with rendered checklist body");
        Console.WriteLine("  --issue-title <text>    Checklist issue title when --create-issue is used");
        Console.WriteLine();
        Console.WriteLine("Required token scopes: `project` and issue write access for issue posting.");
    }

    private static bool TryResolveProjectTarget(
        Options options,
        out string owner,
        out int projectNumber,
        out string repo,
        out string error) {
        owner = options.Owner?.Trim() ?? string.Empty;
        projectNumber = options.ProjectNumber ?? 0;
        repo = options.Repo?.Trim() ?? string.Empty;
        error = string.Empty;

        if (!string.IsNullOrWhiteSpace(owner) && projectNumber > 0) {
            return true;
        }

        if (!File.Exists(options.ConfigPath)) {
            error = "Owner/project not provided and project config file was not found. Use --owner/--project or run `todo project-init` first.";
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(options.ConfigPath));
            var root = doc.RootElement;

            if (string.IsNullOrWhiteSpace(owner)) {
                owner = ReadString(root, "owner");
            }

            if (projectNumber <= 0 &&
                TryGetProperty(root, "project", out var projectObj) &&
                projectObj.ValueKind == JsonValueKind.Object) {
                projectNumber = ReadInt(projectObj, "number");
            }

            if (string.IsNullOrWhiteSpace(repo)) {
                repo = ReadString(root, "repo");
            }
        } catch (Exception ex) {
            error = $"Failed to parse project config at {options.ConfigPath}: {ex.Message}";
            return false;
        }

        if (string.IsNullOrWhiteSpace(owner) || projectNumber <= 0) {
            error = "Unable to resolve owner/project from arguments or config.";
            return false;
        }

        return true;
    }

    private static async Task<(bool Success, int IssueNumber, string Error)> CreateIssueAsync(
        string repo,
        string title,
        string body) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(90),
            "issue", "create",
            "--repo", repo,
            "--title", title,
            "--body", body).ConfigureAwait(false);

        if (code != 0) {
            return (
                false,
                0,
                $"Failed to create checklist issue in '{repo}': {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
        }

        if (!ProjectBootstrapRunner.TryParseIssueNumberFromGhOutput(stdout, out var issueNumber)) {
            return (false, 0, "Checklist issue was created but issue number could not be parsed from `gh issue create` output.");
        }

        return (true, issueNumber, string.Empty);
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value) {
        value = default;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value);
    }

    private static string ReadString(JsonElement root, string name) {
        if (!TryGetProperty(root, name, out var value) || value.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return value.GetString() ?? string.Empty;
    }

    private static int ReadInt(JsonElement root, string name) {
        if (!TryGetProperty(root, name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) {
            return 0;
        }
        return number;
    }

    private static void WriteText(string path, string content) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }
}
