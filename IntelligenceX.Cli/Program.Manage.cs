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
    private sealed class ManageState {
        public string? ActiveRepo { get; set; }
        public string? LastOwner { get; set; }
        public string? LastProcessName { get; set; }
        public int? LastExitCode { get; set; }
        public DateTimeOffset? LastRunAtUtc { get; set; }
        public TimeSpan? LastRunDuration { get; set; }
        public int LastStepCount { get; set; }
        public int? LastOpenPullRequestCount { get; set; }
        public int? LastFailedCheckCount { get; set; }
        public DateTimeOffset? LastMonitorUpdatedAtUtc { get; set; }
        public string? LastReviewerRunStatus { get; set; }
    }

    private sealed class ManagePreferences {
        public string? ActiveRepo { get; set; }
        public string? LastOwner { get; set; }
    }

    private sealed record OpenAiBundleStatus(string Provider, string? AccountId, DateTimeOffset? ExpiresAt);
    private sealed record PullRequestSummary(int Number, string Title, string State, bool IsDraft, string? HeadRefName,
        DateTimeOffset? UpdatedAt, string? Url);
    private sealed record WorkflowRunSummary(long DatabaseId, string? Status, string? Conclusion, string? DisplayTitle,
        string? HeadBranch, string? Event, DateTimeOffset? CreatedAt, string? Url);
    private sealed record PullRequestCheckSummary(string? Name, string? Workflow, string? State, string? Bucket,
        string? Event, string? Link);

    private sealed class ProcessStep {
        public required string Title { get; init; }
        public required Func<Task<int>> RunAsync { get; init; }
        public bool Critical { get; init; } = true;
    }

    private sealed record ProcessStepResult(string Step, int ExitCode, bool Critical, TimeSpan Duration);

    private enum MainMenuAction {
        FavoriteQuickFixOpenAi,
        FavoriteDailyHealthPipeline,
        FavoritePullRequestReadiness,
        QuickFixes,
        AuthAndSecrets,
        SetupAndOnboarding,
        ReviewerOperations,
        Pipelines,
        Diagnostics,
        GitHubMonitor,
        RepositoryManagement,
        SetActiveRepository,
        ShowCheatSheet,
        RefreshDashboard,
        Exit
    }

    private enum QuickFixAction {
        RefreshOpenAiAndSecret,
        RefreshSecretFromLocalAuth,
        Back
    }

    private enum AuthSecretAction {
        LoginOnly,
        ListBundles,
        LoginAndSetRepoSecret,
        UpdateRepoSecretFromLocalAuth,
        Back
    }

    private enum SetupAction {
        SetupWizard,
        SetupWebUi,
        UpdateSecretOnly,
        Back
    }

    private enum ReviewerAction {
        RunReviewerNow,
        ResolveBotThreadsDryRun,
        ResolveBotThreadsNow,
        Back
    }

    private enum DiagnosticsAction {
        DoctorChecks,
        GitHubAuthStatus,
        ShowAuthBundlesDetailed,
        Back
    }

    private enum RepositoryAction {
        SetManually,
        SelectFromGitHub,
        ClearActiveRepository,
        Back
    }

    private enum GitHubMonitorAction {
        ListOpenPullRequests,
        ListRecentReviewerRuns,
        ShowPullRequestChecks,
        ListRecentPullRequests,
        Back
    }

    private enum PipelineAction {
        DailyHealthCheck,
        PullRequestReadiness,
        Back
    }

    private static bool CanLaunchManageHub() {
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected) {
            return false;
        }
        return AnsiConsole.Profile.Capabilities.Ansi;
    }

    private static async Task<int> RunManageAsync(string[] args) {
        if (args.Length > 0) {
            var arg = args[0];
            if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("help", StringComparison.OrdinalIgnoreCase)) {
                PrintManageHelp();
                return 0;
            }
        }

        if (!CanLaunchManageHub()) {
            Console.Error.WriteLine("`manage` requires an interactive terminal.");
            return 1;
        }

        var prefs = LoadPreferences();
        var detectedRepo = ResolveDefaultRepo();
        var state = new ManageState {
            ActiveRepo = !string.IsNullOrWhiteSpace(prefs.ActiveRepo) ? prefs.ActiveRepo : detectedRepo,
            LastOwner = prefs.LastOwner ?? TryGetOwner(detectedRepo)
        };

        while (true) {
            SavePreferences(state);
            RenderDashboard(state);
            var action = PromptMainAction();
            switch (action) {
                case MainMenuAction.FavoriteQuickFixOpenAi:
                    await RunQuickFixOpenAiAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.FavoriteDailyHealthPipeline:
                    await RunDailyHealthPipelineAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.FavoritePullRequestReadiness:
                    await RunPullRequestReadinessPipelineAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.QuickFixes:
                    await HandleQuickFixesAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.AuthAndSecrets:
                    await HandleAuthAndSecretsAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.SetupAndOnboarding:
                    await HandleSetupMenuAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.ReviewerOperations:
                    await HandleReviewerMenuAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.Pipelines:
                    await HandlePipelinesMenuAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.Diagnostics:
                    await HandleDiagnosticsMenuAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.GitHubMonitor:
                    await HandleGitHubMonitorMenuAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.RepositoryManagement:
                    await HandleRepositoryMenuAsync(state).ConfigureAwait(false);
                    break;
                case MainMenuAction.SetActiveRepository:
                    SetActiveRepo(state, PromptRepository("Set active repository", required: false, state.ActiveRepo));
                    break;
                case MainMenuAction.ShowCheatSheet:
                    ShowCheatSheet();
                    PauseForMenu();
                    break;
                case MainMenuAction.RefreshDashboard:
                    break;
                case MainMenuAction.Exit:
                    return 0;
                default:
                    return 0;
            }
        }
    }

    private static async Task HandleQuickFixesAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("tool")} Quick Fixes");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<QuickFixAction>()
                    .Title("Pick a quick fix")
                    .UseConverter(x => x switch {
                        QuickFixAction.RefreshOpenAiAndSecret =>
                            $"{Icon("bolt")} Reauthenticate OpenAI and update repo secret (recommended)",
                        QuickFixAction.RefreshSecretFromLocalAuth =>
                            $"{Icon("refresh")} Update repo secret from existing local auth",
                        QuickFixAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<QuickFixAction>()));

            switch (action) {
                case QuickFixAction.RefreshOpenAiAndSecret:
                    await RunQuickFixOpenAiAsync(state).ConfigureAwait(false);
                    break;
                case QuickFixAction.RefreshSecretFromLocalAuth:
                    await RunUpdateSecretFromLocalAuthAsync(state).ConfigureAwait(false);
                    break;
                case QuickFixAction.Back:
                    return;
            }
        }
    }

    private static async Task HandleAuthAndSecretsAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("lock")} Auth and Secrets");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<AuthSecretAction>()
                    .Title("Choose auth/secret operation")
                    .UseConverter(x => x switch {
                        AuthSecretAction.LoginOnly => $"{Icon("lock")} Auth login (local only)",
                        AuthSecretAction.ListBundles => $"{Icon("box")} List auth bundles",
                        AuthSecretAction.LoginAndSetRepoSecret => $"{Icon("bolt")} Login + set repo secret",
                        AuthSecretAction.UpdateRepoSecretFromLocalAuth =>
                            $"{Icon("refresh")} Update repo secret from existing local auth",
                        AuthSecretAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<AuthSecretAction>()));

            switch (action) {
                case AuthSecretAction.LoginOnly:
                    await RunProcessWithSummaryAsync(state, "Auth login", new[] {
                        new ProcessStep {
                            Title = "Run `intelligencex auth login`",
                            RunAsync = () => RunAuthAsync(new[] { "login" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
                    break;
                case AuthSecretAction.ListBundles:
                    await RunProcessWithSummaryAsync(state, "List auth bundles", new[] {
                        new ProcessStep {
                            Title = "Run `intelligencex auth list`",
                            RunAsync = () => RunAuthAsync(new[] { "list" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
                    break;
                case AuthSecretAction.LoginAndSetRepoSecret:
                    await RunQuickFixOpenAiAsync(state).ConfigureAwait(false);
                    break;
                case AuthSecretAction.UpdateRepoSecretFromLocalAuth:
                    await RunUpdateSecretFromLocalAuthAsync(state).ConfigureAwait(false);
                    break;
                case AuthSecretAction.Back:
                    return;
            }
        }
    }

    private static async Task HandleSetupMenuAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("compass")} Setup and Onboarding");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<SetupAction>()
                    .Title("Choose setup operation")
                    .UseConverter(x => x switch {
                        SetupAction.SetupWizard => $"{Icon("compass")} Start setup wizard",
                        SetupAction.SetupWebUi => $"{Icon("globe")} Start setup web UI",
                        SetupAction.UpdateSecretOnly => $"{Icon("refresh")} Update secret only",
                        SetupAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<SetupAction>()));

            switch (action) {
                case SetupAction.SetupWizard:
                    await RunProcessWithSummaryAsync(state, "Setup wizard", new[] {
                        new ProcessStep {
                            Title = "Start setup wizard",
                            RunAsync = () => RunSetupAsync(new[] { "wizard" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
                    break;
                case SetupAction.SetupWebUi:
                    await RunProcessWithSummaryAsync(state, "Setup web UI", new[] {
                        new ProcessStep {
                            Title = "Start setup web UI (Ctrl+C to return)",
                            RunAsync = () => RunSetupAsync(new[] { "web" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
                    break;
                case SetupAction.UpdateSecretOnly:
                    await RunUpdateSecretFromLocalAuthAsync(state).ConfigureAwait(false);
                    break;
                case SetupAction.Back:
                    return;
            }
        }
    }

    private static async Task HandleReviewerMenuAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("robot")} Reviewer Operations");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<ReviewerAction>()
                    .Title("Choose reviewer operation")
                    .UseConverter(x => x switch {
                        ReviewerAction.RunReviewerNow => $"{Icon("robot")} Run reviewer (current environment)",
                        ReviewerAction.ResolveBotThreadsDryRun => $"{Icon("check")} Resolve bot threads (dry-run)",
                        ReviewerAction.ResolveBotThreadsNow => $"{Icon("tool")} Resolve bot threads (apply)",
                        ReviewerAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<ReviewerAction>()));

            switch (action) {
                case ReviewerAction.RunReviewerNow:
                    await RunProcessWithSummaryAsync(state, "Reviewer run", new[] {
                        new ProcessStep {
                            Title = "Run reviewer",
                            RunAsync = () => RunReviewerAsync(new[] { "run" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
                    break;
                case ReviewerAction.ResolveBotThreadsDryRun:
                    await RunResolveThreadsAsync(state, dryRun: true).ConfigureAwait(false);
                    break;
                case ReviewerAction.ResolveBotThreadsNow:
                    await RunResolveThreadsAsync(state, dryRun: false).ConfigureAwait(false);
                    break;
                case ReviewerAction.Back:
                    return;
            }
        }
    }

    private static async Task HandleDiagnosticsMenuAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("stethoscope")} Diagnostics");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<DiagnosticsAction>()
                    .Title("Choose diagnostics operation")
                    .UseConverter(x => x switch {
                        DiagnosticsAction.DoctorChecks => $"{Icon("stethoscope")} Doctor checks",
                        DiagnosticsAction.GitHubAuthStatus => $"{Icon("gh")} GitHub auth status",
                        DiagnosticsAction.ShowAuthBundlesDetailed => $"{Icon("box")} Show auth bundle details",
                        DiagnosticsAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<DiagnosticsAction>()));

            switch (action) {
                case DiagnosticsAction.DoctorChecks:
                    await RunDoctorAsync(state).ConfigureAwait(false);
                    break;
                case DiagnosticsAction.GitHubAuthStatus:
                    await RunGitHubAuthStatusAsync(state).ConfigureAwait(false);
                    break;
                case DiagnosticsAction.ShowAuthBundlesDetailed:
                    ShowAuthBundlesDetailed();
                    PauseForMenu();
                    break;
                case DiagnosticsAction.Back:
                    return;
            }
        }
    }

    private static async Task HandleGitHubMonitorMenuAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("gh")} GitHub Monitor");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<GitHubMonitorAction>()
                    .Title("Choose monitor view")
                    .UseConverter(x => x switch {
                        GitHubMonitorAction.ListOpenPullRequests => $"{Icon("repo")} Open pull requests",
                        GitHubMonitorAction.ListRecentPullRequests => $"{Icon("repo")} Recent pull requests",
                        GitHubMonitorAction.ShowPullRequestChecks => $"{Icon("check")} Pull request checks",
                        GitHubMonitorAction.ListRecentReviewerRuns => $"{Icon("process")} Recent reviewer workflow runs",
                        GitHubMonitorAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<GitHubMonitorAction>()));

            switch (action) {
                case GitHubMonitorAction.ListOpenPullRequests:
                    await ShowPullRequestListAsync(state, "open", "Open pull requests").ConfigureAwait(false);
                    break;
                case GitHubMonitorAction.ListRecentPullRequests:
                    await ShowPullRequestListAsync(state, "all", "Recent pull requests").ConfigureAwait(false);
                    break;
                case GitHubMonitorAction.ShowPullRequestChecks:
                    await ShowPullRequestChecksAsync(state).ConfigureAwait(false);
                    break;
                case GitHubMonitorAction.ListRecentReviewerRuns:
                    await ShowRecentReviewerRunsAsync(state).ConfigureAwait(false);
                    break;
                case GitHubMonitorAction.Back:
                    return;
            }
        }
    }

    private static async Task HandlePipelinesMenuAsync(ManageState state) {
        while (true) {
            AnsiConsole.Clear();
            RenderTitle($"{Icon("pipeline")} Pipelines");
            var action = AnsiConsole.Prompt(
                new SelectionPrompt<PipelineAction>()
                    .Title("Choose guided pipeline")
                    .UseConverter(x => x switch {
                        PipelineAction.DailyHealthCheck => $"{Icon("check")} Daily health check",
                        PipelineAction.PullRequestReadiness => $"{Icon("tool")} Pull request readiness",
                        PipelineAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<PipelineAction>()));

            switch (action) {
                case PipelineAction.DailyHealthCheck:
                    await RunDailyHealthPipelineAsync(state).ConfigureAwait(false);
                    break;
                case PipelineAction.PullRequestReadiness:
                    await RunPullRequestReadinessPipelineAsync(state).ConfigureAwait(false);
                    break;
                case PipelineAction.Back:
                    return;
            }
        }
    }

    private static async Task RunDailyHealthPipelineAsync(ManageState state) {
        var repo = PromptRepository("Repository", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);

        await RunProcessWithSummaryAsync(state, "Pipeline: daily health check", new[] {
            new ProcessStep {
                Title = "Check GitHub auth status",
                RunAsync = () => Task.FromResult(RunExternalCommand("gh", "auth status").ExitCode)
            },
            new ProcessStep {
                Title = "Run doctor checks",
                RunAsync = () => Doctor.DoctorRunner.RunAsync(new[] { "--repo", repo })
            }
        }).ConfigureAwait(false);

        var prs = await RunWithSpinnerAsync("Fetching open pull requests...",
            () => FetchPullRequests(repo, "open", 50)).ConfigureAwait(false);
        var runs = await RunWithSpinnerAsync("Fetching recent reviewer runs...",
            () => FetchWorkflowRuns(repo, "review-intelligencex.yml", 20)).ConfigureAwait(false);

        if (prs.Items is not null) {
            state.LastOpenPullRequestCount = prs.Items.Count;
            state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
        }
        if (runs.Items is not null && runs.Items.Count > 0) {
            var latest = runs.Items.OrderByDescending(r => r.CreatedAt ?? DateTimeOffset.MinValue).First();
            state.LastReviewerRunStatus = string.Equals(latest.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? latest.Conclusion ?? latest.Status
                : latest.Status;
            state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        AnsiConsole.WriteLine();
        var summary = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Metric")
            .AddColumn("Value");
        summary.AddRow("Repository", Markup.Escape(repo));
        summary.AddRow("Open PRs", prs.Items?.Count.ToString() ?? "-");
        summary.AddRow("Recent reviewer runs", runs.Items?.Count.ToString() ?? "-");

        if (runs.Items is not null && runs.Items.Count > 0) {
            var latest = runs.Items.OrderByDescending(r => r.CreatedAt ?? DateTimeOffset.MinValue).First();
            summary.AddRow("Latest run status", GetRunStatusMarkup(latest.Status, latest.Conclusion));
            summary.AddRow("Latest run created", latest.CreatedAt?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "-");
        }
        AnsiConsole.Write(summary);

        if (prs.Error is not null) {
            AnsiConsole.WriteLine();
            RenderCommandError("Open PR fetch warning", prs.Error);
        }
        if (runs.Error is not null) {
            AnsiConsole.WriteLine();
            RenderCommandError("Workflow runs fetch warning", runs.Error);
        }
        PauseForMenu();
    }

    private static async Task RunPullRequestReadinessPipelineAsync(ManageState state) {
        var repo = PromptRepository("Repository", required: true, state.ActiveRepo);
        if (repo is null) {
            return;
        }
        SetActiveRepo(state, repo);

        var prNumber = await PromptPullRequestNumberAsync(repo).ConfigureAwait(false);
        if (!prNumber.HasValue) {
            return;
        }

        await RunProcessWithSummaryAsync(state, $"Pipeline: PR #{prNumber.Value} readiness", new[] {
            new ProcessStep {
                Title = "Run doctor checks",
                RunAsync = () => Doctor.DoctorRunner.RunAsync(new[] { "--repo", repo }),
                Critical = false
            },
            new ProcessStep {
                Title = "Load pull request checks",
                RunAsync = () => {
                    var checks = FetchPullRequestChecks(repo, prNumber.Value);
                    return Task.FromResult(checks.Error is null ? 0 : 1);
                }
            }
        }).ConfigureAwait(false);

        var checksResult = await RunWithSpinnerAsync("Fetching check details...",
            () => FetchPullRequestChecks(repo, prNumber.Value)).ConfigureAwait(false);
        if (checksResult.Items is not null) {
            state.LastFailedCheckCount = checksResult.Items.Count(c =>
                string.Equals(c.State, "FAILURE", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.State, "ERROR", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.State, "TIMED_OUT", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.State, "ACTION_REQUIRED", StringComparison.OrdinalIgnoreCase));
            state.LastMonitorUpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        AnsiConsole.WriteLine();
        RenderPullRequestReadinessSummary(repo, prNumber.Value, checksResult);
        PauseForMenu();
    }

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
            () => FetchPullRequests(repo, prState, 20)).ConfigureAwait(false);

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
            () => FetchPullRequestChecks(repo, prNumber.Value)).ConfigureAwait(false);

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
            () => FetchWorkflowRuns(repo, "review-intelligencex.yml", 15)).ConfigureAwait(false);

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
        var result = RunExternalCommand("gh", "auth status");
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

    private static void RenderDashboard(ManageState state) {
        AnsiConsole.Clear();
        AnsiConsole.Write(new FigletText("IntelligenceX").Color(Color.Aqua));
        AnsiConsole.MarkupLine("[grey]Management Hub[/]");
        AnsiConsole.WriteLine();

        AnsiConsole.Write(BuildFavoritesPanel());
        AnsiConsole.WriteLine();

        var left = BuildContextPanel(state);
        var right = BuildRuntimePanel();
        var row = new Grid();
        row.AddColumn();
        row.AddColumn();
        row.AddRow(left, right);
        AnsiConsole.Write(row);
        AnsiConsole.WriteLine();

        var secondary = new Grid();
        secondary.AddColumn();
        secondary.AddColumn();
        secondary.AddRow(BuildHealthPanel(state), BuildRecentActivityPanel(state));
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

    private static Panel BuildRuntimePanel() {
        var ghStatus = GetGitHubCliStatus();
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

    private static Panel BuildHealthPanel(ManageState state) {
        var bundles = ReadOpenAiBundles();
        var now = DateTimeOffset.UtcNow;
        var valid = bundles.Count(b => b.ExpiresAt.HasValue && b.ExpiresAt.Value > now.AddHours(24));
        var soon = bundles.Count(b => b.ExpiresAt.HasValue && b.ExpiresAt.Value > now && b.ExpiresAt.Value <= now.AddHours(24));
        var expired = bundles.Count(b => !b.ExpiresAt.HasValue || b.ExpiresAt.Value <= now);
        var gh = GetGitHubCliStatus();

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

    private static List<(string Repo, DateTimeOffset? PushedAt, bool IsPrivate)> ParseRepoList(string json) {
        var list = new List<(string Repo, DateTimeOffset? PushedAt, bool IsPrivate)>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return list;
        }

        foreach (var item in doc.RootElement.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object) {
                continue;
            }

            if (!item.TryGetProperty("nameWithOwner", out var nameProp) || nameProp.ValueKind != JsonValueKind.String) {
                continue;
            }
            var repo = nameProp.GetString();
            if (string.IsNullOrWhiteSpace(repo)) {
                continue;
            }

            DateTimeOffset? pushed = null;
            if (item.TryGetProperty("pushedAt", out var pushedProp) && pushedProp.ValueKind == JsonValueKind.String) {
                var raw = pushedProp.GetString();
                if (DateTimeOffset.TryParse(raw, out var parsed)) {
                    pushed = parsed;
                }
            }

            var isPrivate = item.TryGetProperty("isPrivate", out var privateProp) &&
                            privateProp.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                            privateProp.GetBoolean();

            list.Add((repo, pushed, isPrivate));
        }
        return list;
    }

    private static string? TryGetOwner(string? repo) {
        if (string.IsNullOrWhiteSpace(repo)) {
            return null;
        }
        var normalized = NormalizeRepo(repo);
        var idx = normalized.IndexOf('/');
        if (idx <= 0) {
            return null;
        }
        return normalized.Substring(0, idx);
    }

    private static void ShowCheatSheet() {
        AnsiConsole.Clear();
        RenderTitle($"{Icon("tip")} Command Cheat Sheet");
        var table = new Table()
            .RoundedBorder()
            .BorderColor(Color.Grey)
            .AddColumn("Goal")
            .AddColumn("Command");
        table.AddRow("Open management hub", "intelligencex");
        table.AddRow("Explicit hub command", "intelligencex manage");
        table.AddRow("Main shortcuts", "1=QuickFix 2=HealthPipe 3=PRPipe 4=GHMonitor 5=Diagnostics 0=Exit");
        table.AddRow("Guided pipelines", "Hub -> Pipelines -> Daily health check / PR readiness");
        table.AddRow("Quick OpenAI reauth + secret sync", "intelligencex auth login --set-github-secret --repo owner/name");
        table.AddRow("Refresh secret from local auth", "intelligencex setup --update-secret --repo owner/name");
        table.AddRow("Run doctor checks", "intelligencex doctor --repo owner/name");
        table.AddRow("Run reviewer locally", "intelligencex reviewer run");
        table.AddRow("Resolve bot threads (dry-run)", "intelligencex reviewer resolve-threads --repo owner/name --pr 123 --dry-run");
        table.AddRow("List PRs", "gh pr list --repo owner/name --state open");
        table.AddRow("PR checks", "gh pr checks --repo owner/name <pr-number>");
        table.AddRow("Reviewer runs", "gh run list --repo owner/name --workflow review-intelligencex.yml");
        AnsiConsole.Write(table);
    }

    private static string CompactValue(string value, int maxLength) {
        if (string.IsNullOrEmpty(value) || maxLength < 8 || value.Length <= maxLength) {
            return value;
        }
        var head = Math.Max(3, (maxLength - 1) / 2);
        var tail = Math.Max(3, maxLength - head - 1);
        if (head + tail + 1 > value.Length) {
            return value;
        }
        return $"{value.Substring(0, head)}…{value.Substring(value.Length - tail)}";
    }

    private static string FormatDuration(TimeSpan duration) {
        if (duration.TotalSeconds < 1) {
            return $"{duration.TotalMilliseconds:0}ms";
        }
        if (duration.TotalMinutes < 1) {
            return $"{duration.TotalSeconds:0.0}s";
        }
        if (duration.TotalHours < 1) {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
        return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
    }

    private static string? ResolveDefaultRepo() {
        var envRepo = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_REPO")
                      ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        if (!string.IsNullOrWhiteSpace(envRepo) && TryParseRepo(envRepo, out _, out _)) {
            return envRepo;
        }
        var detected = GitHubRepoDetector.TryDetectRepo(Environment.CurrentDirectory);
        if (!string.IsNullOrWhiteSpace(detected) && TryParseRepo(detected, out _, out _)) {
            return detected;
        }
        return null;
    }

    private static ManagePreferences LoadPreferences() {
        try {
            var path = GetPreferencesPath();
            if (!File.Exists(path)) {
                return new ManagePreferences();
            }
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) {
                return new ManagePreferences();
            }
            var prefs = JsonSerializer.Deserialize<ManagePreferences>(json);
            return prefs ?? new ManagePreferences();
        } catch {
            return new ManagePreferences();
        }
    }

    private static void SavePreferences(ManageState state) {
        try {
            var prefs = new ManagePreferences {
                ActiveRepo = state.ActiveRepo,
                LastOwner = state.LastOwner ?? TryGetOwner(state.ActiveRepo)
            };
            var path = GetPreferencesPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }
            var json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions {
                WriteIndented = true
            });
            File.WriteAllText(path, json);
        } catch {
            // Best effort; ignore persistence failures.
        }
    }

    private static string GetPreferencesPath() {
        var authPath = AuthPaths.ResolveAuthPath();
        var authDir = Path.GetDirectoryName(authPath);
        if (string.IsNullOrWhiteSpace(authDir)) {
            authDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".intelligencex");
        }
        return Path.Combine(authDir, "manage-hub.json");
    }

    private static string NormalizeRepo(string value) {
        var repo = value.Trim();
        while (repo.EndsWith("/") || repo.EndsWith("\\")) {
            repo = repo.Substring(0, repo.Length - 1);
        }
        return repo;
    }

    private static List<OpenAiBundleStatus> ReadOpenAiBundles() {
        var path = AuthPaths.ResolveAuthPath();
        if (!File.Exists(path)) {
            return new List<OpenAiBundleStatus>();
        }

        try {
            var raw = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(raw)) {
                return new List<OpenAiBundleStatus>();
            }

            var json = AuthStoreUtils.DecryptAuthStoreIfNeeded(raw);
            return AuthStoreUtils.ParseAuthStoreEntries(json)
                .Where(e => string.Equals(e.Provider, "openai-codex", StringComparison.OrdinalIgnoreCase))
                .Select(e => new OpenAiBundleStatus(e.Provider, e.AccountId, e.ExpiresAt))
                .ToList();
        } catch {
            return new List<OpenAiBundleStatus>();
        }
    }

    private static (bool Installed, bool Authenticated) GetGitHubCliStatus() {
        var token = TryReadGhToken();
        if (!string.IsNullOrWhiteSpace(token)) {
            return (true, true);
        }

        var result = RunExternalCommand("gh", "auth status");
        if (result.ExitCode == int.MinValue) {
            return (false, false);
        }
        return (true, false);
    }

    private static (int ExitCode, string StdOut, string StdErr) RunExternalCommand(string fileName, string arguments) {
        try {
            var startInfo = new ProcessStartInfo {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            if (process is null) {
                return (int.MinValue, string.Empty, "Failed to start process.");
            }
            var stdOut = process.StandardOutput.ReadToEnd();
            var stdErr = process.StandardError.ReadToEnd();
            var exited = process.WaitForExit(30000);
            if (!exited) {
                try {
                    process.Kill(entireProcessTree: true);
                } catch {
                    // ignore kill failures
                }
                return (124, stdOut, string.IsNullOrWhiteSpace(stdErr) ? "Command timed out after 30s." : stdErr);
            }
            return (process.ExitCode, stdOut, stdErr);
        } catch (Exception ex) {
            return (int.MinValue, string.Empty, ex.Message);
        }
    }

    private static void RenderTitle(string title) {
        AnsiConsole.Write(new Rule(title).LeftJustified().RuleStyle("grey"));
    }

    private static void PauseForMenu() {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
        Console.ReadKey(intercept: true);
    }

    private static string Icon(string name) {
        var unicode = AnsiConsole.Profile.Capabilities.Unicode;
        return name switch {
            "ok" => unicode ? "✓" : "[OK]",
            "fail" => unicode ? "✗" : "[X]",
            "lock" => unicode ? "🔐" : "[AUTH]",
            "box" => unicode ? "📦" : "[BUNDLE]",
            "stethoscope" => unicode ? "🩺" : "[DOCTOR]",
            "robot" => unicode ? "🤖" : "[REVIEW]",
            "compass" => unicode ? "🧭" : "[SETUP]",
            "globe" => unicode ? "🌐" : "[WEB]",
            "repo" => unicode ? "📁" : "[REPO]",
            "refresh" => unicode ? "♻️" : "[REFRESH]",
            "bolt" => unicode ? "🛠" : "[FIX]",
            "process" => unicode ? "⚙️" : "[PROCESS]",
            "gh" => unicode ? "🐙" : "[GH]",
            "arrow" => unicode ? "→" : "->",
            "tip" => unicode ? "💡" : "[TIP]",
            "back" => unicode ? "↩" : "[BACK]",
            "exit" => unicode ? "❌" : "[EXIT]",
            "tool" => unicode ? "🛠" : "[TOOL]",
            "check" => unicode ? "✅" : "[CHECK]",
            "pipeline" => unicode ? "🧪" : "[PIPE]",
            "star" => unicode ? "⭐" : "[FAV]",
            _ => "*"
        };
    }

    private static void PrintManageHelp() {
        Console.WriteLine("IntelligenceX management hub");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex manage");
        Console.WriteLine("  intelligencex manage --help");
        Console.WriteLine();
        Console.WriteLine("No arguments in interactive terminals also open this hub:");
        Console.WriteLine("  intelligencex");
    }
}
