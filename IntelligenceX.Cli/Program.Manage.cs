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
        AutoDetectPreflight,
        SetupWizard,
        SetupWebUi,
        UpdateSecretOnly,
        CleanupFlow,
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
            await RenderDashboardAsync(state).ConfigureAwait(false);
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
                        SetupAction.AutoDetectPreflight => $"{Icon("stethoscope")} Auto-detect path (doctor preflight)",
                        SetupAction.SetupWizard => $"{Icon("compass")} Start setup wizard",
                        SetupAction.SetupWebUi => $"{Icon("globe")} Start setup web UI",
                        SetupAction.UpdateSecretOnly => $"{Icon("refresh")} Update secret only",
                        SetupAction.CleanupFlow => $"{Icon("broom")} Cleanup workflow/config",
                        SetupAction.Back => $"{Icon("back")} Back",
                        _ => x.ToString()
                    })
                    .AddChoices(Enum.GetValues<SetupAction>()));

            switch (action) {
                case SetupAction.AutoDetectPreflight:
                    await RunProcessWithSummaryAsync(state, "Setup auto-detect", new[] {
                        new ProcessStep {
                            Title = "Run setup auto-detect",
                            RunAsync = () => RunSetupAsync(new[] { "autodetect" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
                    break;
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
                case SetupAction.CleanupFlow:
                    await RunProcessWithSummaryAsync(state, "Cleanup wizard", new[] {
                        new ProcessStep {
                            Title = "Start setup wizard in cleanup mode",
                            RunAsync = () => RunSetupAsync(new[] { "wizard", "--operation", "cleanup" })
                        }
                    }).ConfigureAwait(false);
                    PauseForMenu();
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
                RunAsync = async () => (await RunExternalCommandAsync("gh", "auth status", TimeSpan.FromSeconds(30)).ConfigureAwait(false)).ExitCode
            },
            new ProcessStep {
                Title = "Run doctor checks",
                RunAsync = () => Doctor.DoctorRunner.RunAsync(new[] { "--repo", repo })
            }
        }).ConfigureAwait(false);

        var prs = await RunWithSpinnerAsync("Fetching open pull requests...",
            () => FetchPullRequestsAsync(repo, "open", 50)).ConfigureAwait(false);
        var runs = await RunWithSpinnerAsync("Fetching recent reviewer runs...",
            () => FetchWorkflowRunsAsync(repo, "review-intelligencex.yml", 20)).ConfigureAwait(false);

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
                RunAsync = async () => {
                    var checks = await FetchPullRequestChecksAsync(repo, prNumber.Value).ConfigureAwait(false);
                    return checks.Error is null ? 0 : 1;
                }
            }
        }).ConfigureAwait(false);

        var checksResult = await RunWithSpinnerAsync("Fetching check details...",
            () => FetchPullRequestChecksAsync(repo, prNumber.Value)).ConfigureAwait(false);
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

}
