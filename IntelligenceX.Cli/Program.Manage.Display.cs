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
    private static void RenderPullRequestReadinessSummary(string repo, int prNumber,
        FetchResult<PullRequestCheckSummary> checksResult) {
        AnsiConsole.Clear();
        RenderTitle($"{Icon("tool")} PR #{prNumber} Readiness ({repo})");
        if (checksResult.Error is not null) {
            RenderCommandError("Failed to fetch pull request checks", checksResult.Error);
            return;
        }

        var checks = checksResult.Items ?? new List<PullRequestCheckSummary>();
        if (checks.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No checks found. Treat this PR as not ready until checks appear.[/]");
            return;
        }

        var success = checks.Count(c => string.Equals(c.State, "SUCCESS", StringComparison.OrdinalIgnoreCase));
        var pending = checks.Count(c =>
            string.Equals(c.State, "PENDING", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "IN_PROGRESS", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "QUEUED", StringComparison.OrdinalIgnoreCase));
        var failed = checks.Count(c =>
            string.Equals(c.State, "FAILURE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "ERROR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "TIMED_OUT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase));

        var summary = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Readiness")
            .AddColumn("Count");
        summary.AddRow("[green]Success[/]", success.ToString());
        summary.AddRow("[yellow]Pending[/]", pending.ToString());
        summary.AddRow("[red]Failed[/]", failed.ToString());
        summary.AddRow("Total", checks.Count.ToString());
        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();

        var readiness = failed > 0
            ? "[red]NOT READY[/]"
            : pending > 0 ? "[yellow]WAITING[/]" : "[green]READY[/]";
        AnsiConsole.MarkupLine($"Final status: {readiness}");

        var topIssues = checks
            .Where(c =>
                string.Equals(c.State, "FAILURE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.State, "ERROR", StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();
        if (topIssues.Count > 0) {
            AnsiConsole.WriteLine();
            var issues = new Table()
                .RoundedBorder()
                .BorderColor(Color.Grey)
                .AddColumn("Workflow")
                .AddColumn("Check")
                .AddColumn("State");
            foreach (var item in topIssues) {
                issues.AddRow(
                    Markup.Escape(CompactValue(item.Workflow ?? "-", 28)),
                    Markup.Escape(CompactValue(item.Name ?? "-", 44)),
                    GetCheckStateMarkup(item.State));
            }
            AnsiConsole.Write(issues);
        }
    }

    private static async Task ShowPullRequestListAsync(ManageState state, string prState, string title) {
        var repo = PromptRepository("Repository", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);

        var result = await RunWithSpinnerAsync($"Loading PRs from {repo}...",
            () => FetchPullRequestsAsync(repo, prState, 20)).ConfigureAwait(false);

        AnsiConsole.Clear();
        RenderTitle($"{Icon("repo")} {title} ({repo})");
        if (result.Error is not null) {
            RenderCommandError("Failed to load pull requests", result.Error);
            PauseForMenu();
            return;
        }

        if (result.Items is null || result.Items.Count == 0) {
            if (string.Equals(prState, "open", StringComparison.OrdinalIgnoreCase)) {
                state.LastOpenPullRequestCount = 0;
                state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            AnsiConsole.MarkupLine("[yellow]No pull requests found.[/]");
            PauseForMenu();
            return;
        }

        if (string.Equals(prState, "open", StringComparison.OrdinalIgnoreCase)) {
            state.LastOpenPullRequestCount = result.Items.Count;
            state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("PR")
            .AddColumn("State")
            .AddColumn("Branch")
            .AddColumn("Updated")
            .AddColumn("Title");
        foreach (var pr in result.Items.OrderByDescending(p => p.UpdatedAt ?? DateTimeOffset.MinValue)) {
            var stateLabel = pr.IsDraft ? "[yellow]draft[/]" : GetPrStateMarkup(pr.State);
            var updated = pr.UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-";
            var branch = string.IsNullOrWhiteSpace(pr.HeadRefName) ? "-" : pr.HeadRefName!;
            table.AddRow(
                $"#{pr.Number}",
                stateLabel,
                Markup.Escape(CompactValue(branch, 28)),
                updated,
                Markup.Escape(CompactValue(pr.Title, 64)));
        }
        AnsiConsole.Write(table);
        PauseForMenu();
    }

    private static async Task ShowPullRequestChecksAsync(ManageState state) {
        var repo = PromptRepository("Repository", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);

        var prNumber = await PromptPullRequestNumberAsync(repo).ConfigureAwait(false);
        if (!prNumber.HasValue) {
            return;
        }

        var checks = await RunWithSpinnerAsync($"Loading checks for PR #{prNumber.Value}...",
            () => FetchPullRequestChecksAsync(repo, prNumber.Value)).ConfigureAwait(false);

        AnsiConsole.Clear();
        RenderTitle($"{Icon("check")} PR #{prNumber.Value} Checks ({repo})");
        if (checks.Error is not null) {
            RenderCommandError("Failed to load pull request checks", checks.Error);
            PauseForMenu();
            return;
        }

        if (checks.Items is null || checks.Items.Count == 0) {
            state.LastFailedCheckCount = 0;
            state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
            AnsiConsole.MarkupLine("[yellow]No checks found for this pull request.[/]");
            PauseForMenu();
            return;
        }

        state.LastFailedCheckCount = checks.Items.Count(c =>
            string.Equals(c.State, "FAILURE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "ERROR", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "TIMED_OUT", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.State, "ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase));
        state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;

        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Workflow")
            .AddColumn("Name")
            .AddColumn("State")
            .AddColumn("Bucket");
        foreach (var check in checks.Items) {
            table.AddRow(
                Markup.Escape(CompactValue(check.Workflow ?? "-", 30)),
                Markup.Escape(CompactValue(check.Name ?? "-", 42)),
                GetCheckStateMarkup(check.State),
                Markup.Escape(check.Bucket ?? "-"));
        }
        AnsiConsole.Write(table);
        PauseForMenu();
    }

    private static async Task ShowRecentReviewerRunsAsync(ManageState state) {
        var repo = PromptRepository("Repository", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);

        var runs = await RunWithSpinnerAsync($"Loading reviewer workflow runs for {repo}...",
            () => FetchWorkflowRunsAsync(repo, "review-intelligencex.yml", 15)).ConfigureAwait(false);

        AnsiConsole.Clear();
        RenderTitle($"{Icon("process")} Recent Reviewer Runs ({repo})");
        if (runs.Error is not null) {
            RenderCommandError("Failed to load workflow runs", runs.Error);
            PauseForMenu();
            return;
        }

        if (runs.Items is null || runs.Items.Count == 0) {
            AnsiConsole.MarkupLine("[yellow]No reviewer workflow runs found.[/]");
            PauseForMenu();
            return;
        }

        var latestRun = runs.Items.OrderByDescending(r => r.CreatedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
        if (latestRun is not null) {
            var latestStatus = string.Equals(latestRun.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? latestRun.Conclusion
                : latestRun.Status;
            state.LastReviewerRunStatus = latestStatus ?? "unknown";
            state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Run")
            .AddColumn("Status")
            .AddColumn("Branch")
            .AddColumn("Event")
            .AddColumn("Created")
            .AddColumn("Title");
        foreach (var run in runs.Items.OrderByDescending(r => r.CreatedAt ?? DateTimeOffset.MinValue)) {
            var runLabel = run.DatabaseId > 0 ? run.DatabaseId.ToString() : "-";
            table.AddRow(
                runLabel,
                GetRunStatusMarkup(run.Status, run.Conclusion),
                Markup.Escape(CompactValue(run.HeadBranch ?? "-", 22)),
                Markup.Escape(run.Event ?? "-"),
                run.CreatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-",
                Markup.Escape(CompactValue(run.DisplayTitle ?? "-", 44)));
        }
        AnsiConsole.Write(table);
        PauseForMenu();
    }

    private static async Task HandleRepositoryMenuAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("repo")} Repository Management");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<RepositoryAction>()
                    .Title("Choose repository action")
                    .UseConverter(x => x switch {
                        RepositoryAction.SetManually => $"{Icon("repo")} Set active repository manually",
                        RepositoryAction.SelectFromGitHub => $"{Icon("gh")} Select repository from GitHub",
                        RepositoryAction.ClearActiveRepository => $"{Icon("refresh")} Clear active repository",
                        RepositoryAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<RepositoryAction>()));

            switch (action) {
                case RepositoryAction.SetManually:
                    SetActiveRepo(state, PromptRepository("Set active repository", required: false, state.ActiveRepo));
                    break;
                case RepositoryAction.SelectFromGitHub:
                    SetActiveRepo(state,
                        await PromptRepositoryFromGitHubAsync(state.ActiveRepo, state.LastOwner).ConfigureAwait(false) ??
                        state.ActiveRepo);
                    break;
                case RepositoryAction.ClearActiveRepository:
                    SetActiveRepo(state, null);
                    AnsiConsole.MarkupLine("[green]Active repository cleared.[/]");
                    PauseForMenu();
                    break;
                case RepositoryAction.Back:
                    return;
            }
        }
    }

    private static async Task RunDoctorAsync(ManageState state) {
        var repo = PromptRepository("Repository for doctor checks", required: false, state.ActiveRepo);
        if (!string.IsNullOrWhiteSpace(repo)) {
            SetActiveRepo(state, repo);
        }

        var args = string.IsNullOrWhiteSpace(repo)
            ? Array.Empty<string>()
            : new[] { "--repo", repo! };

        await RunProcessWithSummaryAsync(state, "Doctor checks", new[] {
            new ProcessStep {
                Title = "Run doctor preflight checks",
                RunAsync = () => Doctor.DoctorRunner.RunAsync(args)
            }
        }).ConfigureAwait(false);
        PauseForMenu();
    }

    private static async Task RunGitHubAuthStatusAsync(ManageState state) {
        var started = DateTimeOffset.UtcNow;
        var timer = Stopwatch.StartNew();
        var result = await RunExternalCommandAsync("gh", "auth status", TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        timer.Stop();

        state.LastProcessName = "GitHub auth status";
        state.LastExitCode = result.ExitCode == 0 ? 0 : 1;
        state.LastRunAtUtc = started;
        state.LastRunDuration = timer.Elapsed;
        state.LastStepCount = 1;

        AnsiConsole.Clear();
        RenderTitle($"{Icon("gh")} GitHub Auth Status");
        var status = result.ExitCode == 0 ? "[green]authenticated[/]" : "[yellow]not authenticated[/]";
        AnsiConsole.MarkupLine($"Status: {status}  [grey]({FormatDuration(timer.Elapsed)})[/]");
        AnsiConsole.WriteLine();

        var stdout = string.IsNullOrWhiteSpace(result.StdOut) ? "(no output)" : result.StdOut.Trim();
        var stderr = string.IsNullOrWhiteSpace(result.StdErr) ? "(none)" : result.StdErr.Trim();

        var output = new Grid();
        output.AddColumn();
        output.AddColumn();
        output.AddRow(
            new Panel(new Markup(Markup.Escape(CompactValue(stdout, 1200)))) {
                Header = new PanelHeader("stdout"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey)
            },
            new Panel(new Markup(Markup.Escape(CompactValue(stderr, 1200)))) {
                Header = new PanelHeader("stderr"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Grey)
            });
        AnsiConsole.Write(output);
        PauseForMenu();
        await Task.CompletedTask;
    }

    private static async Task RunQuickFixOpenAiAsync(ManageState state) {
        var repo = PromptRepository("Repository to update", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);
        if (!AnsiConsole.Confirm(
                $"Proceed with OpenAI reauthentication and secret update for [green]{Markup.Escape(repo)}[/]?", true)) {
            return;
        }

        var result = await RunProcessWithSummaryAsync(state, "Quick fix: OpenAI auth and secret refresh", new[] {
            new ProcessStep {
                Title = "Reauthenticate OpenAI and upload `INTELLIGENCEX_AUTH_B64`",
                RunAsync = () => RunAuthAsync(new[] { "login", "--set-github-secret", "--repo", repo })
            },
            new ProcessStep {
                Title = "Run doctor checks for the repository",
                RunAsync = () => Doctor.DoctorRunner.RunAsync(new[] { "--repo", repo }),
                Critical = false
            }
        }).ConfigureAwait(false);

        if (result == 0) {
            AnsiConsole.MarkupLine($"[green]{Icon("ok")} Quick fix completed for {Markup.Escape(repo)}.[/]");
        }
        PauseForMenu();
    }

    private static async Task RunUpdateSecretFromLocalAuthAsync(ManageState state) {
        var repo = PromptRepository("Repository to update", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);
        if (!AnsiConsole.Confirm(
                $"Update `INTELLIGENCEX_AUTH_B64` from local auth for [green]{Markup.Escape(repo)}[/]?", true)) {
            return;
        }

        await RunProcessWithSummaryAsync(state, "Update secret from local auth", new[] {
            new ProcessStep {
                Title = "Upload `INTELLIGENCEX_AUTH_B64` from existing local auth",
                RunAsync = () => RunSetupAsync(new[] { "--update-secret", "--repo", repo })
            }
        }).ConfigureAwait(false);
        PauseForMenu();
    }

    private static async Task RunResolveThreadsAsync(ManageState state, bool dryRun) {
        var repo = PromptRepository("Repository", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);
        var pr = PromptPositiveInt("Pull request number");
        if (pr <= 0) {
            return;
        }

        var args = new List<string> {
            "resolve-threads",
            "--repo", repo,
            "--pr", pr.ToString()
        };
        if (dryRun) {
            args.Add("--dry-run");
        } else {
            if (!AnsiConsole.Confirm(
                    $"Resolve bot threads now on [green]{Markup.Escape(repo)}[/] PR [green]#{pr}[/]?", false)) {
                return;
            }
        }

        await RunProcessWithSummaryAsync(
            state,
            dryRun ? "Resolve bot threads (dry-run)" : "Resolve bot threads",
            new[] {
                new ProcessStep {
                    Title = dryRun
                        ? "Preview bot thread resolution"
                        : "Resolve matching bot threads",
                    RunAsync = () => RunReviewerAsync(args.ToArray())
                }
            }).ConfigureAwait(false);
        PauseForMenu();
    }

    private static async Task<int> RunProcessWithSummaryAsync(ManageState state, string processName,
        IReadOnlyList<ProcessStep> steps) {
        AnsiConsole.Clear();
        RenderTitle($"{Icon("process")} {processName}");
        AnsiConsole.MarkupLine($"[grey]Steps: {steps.Count}[/]");
        AnsiConsole.WriteLine();

        var processTimer = Stopwatch.StartNew();
        var results = new List<ProcessStepResult>(steps.Count);
        foreach (var step in steps) {
            AnsiConsole.MarkupLine($"[grey]{Icon("arrow")} {Markup.Escape(step.Title)}[/]");
            var timer = Stopwatch.StartNew();
            var code = await step.RunAsync().ConfigureAwait(false);
            timer.Stop();
            results.Add(new ProcessStepResult(step.Title, code, step.Critical, timer.Elapsed));
            if (code == 0) {
                AnsiConsole.MarkupLine($"[green]{Icon("ok")} OK[/] [grey]({FormatDuration(timer.Elapsed)})[/]");
            } else {
                AnsiConsole.MarkupLine(
                    $"[red]{Icon("fail")} Exit code {code}[/] [grey]({FormatDuration(timer.Elapsed)})[/]");
                if (step.Critical) {
                    break;
                }
            }
            AnsiConsole.WriteLine();
        }
        processTimer.Stop();

        var summary = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Step")
            .AddColumn("Result")
            .AddColumn("Duration")
            .AddColumn("Type");
        foreach (var result in results) {
            var resultLabel = result.ExitCode == 0
                ? $"[green]{Icon("ok")} success[/]"
                : $"[red]{Icon("fail")} failed ({result.ExitCode})[/]";
            var typeLabel = result.Critical ? "critical" : "non-critical";
            summary.AddRow(
                Markup.Escape(result.Step),
                resultLabel,
                FormatDuration(result.Duration),
                typeLabel);
        }
        AnsiConsole.WriteLine();
        AnsiConsole.Write(summary);
        AnsiConsole.WriteLine();

        var failedCritical = results.Any(r => r.ExitCode != 0 && r.Critical);
        state.LastProcessName = processName;
        state.LastExitCode = failedCritical ? 1 : 0;
        state.LastRunAtUtc = DateTimeOffset.UtcNow;
        state.LastRunDuration = processTimer.Elapsed;
        state.LastStepCount = results.Count;

        var finalStyle = failedCritical ? "red" : "green";
        var finalIcon = failedCritical ? Icon("fail") : Icon("ok");
        AnsiConsole.MarkupLine(
            $"[{finalStyle}]{finalIcon} Process {(failedCritical ? "failed" : "completed")}[/] [grey]in {FormatDuration(processTimer.Elapsed)}[/]");
        return failedCritical ? 1 : 0;
    }

    private static MainMenuAction PromptMainAction() {
        var shortcut = PromptMainShortcut();
        if (shortcut.HasValue) {
            return shortcut.Value;
        }

        var prompt = new SelectionPrompt<MainMenuAction>()
            .Title("[bold]Select an area[/]")
            .PageSize(14)
            .UseConverter(action => action switch {
                MainMenuAction.FavoriteQuickFixOpenAi => $"{Icon("star")} Favorite: Quick fix OpenAI + secret",
                MainMenuAction.FavoriteDailyHealthPipeline => $"{Icon("star")} Favorite: Daily health pipeline",
                MainMenuAction.FavoritePullRequestReadiness => $"{Icon("star")} Favorite: PR readiness pipeline",
                MainMenuAction.QuickFixes => $"{Icon("bolt")} Quick fixes",
                MainMenuAction.AuthAndSecrets => $"{Icon("lock")} Auth and secrets",
                MainMenuAction.SetupAndOnboarding => $"{Icon("compass")} Setup and onboarding",
                MainMenuAction.ReviewerOperations => $"{Icon("robot")} Reviewer operations",
                MainMenuAction.Pipelines => $"{Icon("pipeline")} Pipelines",
                MainMenuAction.Diagnostics => $"{Icon("stethoscope")} Diagnostics",
                MainMenuAction.GitHubMonitor => $"{Icon("gh")} GitHub monitor",
                MainMenuAction.RepositoryManagement => $"{Icon("repo")} Repository management",
                MainMenuAction.SetActiveRepository => $"{Icon("repo")} Set active repository",
                MainMenuAction.ShowCheatSheet => $"{Icon("tip")} Command cheat sheet",
                MainMenuAction.RefreshDashboard => $"{Icon("refresh")} Refresh dashboard",
                MainMenuAction.Exit => $"{Icon("exit")} Exit",
                _ => action.ToString()
            })
            .AddChoices(Enum.GetValues<MainMenuAction>());

        return AnsiConsole.Prompt(prompt);
    }

    private static MainMenuAction? PromptMainShortcut() {
        var raw = AnsiConsole.Prompt(
            new TextPrompt<string>(
                    "[grey]Shortcut: (1)QuickFix (2)HealthPipe (3)PRPipe (4)GHMonitor (5)Diagnostics (6)Auth (7)Setup (8)Reviewer (9)Repo (0)Exit (Enter for full menu)[/]")
                .AllowEmpty())
            .Trim();

        if (string.IsNullOrWhiteSpace(raw)) {
            return null;
        }

        return raw switch {
            "1" => MainMenuAction.FavoriteQuickFixOpenAi,
            "2" => MainMenuAction.FavoriteDailyHealthPipeline,
            "3" => MainMenuAction.FavoritePullRequestReadiness,
            "4" => MainMenuAction.GitHubMonitor,
            "5" => MainMenuAction.Diagnostics,
            "6" => MainMenuAction.AuthAndSecrets,
            "7" => MainMenuAction.SetupAndOnboarding,
            "8" => MainMenuAction.ReviewerOperations,
            "9" => MainMenuAction.RepositoryManagement,
            "0" => MainMenuAction.Exit,
            _ => null
        };
    }

    private static async Task RenderDashboardAsync(ManageState state) {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("IntelligenceX").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]Management Hub[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(BuildFavoritesPanel());
        AnsiConsole.WriteLine();

        var left = BuildContextPanel(state);
        var right = await BuildRuntimePanelAsync().ConfigureAwait(false);
        var row = new Grid();
        row.AddColumn();
        row.AddColumn();
        row.AddRow(left, right);
        AnsiConsole.Write(row);
        AnsiConsole.WriteLine();

        var secondary = new Grid();
        secondary.AddColumn();
        secondary.AddColumn();
        secondary.AddRow(await BuildHealthPanelAsync(state).ConfigureAwait(false), BuildRecentActivityPanel(state));
        AnsiConsole.Write(secondary);
        AnsiConsole.WriteLine();

        var bundlesPanel = BuildBundlePanel();
        AnsiConsole.Write(bundlesPanel);
        AnsiConsole.WriteLine();

        AnsiConsole.MarkupLine(
            $"[grey]{Icon("tip")} Tip: use Pipelines for guided workflows and GitHub monitor for PR/check visibility.[/]");
    }

    private static Panel BuildFavoritesPanel() {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(
            $"[bold]{Icon("star")} (1)[/]: Quick fix OpenAI + secret",
            $"[bold]{Icon("star")} (2)[/]: Daily health pipeline",
            $"[bold]{Icon("star")} (3)[/]: PR readiness pipeline");
        grid.AddRow(
            "[grey]Shortcuts available: 0..9[/]",
            "[grey]Press Enter to browse full menu[/]",
            "[grey]Use arrows once in full menu[/]");

        return new Panel(grid) {
            Header = new PanelHeader($"{Icon("star")} Favorites and Shortcuts"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

    private static Panel BuildContextPanel(ManageState state) {
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("k")
            .AddColumn("v");
        table.AddRow("Workspace", Markup.Escape(CompactValue(Environment.CurrentDirectory, 52)));
        table.AddRow("Active repo", Markup.Escape(state.ActiveRepo ?? "(not set)"));
        table.AddRow("Owner hint", Markup.Escape(state.LastOwner ?? "(not set)"));
        table.AddRow("Detected repo", Markup.Escape(ResolveDefaultRepo() ?? "(none)"));
        table.AddRow("UTC", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"));

        return new Panel(table) {
            Header = new PanelHeader($"{Icon("repo")} Context"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

    private static async Task<Panel> BuildRuntimePanelAsync() {
        var ghStatus = await GetGitHubCliStatusAsync().ConfigureAwait(false);
        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("k")
            .AddColumn("v");
        table.AddRow("GitHub CLI", ghStatus.Installed ? "[green]installed[/]" : "[red]missing[/]");
        table.AddRow("GitHub auth", ghStatus.Authenticated ? "[green]authenticated[/]" : "[yellow]not authenticated[/]");
        table.AddRow("OpenAI auth path", Markup.Escape(CompactValue(AuthPaths.ResolveAuthPath(), 52)));
        table.AddRow("Prefs path", Markup.Escape(CompactValue(GetPreferencesPath(), 52)));
        table.AddRow("Hub mode", "interactive");

        return new Panel(table) {
            Header = new PanelHeader($"{Icon("process")} Runtime"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

    private static async Task<Panel> BuildHealthPanelAsync(ManageState state) {
        var bundles = ReadOpenAiBundles();
        var now = DateTimeOffset.UtcNow;
        var valid = bundles.Count(b => b.ExpiresAt.HasValue && b.ExpiresAt.Value > now.AddHours(24));
        var soon = bundles.Count(b => b.ExpiresAt.HasValue && b.ExpiresAt.Value > now && b.ExpiresAt.Value <= now.AddHours(24));
        var expired = bundles.Count(b => !b.ExpiresAt.HasValue || b.ExpiresAt.Value <= now);
        var gh = await GetGitHubCliStatusAsync().ConfigureAwait(false);

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn("k")
            .AddColumn("v");
        table.AddRow("OpenAI bundles", bundles.Count.ToString());
        table.AddRow("Bundle health", $"[green]{valid} valid[/] / [yellow]{soon} soon[/] / [red]{expired} expired[/]");
        table.AddRow("GitHub auth", gh.Authenticated ? "[green]ready[/]" : "[yellow]not authenticated[/]");
        table.AddRow("Open PRs (cached)", state.LastOpenPullRequestCount?.ToString() ?? "-");
        table.AddRow("Failed checks (cached)", state.LastFailedCheckCount?.ToString() ?? "-");
        table.AddRow("Reviewer run (cached)", Markup.Escape(state.LastReviewerRunStatus ?? "-"));
        table.AddRow("Monitor refreshed", state.LastMonitorUpdatedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "-");

        return new Panel(table) {
            Header = new PanelHeader($"{Icon("check")} Health"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };
    }

}
