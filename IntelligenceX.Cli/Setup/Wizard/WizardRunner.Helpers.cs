using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup;
using IntelligenceX.Cli.Setup.Host;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static partial class WizardRunner {
    private static void ApplyAnalysisSelection(WizardState state) {
        if (state.Operation != WizardOperation.Setup || state.ConfigMode != ConfigMode.Preset) {
            state.AnalysisEnabled = null;
            state.AnalysisGateEnabled = null;
            state.AnalysisRunStrict = null;
            state.AnalysisPacks = null;
            state.AnalysisExportPath = null;
            return;
        }

        // Preset mode always generates reviewer.json; ensure state reflects that.
        state.WithConfig = true;

        var analysisEnabled = WizardPrompts.PromptAnalysisEnabled(state.AnalysisEnabled ?? true);
        state.AnalysisEnabled = analysisEnabled;
        if (!analysisEnabled) {
            state.AnalysisGateEnabled = null;
            state.AnalysisRunStrict = null;
            state.AnalysisPacks = null;
            state.AnalysisExportPath = null;
            return;
        }

        state.AnalysisPacks = WizardPrompts.PromptAnalysisPacks(state.AnalysisPacks);
        if (string.Equals(state.AnalysisPacks, WizardPrompts.DisableAnalysisSelection, StringComparison.Ordinal)) {
            state.AnalysisEnabled = false;
            state.AnalysisGateEnabled = null;
            state.AnalysisRunStrict = null;
            state.AnalysisPacks = null;
            state.AnalysisExportPath = null;
            return;
        }
        state.AnalysisGateEnabled = WizardPrompts.PromptAnalysisGateEnabled(state.AnalysisGateEnabled ?? false);
        state.AnalysisRunStrict = WizardPrompts.PromptAnalysisRunStrict(state.AnalysisRunStrict ?? false);
        state.AnalysisExportPath = WizardPrompts.PromptAnalysisExportPath(state.AnalysisExportPath);
    }

    private static SetupPlan BuildPlan(WizardState state, string repo) {
        var withConfig = state.WithConfig ||
                         !string.IsNullOrWhiteSpace(state.ConfigPath) ||
                         !string.IsNullOrWhiteSpace(state.ConfigJson);
        var analysisApplies = withConfig &&
                              state.Operation == WizardOperation.Setup &&
                              state.ConfigMode == ConfigMode.Preset;
        var openAiAccountRoutingApplies = analysisApplies &&
                                          IsOpenAiProvider(state.Provider);
        var allowSecrets = state.Operation == WizardOperation.Setup;
        var plan = new SetupPlan(repo) {
            GitHubClientId = state.GitHubClientId,
            GitHubToken = state.GitHubToken,
            WithConfig = withConfig,
            ConfigPath = state.ConfigPath,
            ConfigJson = state.ConfigJson,
            AuthB64 = ShouldPassAuthB64(state) ? state.OpenAiAuthB64 : null,
            Provider = state.Provider,
            OpenAIModel = state.OpenAiModel,
            OpenAIAccountId = openAiAccountRoutingApplies ? state.OpenAiAccountId : null,
            OpenAIAccountIds = openAiAccountRoutingApplies ? state.OpenAiAccountIds : null,
            OpenAIAccountRotation = openAiAccountRoutingApplies && !string.IsNullOrWhiteSpace(state.OpenAiAccountIds)
                ? state.OpenAiAccountRotation
                : null,
            OpenAIAccountFailover = openAiAccountRoutingApplies && !string.IsNullOrWhiteSpace(state.OpenAiAccountIds)
                ? state.OpenAiAccountFailover
                : null,
            ReviewProfile = ResolveProfile(state),
            ReviewMode = ResolveMode(state),
            SkipSecret = allowSecrets && state.SkipSecret,
            ManualSecret = allowSecrets && state.ManualSecret,
            ManualSecretStdout = allowSecrets && state.ManualSecret && state.ManualSecretStdout,
            ExplicitSecrets = allowSecrets && state.ExplicitSecrets,
            Upgrade = allowSecrets && state.Upgrade,
            Force = allowSecrets && state.Force,
            UpdateSecret = state.Operation == WizardOperation.UpdateSecret,
            Cleanup = state.Operation == WizardOperation.Cleanup,
            KeepSecret = state.KeepSecret,
            DryRun = state.DryRun,
            BranchName = state.BranchName,
            AnalysisEnabled = analysisApplies ? state.AnalysisEnabled : null,
            AnalysisGateEnabled = analysisApplies ? state.AnalysisGateEnabled : null,
            AnalysisRunStrict = analysisApplies && state.AnalysisEnabled == true ? state.AnalysisRunStrict : null,
            AnalysisPacks = analysisApplies ? state.AnalysisPacks : null,
            AnalysisExportPath = analysisApplies && state.AnalysisEnabled == true ? state.AnalysisExportPath : null
        };
        return plan;
    }

    private static string DescribeAuth(WizardState state) {
        return state.AuthMode switch {
            GitHubAuthMode.DefaultDeviceFlow => "IntelligenceX app (device flow)",
            GitHubAuthMode.AppInstallation => "app installation token",
            GitHubAuthMode.CustomDeviceFlow => "custom device flow",
            GitHubAuthMode.PersonalAccessToken => "personal access token",
            _ => "token"
        };
    }

    private static SetupPostApplyContext BuildPostApplyVerifyContext(
        WizardState state,
        SetupPlan plan,
        SetupHostResult result,
        string? pullRequestUrl) {
        var operation = state.Operation switch {
            WizardOperation.Cleanup => SetupApplyOperation.Cleanup,
            WizardOperation.UpdateSecret => SetupApplyOperation.UpdateSecret,
            _ => SetupApplyOperation.Setup
        };
        var provider = !string.IsNullOrWhiteSpace(plan.Provider) ? plan.Provider! : state.Provider;
        var expectOrgSecret = (state.Operation == WizardOperation.Setup || state.Operation == WizardOperation.UpdateSecret) &&
                              IsOpenAiProvider(provider) &&
                              state.SecretTarget == SecretTarget.Org;
        var withConfig = plan.WithConfig ||
                         !string.IsNullOrWhiteSpace(plan.ConfigJson) ||
                         !string.IsNullOrWhiteSpace(plan.ConfigPath);

        return new SetupPostApplyContext {
            Repo = plan.RepoFullName,
            Operation = operation,
            WithConfig = withConfig,
            SkipSecret = plan.SkipSecret,
            ManualSecret = plan.ManualSecret,
            KeepSecret = plan.KeepSecret,
            DryRun = plan.DryRun,
            ExitSuccess = result.ExitCode == 0,
            ExpectOrgSecret = expectOrgSecret,
            SecretOrg = expectOrgSecret ? state.SecretOrg : null,
            Provider = provider,
            Output = result.Output,
            PullRequestUrl = pullRequestUrl
        };
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

    private static void RenderPostApplyVerificationSummary(IReadOnlyList<SetupPostApplyVerification> results) {
        var table = new Table()
            .RoundedBorder()
            .AddColumn("Repository")
            .AddColumn("Verify")
            .AddColumn("Details");

        foreach (var verify in results) {
            var status = verify.Skipped
                ? "[grey]skipped[/]"
                : verify.Passed
                    ? "[green]ok[/]"
                    : "[red]failed[/]";

            var details = new List<string>();
            if (!string.IsNullOrWhiteSpace(verify.CheckedRef)) {
                details.Add($"{verify.CheckedRefSource ?? "ref"}={verify.CheckedRef}");
            }
            foreach (var check in verify.Checks) {
                var checkStatus = check.Skipped ? "skip" : (check.Passed ? "ok" : "fail");
                var checkDetail = $"{check.Name}:{checkStatus}";
                if (!string.IsNullOrWhiteSpace(check.Note)) {
                    checkDetail += $" ({check.Note})";
                }
                details.Add(checkDetail);
            }
            if (!string.IsNullOrWhiteSpace(verify.Note)) {
                details.Add(verify.Note!);
            }

            table.AddRow(verify.Repo, status, string.Join("; ", details));
        }

        AnsiConsole.MarkupLine("[grey]Post-apply verification[/]");
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
            var report = await service.GetReportAsync(includeEvents, cancellationToken: CancellationToken.None).ConfigureAwait(false);
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
                table.AddRow("Credits balance",
                    snapshot.Credits.Balance.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
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
            return await EnsureGitHubAppTokenAsync(state).ConfigureAwait(false);
        }

        // DefaultDeviceFlow or CustomDeviceFlow
        if (state.AuthMode == GitHubAuthMode.DefaultDeviceFlow) {
            // Use the built-in IntelligenceX GitHub App Client ID
            state.GitHubClientId = IntelligenceXDefaults.GetEffectiveGitHubClientId();
            AnsiConsole.MarkupLine($"[dim]Using IntelligenceX GitHub App for authentication[/]");
        } else {
            // CustomDeviceFlow - prompt for Client ID
            var fallback = Environment.GetEnvironmentVariable(IntelligenceXDefaults.GitHubClientIdEnvVar);
            state.GitHubClientId = WizardPrompts.PromptGitHubClientId(fallback);
            if (string.IsNullOrWhiteSpace(state.GitHubClientId)) {
                AnsiConsole.MarkupLine("[yellow]No Client ID provided. Cannot proceed with device flow.[/]");
                return false;
            }
        }

        var token = await GitHubAuth.DeviceFlowAsync(state.GitHubClientId!, DefaultGitHubAuth, IntelligenceXDefaults.GitHubScopes)
            .ConfigureAwait(false);
        state.GitHubToken = token;
        return !string.IsNullOrWhiteSpace(state.GitHubToken);
    }

    private static async Task<bool> EnsureGitHubAppTokenAsync(WizardState state) {
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
            state.ConfigSourceLabel = "loaded from repo";
            string? content;
            string? branch;
            (content, branch) = await TryLoadRepoFileAsync(state, repo, ".intelligencex/reviewer.json")
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(content)) {
                // Backward compatibility: older setup flows wrote reviewer settings into `.intelligencex/config.json`.
                (content, branch) = await TryLoadRepoFileAsync(state, repo, ".intelligencex/config.json")
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(content)) {
                    state.ConfigSourceLabel = "loaded from repo (legacy config.json)";
                }
            }
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

}
