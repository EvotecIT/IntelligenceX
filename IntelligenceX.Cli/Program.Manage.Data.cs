using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.Auth;
using IntelligenceX.OpenAI.Auth;
using Spectre.Console;

namespace IntelligenceX.Cli;

internal static partial class Program {
    private static Panel BuildRecentActivityPanel(ManageState state) {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("k")
            .AddColumn("v");

        if (string.IsNullOrWhiteSpace(state.LastProcessName)) {
            table.AddRow("Last process", "(none yet)");
            table.AddRow("Status", "-");
            table.AddRow("When", "-");
            table.AddRow("Duration", "-");
        } else {
            table.AddRow("Last process", Markup.Escape(state.LastProcessName!));
            table.AddRow("Status", state.LastExitCode == 0 ? "[green]success[/]" : "[red]failed[/]");
            table.AddRow("When", state.LastRunAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "-");
            table.AddRow("Duration", state.LastRunDuration.HasValue ? FormatDuration(state.LastRunDuration.Value) : "-");
            table.AddRow("Steps", state.LastStepCount.ToString());
        }

        return new Panel(table) {
            Header = new PanelHeader($"{Icon("process")} Recent Activity"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

    private static Panel BuildBundlePanel() {
        var bundles = ReadOpenAiBundles();
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Account")
            .AddColumn("Expires (UTC)")
            .AddColumn("State");

        if (bundles.Count == 0) {
            table.AddRow("(none)", "(n/a)", "[yellow]missing[/]");
        } else {
            foreach (var bundle in bundles.OrderBy(b => b.ExpiresAt ?? DateTimeOffset.MaxValue)) {
                var account = string.IsNullOrWhiteSpace(bundle.AccountId) ? "-" : bundle.AccountId!;
                var expiry = bundle.ExpiresAt.HasValue ? bundle.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-";
                table.AddRow(Markup.Escape(account), expiry, GetBundleStateMarkup(bundle.ExpiresAt));
            }
        }

        return new Panel(table) {
            Header = new PanelHeader($"{Icon("box")} OpenAI Bundles"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

    private static string GetBundleStateMarkup(DateTimeOffset? expiresAt) {
        if (!expiresAt.HasValue) {
            return "[yellow]unknown[/]";
        }
        var now = DateTimeOffset.UtcNow;
        if (expiresAt.Value <= now) {
            return "[red]expired[/]";
        }
        if (expiresAt.Value <= now.AddHours(24)) {
            return "[yellow]expiring soon[/]";
        }
        return "[green]valid[/]";
    }

    private static void ShowAuthBundlesDetailed() {
        AnsiConsole.Clear();
        RenderTitle($"{Icon("box")} OpenAI Bundle Details");
        var bundles = ReadOpenAiBundles();
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Provider")
            .AddColumn("Account")
            .AddColumn("Expires (UTC)")
            .AddColumn("State");

        if (bundles.Count == 0) {
            table.AddRow("openai-codex", "(none)", "-", "[yellow]missing[/]");
        } else {
            foreach (var bundle in bundles.OrderBy(b => b.ExpiresAt ?? DateTimeOffset.MaxValue)) {
                table.AddRow(
                    Markup.Escape(bundle.Provider),
                    Markup.Escape(bundle.AccountId ?? "-"),
                    bundle.ExpiresAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "-",
                    GetBundleStateMarkup(bundle.ExpiresAt));
            }
        }

        AnsiConsole.Write(table);
    }

    private static string? PromptRepository(string title, bool required, string? fallback) {
        var defaultRepo = !string.IsNullOrWhiteSpace(fallback) ? fallback : ResolveDefaultRepo();
        var prompt = new TextPrompt<string>($"{title} [grey](owner/name)[/]")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(defaultRepo)) {
            prompt = prompt.DefaultValue(defaultRepo!);
        }

        while (true) {
            var raw = AnsiConsole.Prompt(prompt).Trim();
            if (string.IsNullOrWhiteSpace(raw)) {
                if (required) {
                    AnsiConsole.MarkupLine("[yellow]Repository is required for this action.[/]");
                    continue;
                }
                return null;
            }
            var repo = NormalizeRepo(raw);
            if (TryParseRepo(repo, out _, out _)) {
                return repo;
            }
            AnsiConsole.MarkupLine("[yellow]Repository must be in owner/name format.[/]");
        }
    }

    private static int PromptPositiveInt(string title) {
        while (true) {
            var raw = AnsiConsole.Prompt(new TextPrompt<string>($"{title}").AllowEmpty()).Trim();
            if (int.TryParse(raw, out var value) && value > 0) {
                return value;
            }
            AnsiConsole.MarkupLine("[yellow]Please enter a positive integer.[/]");
        }
    }

    private static void SetActiveRepo(ManageState state, string? repo) {
        SetActiveRepo(state, repo);
        var owner = TryGetOwner(repo);
        if (!string.IsNullOrWhiteSpace(owner)) {
            state.LastOwner = owner;
        }
    }

    private sealed record CommandError(string Command, int ExitCode, string StdErr, string StdOut);
    private sealed record FetchResult<T>(List<T>? Items, CommandError? Error);

    private static FetchResult<PullRequestSummary> FetchPullRequests(string repo, string state, int limit) {
        var cmd =
            $"pr list --repo {repo} --state {state} --limit {limit} --json number,title,state,isDraft,headRefName,updatedAt,url";
        var result = RunExternalCommand("gh", cmd);
        if (result.ExitCode != 0) {
            return new FetchResult<PullRequestSummary>(
                null,
                new CommandError($"gh {cmd}", result.ExitCode, result.StdErr, result.StdOut));
        }

        try {
            var items = ParsePullRequestList(result.StdOut);
            return new FetchResult<PullRequestSummary>(items, null);
        } catch (Exception ex) {
            return new FetchResult<PullRequestSummary>(
                null,
                new CommandError($"gh {cmd}", 1, $"Failed to parse PR list JSON: {ex.Message}", result.StdOut));
        }
    }

    private static FetchResult<WorkflowRunSummary> FetchWorkflowRuns(string repo, string workflowFile, int limit) {
        var cmd =
            $"run list --repo {repo} --workflow {workflowFile} --limit {limit} --json databaseId,status,conclusion,displayTitle,headBranch,event,createdAt,url";
        var result = RunExternalCommand("gh", cmd);
        if (result.ExitCode != 0) {
            return new FetchResult<WorkflowRunSummary>(
                null,
                new CommandError($"gh {cmd}", result.ExitCode, result.StdErr, result.StdOut));
        }

        try {
            var items = ParseWorkflowRuns(result.StdOut);
            return new FetchResult<WorkflowRunSummary>(items, null);
        } catch (Exception ex) {
            return new FetchResult<WorkflowRunSummary>(
                null,
                new CommandError($"gh {cmd}", 1, $"Failed to parse workflow runs JSON: {ex.Message}", result.StdOut));
        }
    }

    private static FetchResult<PullRequestCheckSummary> FetchPullRequestChecks(string repo, int prNumber) {
        var cmd =
            $"pr checks --repo {repo} {prNumber} --json name,state,link,bucket,event,workflow";
        var result = RunExternalCommand("gh", cmd);
        if (result.ExitCode != 0) {
            return new FetchResult<PullRequestCheckSummary>(
                null,
                new CommandError($"gh {cmd}", result.ExitCode, result.StdErr, result.StdOut));
        }

        try {
            var items = ParsePullRequestChecks(result.StdOut);
            return new FetchResult<PullRequestCheckSummary>(items, null);
        } catch (Exception ex) {
            return new FetchResult<PullRequestCheckSummary>(
                null,
                new CommandError($"gh {cmd}", 1, $"Failed to parse PR checks JSON: {ex.Message}", result.StdOut));
        }
    }

    private static List<PullRequestSummary> ParsePullRequestList(string json) {
        var list = new List<PullRequestSummary>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return list;
        }

        foreach (var item in doc.RootElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }

            var number = item.TryGetProperty("number", out var numberProp) && numberProp.TryGetInt32(out var n) ? n : 0;
            var title = item.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                ? titleProp.GetString() ?? string.Empty
                : string.Empty;
            var state = item.TryGetProperty("state", out var stateProp) && stateProp.ValueKind == JsonValueKind.String
                ? stateProp.GetString() ?? "UNKNOWN"
                : "UNKNOWN";
            var isDraft = item.TryGetProperty("isDraft", out var draftProp) && draftProp.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                          draftProp.GetBoolean();
            var head = item.TryGetProperty("headRefName", out var headProp) && headProp.ValueKind == JsonValueKind.String
                ? headProp.GetString()
                : null;
            var updated = ParseDate(item, "updatedAt");
            var url = item.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString()
                : null;

            if (number > 0) {
                list.Add(new PullRequestSummary(number, title, state, isDraft, head, updated, url));
            }
        }
        return list;
    }

    private static List<WorkflowRunSummary> ParseWorkflowRuns(string json) {
        var list = new List<WorkflowRunSummary>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return list;
        }

        foreach (var item in doc.RootElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }

            long id = item.TryGetProperty("databaseId", out var idProp) && idProp.TryGetInt64(out var parsedId) ? parsedId : 0;
            var status = GetString(item, "status");
            var conclusion = GetString(item, "conclusion");
            var title = GetString(item, "displayTitle");
            var branch = GetString(item, "headBranch");
            var evt = GetString(item, "event");
            var created = ParseDate(item, "createdAt");
            var url = GetString(item, "url");

            list.Add(new WorkflowRunSummary(id, status, conclusion, title, branch, evt, created, url));
        }
        return list;
    }

    private static List<PullRequestCheckSummary> ParsePullRequestChecks(string json) {
        var list = new List<PullRequestCheckSummary>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return list;
        }

        foreach (var item in doc.RootElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }
            list.Add(new PullRequestCheckSummary(
                GetString(item, "name"),
                GetString(item, "workflow"),
                GetString(item, "state"),
                GetString(item, "bucket"),
                GetString(item, "event"),
                GetString(item, "link")));
        }
        return list;
    }

    private static string? GetString(JsonElement obj, string propertyName) {
        if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return null;
        }
        return prop.GetString();
    }

    private static DateTimeOffset? ParseDate(JsonElement obj, string propertyName) {
        var raw = GetString(obj, propertyName);
        if (string.IsNullOrWhiteSpace(raw)) {
            return null;
        }
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static async Task<int?> PromptPullRequestNumberAsync(string repo) {
        var open = await RunWithSpinnerAsync($"Loading PR candidates for {repo}...",
            () => FetchPullRequests(repo, "all", 20)).ConfigureAwait(false);
        if (open.Error is not null || open.Items is null || open.Items.Count == 0) {
            return PromptPrNumberManual();
        }

        var choices = new List<string>();
        var mapping = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pr in open.Items.OrderByDescending(p => p.UpdatedAt ?? DateTimeOffset.MinValue).Take(15)) {
            var label =
                $"#{pr.Number} {CompactValue(pr.Title, 62)} [{pr.State?.ToLowerInvariant() ?? "unknown"}]";
            choices.Add(label);
            mapping[label] = pr.Number;
        }
        var manualLabel = "Enter PR number manually...";
        var cancelLabel = "Cancel";
        choices.Add(manualLabel);
        choices.Add(cancelLabel);

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select pull request")
                .PageSize(18)
                .AddChoices(choices));
        if (string.Equals(selected, cancelLabel, StringComparison.Ordinal)) {
            return null;
        }
        if (string.Equals(selected, manualLabel, StringComparison.Ordinal)) {
            return PromptPrNumberManual();
        }
        return mapping.TryGetValue(selected, out var number) ? number : null;
    }

    private static int? PromptPrNumberManual() {
        while (true) {
            var raw = AnsiConsole.Prompt(new TextPrompt<string>("Pull request number [grey](blank to cancel)[/]").AllowEmpty())
                .Trim();
            if (string.IsNullOrWhiteSpace(raw)) {
                return null;
            }
            if (int.TryParse(raw, out var num) && num > 0) {
                return num;
            }
            AnsiConsole.MarkupLine("[yellow]Enter a valid positive PR number.[/]");
        }
    }

    private static async Task<T> RunWithSpinnerAsync<T>(string statusMessage, Func<T> work) {
        T result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync(statusMessage, _ => {
                result = work();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        return result;
    }

    private static void RenderCommandError(string title, CommandError error) {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("k")
            .AddColumn("v");
        table.AddRow("Title", Markup.Escape(title));
        table.AddRow("Command", Markup.Escape(error.Command));
        table.AddRow("Exit code", error.ExitCode.ToString());
        table.AddRow("stderr", Markup.Escape(CompactValue(string.IsNullOrWhiteSpace(error.StdErr) ? "(none)" : error.StdErr.Trim(), 380)));
        table.AddRow("stdout", Markup.Escape(CompactValue(string.IsNullOrWhiteSpace(error.StdOut) ? "(none)" : error.StdOut.Trim(), 380)));

        AnsiConsole.Write(new Panel(table) {
            Header = new PanelHeader($"{Icon("fail")} Command Error"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red)
        });
    }

    private static string GetPrStateMarkup(string? state) {
        return (state ?? string.Empty).ToUpperInvariant() switch {
            "OPEN" => "[green]open[/]",
            "MERGED" => "[blue]merged[/]",
            "CLOSED" => "[yellow]closed[/]",
            _ => Markup.Escape(state ?? "unknown")
        };
    }

    private static string GetCheckStateMarkup(string? state) {
        return (state ?? string.Empty).ToUpperInvariant() switch {
            "SUCCESS" => "[green]success[/]",
            "SKIPPED" => "[grey]skipped[/]",
            "PENDING" => "[yellow]pending[/]",
            "QUEUED" => "[yellow]queued[/]",
            "IN_PROGRESS" => "[yellow]in_progress[/]",
            "FAILURE" => "[red]failure[/]",
            "ERROR" => "[red]error[/]",
            "CANCELLED" => "[grey]cancelled[/]",
            _ => Markup.Escape(state ?? "unknown")
        };
    }

    private static string GetRunStatusMarkup(string? status, string? conclusion) {
        var statusNormalized = (status ?? string.Empty).ToLowerInvariant();
        if (string.Equals(statusNormalized, "completed", StringComparison.Ordinal)) {
            return GetCheckStateMarkup(conclusion);
        }
        return statusNormalized switch {
            "in_progress" => "[yellow]in_progress[/]",
            "queued" => "[yellow]queued[/]",
            "requested" => "[yellow]requested[/]",
            "waiting" => "[yellow]waiting[/]",
            _ => Markup.Escape(string.IsNullOrWhiteSpace(status) ? "unknown" : status!)
        };
    }

    private static Task<string?> PromptRepositoryFromGitHubAsync(string? fallbackRepo, string? ownerHint) {
        var ownerDefault = ownerHint ?? TryGetOwner(fallbackRepo) ?? TryGetOwner(ResolveDefaultRepo()) ?? string.Empty;
        var owner = AnsiConsole.Prompt(
            new TextPrompt<string>("GitHub owner/org")
                .DefaultValue(ownerDefault)
                .AllowEmpty());

        owner = owner.Trim();
        if (string.IsNullOrWhiteSpace(owner)) {
            AnsiConsole.MarkupLine("[yellow]Owner/org is required to browse repositories.[/]");
            return Task.FromResult<string?>(null);
        }

        var result = RunExternalCommand("gh", $"repo list {owner} --limit 100 --json nameWithOwner,pushedAt,isPrivate");
        if (result.ExitCode != 0) {
            AnsiConsole.MarkupLine("[yellow]Could not load repositories from GitHub. Falling back to manual entry.[/]");
            return Task.FromResult(PromptRepository("Set active repository", required: false, fallbackRepo));
        }

        List<(string Repo, DateTimeOffset? PushedAt, bool IsPrivate)> repos;
        try {
            repos = ParseRepoList(result.StdOut);
        } catch {
            AnsiConsole.MarkupLine("[yellow]Failed to parse GitHub repository list. Falling back to manual entry.[/]");
            return Task.FromResult(PromptRepository("Set active repository", required: false, fallbackRepo));
        }

        if (repos.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No repositories returned for this owner/org.[/]");
            return Task.FromResult(PromptRepository("Set active repository", required: false, fallbackRepo));
        }

        var labels = new List<string>();
        var mapping = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var repo in repos
                     .OrderByDescending(r => r.PushedAt ?? DateTimeOffset.MinValue)
                     .Take(40)) {
            var pushed = repo.PushedAt?.ToString("yyyy-MM-dd") ?? "unknown";
            var privacy = repo.IsPrivate ? "private" : "public";
            var label = $"{repo.Repo}  [{privacy}, pushed {pushed}]";
            labels.Add(label);
            mapping[label] = repo.Repo;
        }

        var manual = "Enter repository manually...";
        labels.Add(manual);

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select repository")
                .PageSize(18)
                .AddChoices(labels));

        if (string.Equals(selected, manual, StringComparison.Ordinal)) {
            return Task.FromResult(PromptRepository("Set active repository", required: false, fallbackRepo));
        }
        return Task.FromResult(mapping.TryGetValue(selected, out var resolved) ? resolved : fallbackRepo);
    }

}
