using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup.Host;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class WizardRunner {
    private const string DefaultGitHubApi = "https://api.github.com";
    private const string DefaultGitHubAuth = "https://github.com";
    private const string DefaultGitHubScopes = "repo workflow read:org";

    public static async Task<int> RunAsync(string[] args) {
        var options = WizardOptions.Parse(args);
        if (options.ShowHelp) {
            WriteHelp();
            return 0;
        }

        if (options.ForcePlain || Console.IsInputRedirected || !AnsiConsole.Profile.Capabilities.Ansi) {
            Console.WriteLine("Wizard requires an interactive terminal. Falling back to setup options.");
            return await SetupRunner.RunAsync(args).ConfigureAwait(false);
        }

        AnsiConsole.Write(new FigletText("IntelligenceX"));
        AnsiConsole.MarkupLine("[grey]Setup wizard (CLI)[/]");
        AnsiConsole.WriteLine();

        var state = new WizardState {
            RepoFullName = options.RepoFullName,
            WithConfig = options.WithConfig,
            SkipSecret = options.SkipSecret,
            ManualSecret = options.ManualSecret,
            ExplicitSecrets = options.ExplicitSecrets,
            DryRun = options.DryRun,
            BranchName = options.BranchName,
            Upgrade = options.Upgrade,
            Force = options.Force,
            Operation = options.Operation,
            AuthBundlePath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH")
        };

        state.Operation = WizardPrompts.PromptOperation();
        state.AuthMode = WizardPrompts.PromptAuthMode();

        if (!await EnsureGitHubTokenAsync(state).ConfigureAwait(false)) {
            AnsiConsole.MarkupLine("[red]GitHub authentication failed.[/]");
            return 1;
        }

        state.Scope = WizardPrompts.PromptScope();
        await ResolveRepositoriesAsync(state).ConfigureAwait(false);

        if (state.SelectedRepos.Count == 0) {
            AnsiConsole.MarkupLine("[red]No repositories selected.[/]");
            return 1;
        }

        if (state.Operation == WizardOperation.Setup) {
            state.ConfigMode = WizardPrompts.PromptConfigMode();
            await ApplyConfigSelectionAsync(state).ConfigureAwait(false);
            if (WizardPrompts.PromptViewWorkflowPreview()) {
                await ShowWorkflowPreviewAsync(state).ConfigureAwait(false);
            }
        } else {
            state.WithConfig = false;
        }
        if (state.Operation == WizardOperation.Setup) {
            state.Provider = WizardPrompts.PromptProvider(state.Provider);
            if (string.Equals(state.Provider, "copilot", StringComparison.OrdinalIgnoreCase)) {
                state.SkipSecret = true;
                state.ManualSecret = false;
            }
            state.SkipSecret = WizardPrompts.PromptSkipSecret(state.SkipSecret);
            if (!state.SkipSecret) {
                state.ManualSecret = WizardPrompts.PromptManualSecret(state.ManualSecret);
            }
            state.ExplicitSecrets = WizardPrompts.PromptExplicitSecrets(state.ExplicitSecrets);
            state.Upgrade = WizardPrompts.PromptUpgradeManaged(state.Upgrade);
            state.Force = WizardPrompts.PromptForceOverwrite(state.Force);
        }
        if (state.Operation == WizardOperation.UpdateSecret) {
            state.SkipSecret = false;
            state.ManualSecret = false;
        }
        if (state.Operation == WizardOperation.Cleanup) {
            state.KeepSecret = WizardPrompts.PromptKeepSecret(state.KeepSecret);
        }
        state.DryRun = WizardPrompts.PromptDryRun(state.DryRun);
        state.BranchName = WizardPrompts.PromptBranchName(state.BranchName);

        var plan = BuildPlan(state, state.SelectedRepos[0]);
        var workflowStatus = await GetWorkflowStatusAsync(state).ConfigureAwait(false);
        var usageLabel = TryLoadCachedUsageSummary();
        WizardSummary.Render(plan, state.SelectedRepos, workflowStatus, state.ConfigSourceLabel, DescribeAuth(state), usageLabel);

        if (!WizardPrompts.PromptConfirmApply()) {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        var failures = 0;
        var prLinks = new List<(string Repo, string Url)>();
        var host = new SetupHost();
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Applying setup...", async _ => {
                foreach (var repoPlan in state.SelectedRepos.Select(repo => BuildPlan(state, repo))) {
                    var result = await host.ApplyWithOutputAsync(repoPlan).ConfigureAwait(false);
                    if (result.ExitCode != 0) {
                        failures++;
                    }
                    var prUrl = ExtractPullRequestUrl(result.Output);
                    if (!string.IsNullOrWhiteSpace(prUrl)) {
                        prLinks.Add((repoPlan.RepoFullName, prUrl!));
                    }
                }
            }).ConfigureAwait(false);

        if (failures > 0) {
            AnsiConsole.MarkupLine($"[red]Setup completed with {failures} failure(s).[/]");
            return 1;
        }

        if (prLinks.Count > 0) {
            RenderPullRequestSummary(prLinks);
        }

        AnsiConsole.MarkupLine("[green]Setup completed successfully.[/]");
        await TryShowUsageAsync(state).ConfigureAwait(false);
        return 0;
    }

    private static SetupPlan BuildPlan(WizardState state, string repo) {
        var plan = new SetupPlan(repo) {
            GitHubClientId = state.GitHubClientId,
            GitHubToken = state.GitHubToken,
            WithConfig = state.WithConfig,
            ConfigPath = state.ConfigPath,
            ConfigJson = state.ConfigJson,
            Provider = state.Provider,
            ReviewProfile = ResolveProfile(state),
            ReviewMode = ResolveMode(state),
            SkipSecret = state.SkipSecret,
            ManualSecret = state.ManualSecret,
            ExplicitSecrets = state.ExplicitSecrets,
            Upgrade = state.Upgrade,
            Force = state.Force,
            UpdateSecret = state.Operation == WizardOperation.UpdateSecret,
            Cleanup = state.Operation == WizardOperation.Cleanup,
            KeepSecret = state.KeepSecret,
            DryRun = state.DryRun,
            BranchName = state.BranchName
        };
        return plan;
    }

    private static string DescribeAuth(WizardState state) {
        return state.AuthMode switch {
            GitHubAuthMode.AppInstallation => "app installation token",
            GitHubAuthMode.DeviceFlow => "device flow",
            GitHubAuthMode.PersonalAccessToken => "personal access token",
            _ => "token"
        };
    }

    private static string? ExtractPullRequestUrl(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }
        foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) {
            var marker = "PR created:";
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) {
                continue;
            }
            return line.Substring(idx + marker.Length).Trim();
        }
        return null;
    }

    private static void RenderPullRequestSummary(IReadOnlyList<(string Repo, string Url)> links) {
        var table = new Table()
            .RoundedBorder()
            .AddColumn("Repository")
            .AddColumn("PR URL");

        foreach (var (repo, url) in links) {
            table.AddRow(repo, url);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static async Task TryShowUsageAsync(WizardState state) {
        if (state.Operation == WizardOperation.Cleanup) {
            return;
        }
        if (!WizardPrompts.PromptCheckUsage()) {
            return;
        }
        var includeEvents = WizardPrompts.PromptIncludeUsageEvents();
        var authPath = !string.IsNullOrWhiteSpace(state.AuthBundlePath)
            ? state.AuthBundlePath!
            : AuthPaths.ResolveAuthPath();
        if (!System.IO.File.Exists(authPath)) {
            var overridePath = WizardPrompts.PromptAuthBundlePath();
            if (string.IsNullOrWhiteSpace(overridePath)) {
                AnsiConsole.MarkupLine("[yellow]No local auth bundle found. Run `intelligencex auth login` first.[/]");
                return;
            }
            authPath = overridePath;
            state.AuthBundlePath = overridePath;
        }
        if (!System.IO.File.Exists(authPath)) {
            AnsiConsole.MarkupLine("[yellow]Auth bundle not found at the specified path.[/]");
            return;
        }
        try {
            var options = new OpenAINativeOptions {
                AuthStore = new FileAuthBundleStore(authPath)
            };
            using var service = new ChatGptUsageService(options);
            var report = await service.GetReportAsync(includeEvents, System.Threading.CancellationToken.None).ConfigureAwait(false);
            TrySaveCache(report.Snapshot);
            RenderUsage(report);
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[yellow]Usage check failed: {ex.Message}[/]");
        }
    }

    private static void TrySaveCache(ChatGptUsageSnapshot snapshot) {
        try {
            ChatGptUsageCache.Save(snapshot);
        } catch {
            // Best-effort cache.
        }
    }

    private static string? TryLoadCachedUsageSummary() {
        try {
            if (!ChatGptUsageCache.TryLoad(out var entry) || entry is null) {
                return null;
            }
            var summary = FormatUsageSummary(entry.Snapshot);
            var updatedAt = entry.UpdatedAt.ToUniversalTime().ToString("u");
            return string.IsNullOrWhiteSpace(summary) ? $"updated {updatedAt}" : $"{summary} (updated {updatedAt})";
        } catch {
            return null;
        }
    }

    private static string FormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(snapshot.PlanType)) {
            parts.Add($"Plan {snapshot.PlanType}");
        }
        if (snapshot.Credits is not null && snapshot.Credits.Balance.HasValue) {
            parts.Add($"Credits {snapshot.Credits.Balance.Value:0.####}");
        }
        if (snapshot.RateLimit is not null && snapshot.RateLimit.LimitReached) {
            parts.Add("Limit reached");
        }
        return string.Join(" | ", parts);
    }

    private static void RenderUsage(ChatGptUsageReport report) {
        var snapshot = report.Snapshot;
        var table = new Table()
            .RoundedBorder()
            .AddColumn("Metric")
            .AddColumn("Value");

        if (!string.IsNullOrWhiteSpace(snapshot.PlanType)) {
            table.AddRow("Plan", snapshot.PlanType!);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.Email)) {
            table.AddRow("Email", snapshot.Email!);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.AccountId)) {
            table.AddRow("Account", snapshot.AccountId!);
        }

        AddRateLimitRows(table, "Rate limit", snapshot.RateLimit);
        AddRateLimitRows(table, "Code review limit", snapshot.CodeReviewRateLimit);

        if (snapshot.Credits is not null) {
            table.AddRow("Credits (has)", snapshot.Credits.HasCredits.ToString());
            table.AddRow("Credits (unlimited)", snapshot.Credits.Unlimited.ToString());
            if (snapshot.Credits.Balance.HasValue) {
                table.AddRow("Credits balance", snapshot.Credits.Balance.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]ChatGPT usage[/]");
        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        if (report.Events.Count > 0) {
            var eventsTable = new Table()
                .RoundedBorder()
                .AddColumn("Date")
                .AddColumn("Surface")
                .AddColumn("Credits")
                .AddColumn("Usage Id");
            foreach (var evt in report.Events) {
                eventsTable.AddRow(evt.Date ?? "-", evt.ProductSurface ?? "-", FormatCredits(evt.CreditAmount), evt.UsageId ?? "-");
            }
            AnsiConsole.MarkupLine("[grey]Credit usage events[/]");
            AnsiConsole.Write(eventsTable);
            AnsiConsole.WriteLine();
        }
    }

    private static void AddRateLimitRows(Table table, string label, ChatGptRateLimitStatus? status) {
        if (status is null) {
            return;
        }
        table.AddRow($"{label} allowed", status.Allowed.ToString());
        table.AddRow($"{label} limit reached", status.LimitReached.ToString());
        if (status.PrimaryWindow is not null) {
            table.AddRow($"{label} primary", FormatWindow(status.PrimaryWindow));
        }
        if (status.SecondaryWindow is not null) {
            table.AddRow($"{label} secondary", FormatWindow(status.SecondaryWindow));
        }
    }

    private static string FormatWindow(ChatGptRateLimitWindow window) {
        var parts = new List<string>();
        if (window.UsedPercent.HasValue) {
            parts.Add($"{window.UsedPercent.Value:0.#}%");
        }
        if (window.LimitWindowSeconds.HasValue) {
            var span = TimeSpan.FromSeconds(Math.Max(0, window.LimitWindowSeconds.Value));
            parts.Add($"{(int)span.TotalMinutes}m");
        }
        if (window.ResetAfterSeconds.HasValue) {
            var span = TimeSpan.FromSeconds(Math.Max(0, window.ResetAfterSeconds.Value));
            parts.Add($"resets in {(int)span.TotalMinutes}m");
        } else if (window.ResetAtUnixSeconds.HasValue) {
            var resetAt = DateTimeOffset.FromUnixTimeSeconds(window.ResetAtUnixSeconds.Value).ToUniversalTime();
            parts.Add($"reset at {resetAt:u}");
        }
        return parts.Count == 0 ? "n/a" : string.Join(", ", parts);
    }

    private static string FormatCredits(double? value) {
        return value.HasValue
            ? value.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            : "-";
    }

    private static async Task<bool> EnsureGitHubTokenAsync(WizardState state) {
        if (state.AuthMode == GitHubAuthMode.PersonalAccessToken) {
            state.GitHubToken = WizardPrompts.PromptGitHubToken();
            return !string.IsNullOrWhiteSpace(state.GitHubToken);
        }

        if (state.AuthMode == GitHubAuthMode.AppInstallation) {
            var store = new GitHubAppStore();
            var savedProfiles = store.LoadAll();
            if (savedProfiles.Count > 0) {
                var selectedProfile = WizardPrompts.PromptSavedApp(savedProfiles);
                if (selectedProfile is not null) {
                    state.GitHubAppId = selectedProfile.AppId;
                    state.GitHubAppKeyPath = selectedProfile.KeyPath;
                    if (!string.IsNullOrWhiteSpace(selectedProfile.KeyPath) && System.IO.File.Exists(selectedProfile.KeyPath)) {
                        state.GitHubAppKeyPem = System.IO.File.ReadAllText(selectedProfile.KeyPath);
                    }
                    state.GitHubInstallationId = selectedProfile.DefaultInstallationId;
                }
            }

            if (!state.GitHubAppId.HasValue && WizardPrompts.PromptCreateAppFromManifest()) {
                var appName = WizardPrompts.PromptAppName("IntelligenceX Reviewer");
                var owner = WizardPrompts.PromptAppOwner();
                var result = await GitHubAppManifestFlow.RunAsync(new GitHubAppManifestOptions {
                    AppName = appName,
                    Owner = owner,
                    AuthBaseUrl = DefaultGitHubAuth,
                    ApiBaseUrl = DefaultGitHubApi
                }, CancellationToken.None).ConfigureAwait(false);
                if (result is not null) {
                    state.GitHubAppId = result.AppId;
                    state.GitHubAppKeyPem = result.Pem;
                    state.GitHubAppKeyPath = SavePemToDisk(result.Pem);
                }
            }
            if (!state.GitHubAppId.HasValue) {
                state.GitHubAppId = WizardPrompts.PromptGitHubAppId();
            }
            if (!state.GitHubAppId.HasValue) {
                return false;
            }

            if (string.IsNullOrWhiteSpace(state.GitHubAppKeyPem)) {
                var keyPath = WizardPrompts.PromptGitHubAppKeyPath();
                if (!string.IsNullOrWhiteSpace(keyPath)) {
                    try {
                        state.GitHubAppKeyPem = System.IO.File.ReadAllText(keyPath);
                        state.GitHubAppKeyPath = keyPath;
                    } catch {
                        return false;
                    }
                } else {
                    state.GitHubAppKeyPem = WizardPrompts.PromptGitHubAppKeyPem();
                }
            }

            if (string.IsNullOrWhiteSpace(state.GitHubAppKeyPem)) {
                return false;
            }

            try {
                using var appClient = new GitHubAppClient(state.GitHubAppId.Value, state.GitHubAppKeyPem, DefaultGitHubApi);
                var installs = await appClient.ListInstallationsAsync().ConfigureAwait(false);
                if (installs.Count == 0) {
                    WizardPrompts.ShowInstallAppHint(state.GitHubAppId.Value);
                    return false;
                }
                var selected = state.GitHubInstallationId ?? WizardPrompts.PromptInstallation(installs);
                if (!selected.HasValue) {
                    return false;
                }
                state.GitHubInstallationId = selected;
                state.GitHubToken = await appClient.CreateInstallationTokenAsync(selected.Value).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(state.GitHubAppKeyPath) && WizardPrompts.PromptSaveApp()) {
                    var name = WizardPrompts.PromptAppProfileName($"app-{state.GitHubAppId}");
                    store.Save(new GitHubAppProfile {
                        Name = name ?? $"app-{state.GitHubAppId}",
                        AppId = state.GitHubAppId.Value,
                        KeyPath = state.GitHubAppKeyPath,
                        DefaultInstallationId = state.GitHubInstallationId
                    }, makeDefault: true);
                }

                return !string.IsNullOrWhiteSpace(state.GitHubToken);
            } catch {
                return false;
            }
        }

        var fallback = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_CLIENT_ID");
        state.GitHubClientId = WizardPrompts.PromptGitHubClientId(fallback);
        if (string.IsNullOrWhiteSpace(state.GitHubClientId)) {
            return false;
        }

        var token = await GitHubAuth.DeviceFlowAsync(state.GitHubClientId!, DefaultGitHubAuth, DefaultGitHubScopes)
            .ConfigureAwait(false);
        state.GitHubToken = token;
        return !string.IsNullOrWhiteSpace(state.GitHubToken);
    }

    private static string SavePemToDisk(string pem) {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) {
            home = ".";
        }
        var dir = System.IO.Path.Combine(home, ".intelligencex");
        System.IO.Directory.CreateDirectory(dir);
        var uniqueFileName = $"github-app-private-key-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.pem";
        var path = System.IO.Path.Combine(dir, uniqueFileName);
        System.IO.File.WriteAllText(path, pem);
        return path;
    }

    private static async Task ResolveRepositoriesAsync(WizardState state) {
        if (state.Scope == SetupScope.Manual) {
            state.SelectedRepos.AddRange(WizardPrompts.PromptManualRepos());
            return;
        }

        var repos = await LoadRepositoriesAsync(state).ConfigureAwait(false);
        if (repos.Count == 0) {
            state.SelectedRepos.AddRange(WizardPrompts.PromptManualRepos());
            return;
        }

        var filtered = FilterRepositories(repos);
        if (filtered.Count == 0) {
            state.SelectedRepos.AddRange(WizardPrompts.PromptManualRepos());
            return;
        }

        if (state.Scope == SetupScope.SingleRepo) {
            var selected = WizardPrompts.PromptSingleRepo(filtered);
            state.SelectedRepos.Add(selected);
            return;
        }

        var selectedRepos = WizardPrompts.PromptMultipleRepos(filtered);
        state.SelectedRepos.AddRange(selectedRepos);
    }

    private static async Task ApplyConfigSelectionAsync(WizardState state) {
        if (state.ConfigMode == ConfigMode.None) {
            state.WithConfig = false;
            return;
        }

        if (state.ConfigMode == ConfigMode.Preset) {
            state.Preset = WizardPrompts.PromptPreset();
            state.WithConfig = true;
            return;
        }

        if (state.ConfigMode == ConfigMode.Existing) {
            state.WithConfig = true;
            state.ConfigSourceLabel = "loaded from repo";
            var repo = SelectRepoForInspection(state, "Select repository to load config from:");
            if (string.IsNullOrWhiteSpace(repo)) {
                state.WithConfig = false;
                state.ConfigSourceLabel = null;
                return;
            }
            var (content, branch) = await TryLoadRepoFileAsync(state, repo, ".intelligencex/config.json")
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) {
                AnsiConsole.MarkupLine("[yellow]Config not found in default branch.[/]");
                state.WithConfig = false;
                state.ConfigSourceLabel = null;
                return;
            }
            if (!WizardConfigEditor.IsValidJson(content)) {
                AnsiConsole.MarkupLine("[red]Config JSON is invalid. Skipping config.[/]");
                state.WithConfig = false;
                state.ConfigSourceLabel = null;
                return;
            }
            state.ConfigJson = content;
            AnsiConsole.MarkupLine($"[green]Loaded config from {repo} ({branch ?? "default"}).[/]");
            if (WizardPrompts.PromptEditLoadedConfig()) {
                var edited = WizardConfigEditor.EditInEditor(state.ConfigJson);
                if (!string.IsNullOrWhiteSpace(edited)) {
                    state.ConfigJson = edited;
                }
            }
            return;
        }

        state.WithConfig = true;
        state.ConfigSourceLabel = null;
        var source = WizardPrompts.PromptConfigSource();
        switch (source) {
            case ConfigSource.Editor:
                state.ConfigJson = WizardConfigEditor.EditInEditor();
                break;
            case ConfigSource.Path: {
                var path = WizardPrompts.PromptConfigPath();
                if (!string.IsNullOrWhiteSpace(path)) {
                    state.ConfigPath = path;
                }
                break;
            }
            case ConfigSource.Paste:
                state.ConfigJson = WizardPrompts.PromptConfigJson();
                break;
        }

        if (!string.IsNullOrWhiteSpace(state.ConfigJson) && !WizardConfigEditor.IsValidJson(state.ConfigJson)) {
            AnsiConsole.MarkupLine("[red]Invalid JSON provided. Skipping config.[/]");
            state.ConfigJson = null;
            state.WithConfig = false;
        }

        if (!string.IsNullOrWhiteSpace(state.ConfigPath)) {
            try {
                var content = System.IO.File.ReadAllText(state.ConfigPath);
                if (!WizardConfigEditor.IsValidJson(content)) {
                    AnsiConsole.MarkupLine("[red]Invalid JSON in config file. Skipping config.[/]");
                    state.ConfigPath = null;
                    state.WithConfig = false;
                }
            } catch {
                AnsiConsole.MarkupLine("[red]Failed to read config file. Skipping config.[/]");
                state.ConfigPath = null;
                state.WithConfig = false;
            }
        }
    }

    private static string? ResolveProfile(WizardState state) {
        if (state.ConfigMode != ConfigMode.Preset) {
            return null;
        }
        return state.Preset switch {
            ConfigPreset.Balanced => "balanced",
            ConfigPreset.Strict => "picky",
            ConfigPreset.Security => "security",
            ConfigPreset.Minimal => "highlevel",
            ConfigPreset.Performance => "performance",
            ConfigPreset.Tests => "tests",
            _ => "balanced"
        };
    }

    private static string? ResolveMode(WizardState state) {
        if (state.ConfigMode != ConfigMode.Preset) {
            return null;
        }
        return state.Preset == ConfigPreset.Minimal ? "summary" : "hybrid";
    }

    private static async Task ShowWorkflowPreviewAsync(WizardState state) {
        if (string.IsNullOrWhiteSpace(state.GitHubToken)) {
            AnsiConsole.MarkupLine("[yellow]GitHub token is required to load workflow.[/]");
            return;
        }
        var repo = SelectRepoForInspection(state, "Select repository to preview workflow:");
        if (string.IsNullOrWhiteSpace(repo)) {
            return;
        }
        var (content, branch) = await TryLoadRepoFileAsync(state, repo, ".github/workflows/review-intelligencex.yml")
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            AnsiConsole.MarkupLine("[yellow]Workflow not found in default branch.[/]");
            return;
        }
        var managed = content.Contains("INTELLIGENCEX:BEGIN", StringComparison.Ordinal);
        var header = $"Workflow preview ({(managed ? "managed" : "unmanaged")})";
        AnsiConsole.Write(new Panel(new Text(content)) {
            Header = new PanelHeader(header),
            Border = BoxBorder.Rounded
        });
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Loaded from {repo} ({branch ?? "default"}).[/]");
        AnsiConsole.WriteLine();
    }

    private static async Task<string?> GetWorkflowStatusAsync(WizardState state) {
        if (state.SelectedRepos.Count == 0 || string.IsNullOrWhiteSpace(state.GitHubToken)) {
            return null;
        }
        var repo = state.SelectedRepos[0];
        var (content, _) = await TryLoadRepoFileAsync(state, repo, ".github/workflows/review-intelligencex.yml")
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content)) {
            return state.SelectedRepos.Count > 1 ? "missing (first repo)" : "missing";
        }
        var managed = content.Contains("INTELLIGENCEX:BEGIN", StringComparison.Ordinal);
        var status = managed ? "managed" : "unmanaged";
        return state.SelectedRepos.Count > 1 ? $"{status} (first repo)" : status;
    }

    private static async Task<List<string>> LoadRepositoriesAsync(WizardState state) {
        if (string.IsNullOrWhiteSpace(state.GitHubToken)) {
            return new List<string>();
        }

        try {
            using var client = new GitHubRepoClient(state.GitHubToken, DefaultGitHubApi);
            var repos = state.AuthMode == GitHubAuthMode.AppInstallation
                ? await client.ListInstallationRepositoriesAsync().ConfigureAwait(false)
                : await client.ListRepositoriesAsync().ConfigureAwait(false);
            return repos
                .OrderByDescending(r => r.UpdatedAt ?? DateTimeOffset.MinValue)
                .Select(r => r.FullName)
                .ToList();
        } catch {
            return new List<string>();
        }
    }

    private static List<string> FilterRepositories(IReadOnlyList<string> repos) {
        var filter = WizardPrompts.PromptFilter();
        if (string.IsNullOrWhiteSpace(filter)) {
            return repos.ToList();
        }
        return repos
            .Where(repo => repo.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static string? SelectRepoForInspection(WizardState state, string title) {
        if (state.SelectedRepos.Count == 0) {
            return null;
        }
        if (state.SelectedRepos.Count == 1) {
            return state.SelectedRepos[0];
        }
        return WizardPrompts.PromptRepoForInspection(state.SelectedRepos, title);
    }

    private static async Task<(string? Content, string? Branch)> TryLoadRepoFileAsync(WizardState state, string repo, string path) {
        if (string.IsNullOrWhiteSpace(state.GitHubToken)) {
            return (null, null);
        }
        if (!TryParseRepo(repo, out var owner, out var name)) {
            return (null, null);
        }
        try {
            using var client = new GitHubRepoClient(state.GitHubToken, DefaultGitHubApi);
            var defaultBranch = await client.GetDefaultBranchAsync(owner, name).ConfigureAwait(false);
            var file = await client.TryGetFileAsync(owner, name, path, defaultBranch).ConfigureAwait(false);
            return (file?.Content, defaultBranch);
        } catch {
            return (null, null);
        }
    }

    private static bool TryParseRepo(string repo, out string owner, out string name) {
        owner = string.Empty;
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(repo)) {
            return false;
        }
        var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2) {
            return false;
        }
        owner = parts[0];
        name = parts[1];
        return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(name);
    }

    private static void WriteHelp() {
        Console.WriteLine("IntelligenceX setup wizard");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex setup wizard [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --with-config");
        Console.WriteLine("  --skip-secret");
        Console.WriteLine("  --manual-secret");
        Console.WriteLine("  --explicit-secrets");
        Console.WriteLine("  --operation <setup|update-secret|cleanup>");
        Console.WriteLine("  --upgrade");
        Console.WriteLine("  --force");
        Console.WriteLine("  --dry-run");
        Console.WriteLine("  --branch <name>");
        Console.WriteLine("  --plain (disable wizard UI)");
        Console.WriteLine("  --help");
    }

    private sealed class WizardOptions {
        public string? RepoFullName { get; set; }
        public bool WithConfig { get; set; }
        public bool SkipSecret { get; set; }
        public bool ManualSecret { get; set; }
        public bool ExplicitSecrets { get; set; }
        public bool Upgrade { get; set; }
        public bool Force { get; set; }
        public WizardOperation Operation { get; set; }
        public bool DryRun { get; set; }
        public string? BranchName { get; set; }
        public bool ForcePlain { get; set; }
        public bool ShowHelp { get; set; }

        public static WizardOptions Parse(string[] args) {
            var options = new WizardOptions();
            for (var i = 0; i < args.Length; i++) {
                var arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal)) {
                    continue;
                }
                var key = arg.Substring(2);
                var value = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? args[++i]
                    : "true";

                switch (key) {
                    case "repo":
                        options.RepoFullName = value;
                        break;
                    case "with-config":
                        options.WithConfig = ParseBool(value, true);
                        break;
                    case "skip-secret":
                        options.SkipSecret = ParseBool(value, true);
                        break;
                    case "manual-secret":
                        options.ManualSecret = ParseBool(value, true);
                        break;
                    case "explicit-secrets":
                        options.ExplicitSecrets = ParseBool(value, true);
                        break;
                    case "upgrade":
                        options.Upgrade = ParseBool(value, true);
                        break;
                    case "force":
                        options.Force = ParseBool(value, true);
                        break;
                    case "operation":
                        options.Operation = ParseOperation(value);
                        break;
                    case "dry-run":
                        options.DryRun = ParseBool(value, true);
                        break;
                    case "branch":
                        options.BranchName = value;
                        break;
                    case "plain":
                        options.ForcePlain = ParseBool(value, true);
                        break;
                    case "help":
                        options.ShowHelp = true;
                        break;
                }
            }
            return options;
        }

        private static bool ParseBool(string value, bool fallback) {
            if (bool.TryParse(value, out var parsed)) {
                return parsed;
            }
            return fallback;
        }

        private static WizardOperation ParseOperation(string value) {
            if (string.IsNullOrWhiteSpace(value)) {
                return WizardOperation.Setup;
            }
            return value.Trim().ToLowerInvariant() switch {
                "cleanup" => WizardOperation.Cleanup,
                "update-secret" => WizardOperation.UpdateSecret,
                "update" => WizardOperation.UpdateSecret,
                _ => WizardOperation.Setup
            };
        }
    }
}
