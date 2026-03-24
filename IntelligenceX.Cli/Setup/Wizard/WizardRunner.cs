using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup;
using IntelligenceX.Cli.Setup.Host;
using IntelligenceX.Cli.Setup.Onboarding;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static partial class WizardRunner {
    private const string DefaultGitHubApi = "https://api.github.com";
    private const string DefaultGitHubAuth = "https://github.com";

    public static async Task<int> RunAsync(string[] args) {
        var options = WizardOptions.Parse(args);
        if (!string.IsNullOrWhiteSpace(options.ParseError)) {
            Console.Error.WriteLine(options.ParseError);
            WriteHelp();
            return 1;
        }
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
            ManualSecretStdout = options.ManualSecretStdout,
            ExplicitSecrets = options.ExplicitSecrets,
            DryRun = options.DryRun,
            BranchName = options.BranchName,
            Upgrade = options.Upgrade,
            Force = options.Force,
            Operation = options.Operation,
            OnboardingPathId = options.PathSpecified
                ? options.PathId!
                : ResolvePathIdFromOperation(options.Operation),
            AuthBundlePath = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_PATH")
        };

        if (options.PathSpecified) {
            state.Operation = ResolveOperationFromPathId(state.OnboardingPathId);
        } else if (options.OperationSpecified) {
            state.OnboardingPathId = ResolvePathIdFromOperation(state.Operation);
        } else {
            SetupOnboardingAutoDetectResult? autoDetect = null;
            try {
                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync("Running auto-detect preflight...", async _ => {
                        autoDetect = await SetupOnboardingAutoDetectRunner
                            .RunAsync(Environment.CurrentDirectory, options.RepoFullName)
                            .ConfigureAwait(false);
                    }).ConfigureAwait(false);
            } catch (Exception ex) {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(FormatAutoDetectUnavailableMessage(ex, options.Verbose))}[/]");
            }

            if (autoDetect is not null) {
                RenderAutoDetectSummary(autoDetect);
            } else {
                AnsiConsole.MarkupLine("[yellow]Auto-detect preflight unavailable. Choose onboarding path manually.[/]");
            }

            var (recommendedPathId, recommendedReason) = ResolveAutoDetectPromptRecommendation(autoDetect);
            state.OnboardingPathId = WizardPrompts.PromptOnboardingPath(recommendedPathId, recommendedReason);
            state.Operation = ResolveOperationFromPathId(state.OnboardingPathId);
        }

        // Show trust info before auth mode selection
        WizardPrompts.ShowTrustInfo();
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
            ApplyAnalysisSelection(state);
            if (WizardPrompts.PromptViewWorkflowPreview()) {
                await ShowWorkflowPreviewAsync(state).ConfigureAwait(false);
            }
        } else {
            state.WithConfig = false;
        }
        if (state.Operation == WizardOperation.Setup) {
            var previousProvider = state.Provider;
            state.Provider = WizardPrompts.PromptProvider(state.Provider);
            if (string.Equals(state.Provider, SetupProviderCatalog.CopilotProvider, StringComparison.OrdinalIgnoreCase)) {
                state.SkipSecret = true;
                state.ManualSecret = false;
                state.ManualSecretStdout = false;
                state.OpenAiModel = null;
                state.OpenAiAccountId = null;
                state.OpenAiAccountIds = null;
                state.OpenAiAccountRotation = "first-available";
                state.OpenAiAccountFailover = true;
                state.AnthropicApiKey = null;
                state.AnthropicApiKeyPath = null;
            } else if (SetupProviderCatalog.IsClaudeProvider(state.Provider)) {
                state.OpenAiModel = ResolveSuggestedModelForProvider(previousProvider, state.Provider, state.OpenAiModel);
                state.OpenAiAccountId = null;
                state.OpenAiAccountIds = null;
                state.OpenAiAccountRotation = "first-available";
                state.OpenAiAccountFailover = true;
            } else {
                state.OpenAiModel = ResolveSuggestedModelForProvider(previousProvider, state.Provider, state.OpenAiModel);
            }
            if (!string.Equals(state.Provider, SetupProviderCatalog.CopilotProvider, StringComparison.OrdinalIgnoreCase)) {
                state.OpenAiModel = WizardPrompts.PromptModel(state.Provider, state.OpenAiModel);
            }
            if (state.WithConfig && state.ConfigMode == ConfigMode.Preset && SetupProviderCatalog.SupportsOpenAiAccountRouting(state.Provider)) {
                state.OpenAiAccountId = WizardPrompts.PromptOpenAiAccountId(state.OpenAiAccountId);
                state.OpenAiAccountIds = WizardPrompts.PromptOpenAiAccountIds(state.OpenAiAccountIds, state.OpenAiAccountId);
                if (!string.IsNullOrWhiteSpace(state.OpenAiAccountIds)) {
                    state.OpenAiAccountRotation = WizardPrompts.PromptOpenAiAccountRotation(state.OpenAiAccountRotation);
                    state.OpenAiAccountFailover = WizardPrompts.PromptOpenAiAccountFailover(state.OpenAiAccountFailover);
                }
            }
            state.SkipSecret = WizardPrompts.PromptSkipSecret(state.Provider, state.SkipSecret);
            if (!state.SkipSecret) {
                state.ManualSecret = WizardPrompts.PromptManualSecret(state.Provider, state.ManualSecret);
            }
            state.ExplicitSecrets = WizardPrompts.PromptExplicitSecrets(state.ExplicitSecrets);
            state.Upgrade = WizardPrompts.PromptUpgradeManaged(state.Upgrade);
            state.Force = WizardPrompts.PromptForceOverwrite(state.Force);
        }
        if (state.Operation == WizardOperation.UpdateSecret) {
            state.SkipSecret = false;
            state.ManualSecret = false;
            state.ManualSecretStdout = false;
            state.ExplicitSecrets = false;
            state.Upgrade = false;
            state.Force = false;
        }
        if (state.Operation == WizardOperation.Cleanup) {
            state.KeepSecret = WizardPrompts.PromptKeepSecret(state.Provider, state.KeepSecret);
        }
        state.DryRun = WizardPrompts.PromptDryRun(state.DryRun);
        state.BranchName = WizardPrompts.PromptBranchName(state.BranchName);

        if (SetupProviderCatalog.SupportsOrgSecret(state.Provider) && !state.SkipSecret && !state.ManualSecret && state.Operation != WizardOperation.Cleanup) {
            var ownersMatch = TryGetCommonOwner(state.SelectedRepos, out var owner);
            state.SecretTarget = WizardPrompts.PromptSecretTarget(state.SelectedRepos.Count, ownersMatch);
            if (state.SecretTarget == SecretTarget.Org) {
                state.SecretOrg = WizardPrompts.PromptOrg(owner);
                state.SecretVisibility = WizardPrompts.PromptOrgSecretVisibility();
                state.DeleteRepoSecretsAfterOrgSecret = WizardPrompts.PromptDeleteRepoSecrets(state.DeleteRepoSecretsAfterOrgSecret);
            }
        }

        var plan = BuildPlan(state, state.SelectedRepos[0]);
        var workflowStatus = await GetWorkflowStatusAsync(state).ConfigureAwait(false);
        var usageLabel = TryLoadCachedUsageSummary();
        WizardSummary.Render(plan, state.SelectedRepos, workflowStatus, state.ConfigSourceLabel, DescribeAuth(state), usageLabel, DescribeSecretTarget(state));

        if (!WizardPrompts.PromptConfirmApply()) {
            AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
            return 1;
        }

        if (SetupProviderCatalog.RequiresManagedSecret(state.Provider) && !state.SkipSecret && !state.ManualSecret && state.Operation != WizardOperation.Cleanup) {
            if (IsOpenAiProvider(state.Provider)) {
                if (!await EnsureOpenAiAuthB64Async(state).ConfigureAwait(false)) {
                    AnsiConsole.MarkupLine("[red]OpenAI login failed (secret not set).[/]");
                    return 1;
                }

                if (state.SecretTarget == SecretTarget.Org) {
                    if (!await EnsureOrgSecretAsync(state).ConfigureAwait(false)) {
                        return 1;
                    }
                    // Secret is now stored at org scope; per-repo setup should skip secret upload.
                    state.SkipSecret = true;
                }

                if (state.Operation == WizardOperation.UpdateSecret && state.SecretTarget == SecretTarget.Org) {
                    AnsiConsole.MarkupLine("[green]Org secret updated successfully.[/]");
                    return 0;
                }
            } else if (IsClaudeProvider(state.Provider)) {
                if (!EnsureClaudeApiKey(state)) {
                    return 1;
                }
            }
        }

        var failures = 0;
        var verifyFailures = 0;
        var prLinks = new List<(string Repo, string Url)>();
        var verifyResults = new List<SetupPostApplyVerification>();
        var host = new SetupHost();
        GitHubRepoClient? verifyClient = null;
        try {
            if (!state.DryRun && !string.IsNullOrWhiteSpace(state.GitHubToken)) {
                verifyClient = new GitHubRepoClient(state.GitHubToken!, DefaultGitHubApi);
            }

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Applying setup...", async _ => {
                    foreach (var repoPlan in state.SelectedRepos.Select(repo => BuildPlan(state, repo))) {
                        var result = await host.ApplyWithOutputAsync(repoPlan).ConfigureAwait(false);
                        if (result.ExitCode != 0) {
                            failures++;
                        }

                        var prUrl = SetupPostApplyVerifier.ExtractPullRequestUrl(result.Output);
                        if (!string.IsNullOrWhiteSpace(prUrl)) {
                            prLinks.Add((repoPlan.RepoFullName, prUrl!));
                        }

                        var verifyContext = BuildPostApplyVerifyContext(state, repoPlan, result, prUrl);
                        var verifyResult = await ResolvePostApplyVerificationAsync(
                            verifyContext,
                            () => SetupPostApplyVerifier.VerifyAsync(verifyClient, verifyContext)).ConfigureAwait(false);
                        verifyResults.Add(verifyResult);
                    }
                }).ConfigureAwait(false);
        } finally {
            verifyClient?.Dispose();
        }

        if (failures > 0) {
            if (verifyResults.Count > 0) {
                RenderPostApplyVerificationSummary(verifyResults);
            }
            AnsiConsole.MarkupLine($"[red]Setup completed with {failures} failure(s).[/]");
            return 1;
        }

        if (prLinks.Count > 0) {
            RenderPullRequestSummary(prLinks);
        }
        if (verifyResults.Count > 0) {
            RenderPostApplyVerificationSummary(verifyResults);
            verifyFailures = verifyResults.Count(verify => !verify.Skipped && !verify.Passed);
            if (verifyFailures > 0) {
                AnsiConsole.MarkupLine($"[red]Post-apply verification detected {verifyFailures} issue(s).[/]");
                return 1;
            }
        }

        AnsiConsole.MarkupLine("[green]Setup completed successfully.[/]");
        await TryShowUsageAsync(state).ConfigureAwait(false);
        return 0;
    }

    private static void RenderAutoDetectSummary(SetupOnboardingAutoDetectResult result) {
        var path = SetupOnboardingPaths.GetOrDefault(result.RecommendedPath);
        var checks = result.Checks ?? Array.Empty<SetupOnboardingCheck>();
        var recommendedReason = NormalizeAutoDetectRecommendedReason(result.RecommendedReason);
        var status = result.Status.ToLowerInvariant() switch {
            "fail" => "[red]fail[/]",
            "warn" => "[yellow]warn[/]",
            _ => "[green]ok[/]"
        };

        var lines = new List<string> {
            $"Status: {status}",
            $"Recommended path: [cyan]{Markup.Escape(path.DisplayName)}[/]",
            $"Reason: {Markup.Escape(recommendedReason)}"
        };
        if (string.Equals(result.Status, "fail", StringComparison.OrdinalIgnoreCase)) {
            lines.Add("Run `intelligencex setup autodetect --json` for full preflight diagnostics.");
        }

        if (checks.Count > 0) {
            lines.Add(string.Empty);
            lines.Add("Top checks:");
            foreach (var check in checks.Take(3)) {
                var checkStatus = check.Status switch {
                    SetupOnboardingCheckStatus.Fail => "[red]FAIL[/]",
                    SetupOnboardingCheckStatus.Warn => "[yellow]WARN[/]",
                    _ => "[green]OK[/]"
                };
                lines.Add($"- {checkStatus} {Markup.Escape(check.Message)}");
            }
            if (checks.Count > 3) {
                lines.Add($"- [grey]... +{checks.Count - 3} more[/]");
            }
        }

        var panel = new Panel(string.Join(Environment.NewLine, lines)) {
            Header = new PanelHeader("[blue]Auto-Detect (Doctor Preflight)[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    internal static string NormalizeAutoDetectRecommendedReasonForTests(string? recommendedReason) {
        return NormalizeAutoDetectRecommendedReason(recommendedReason);
    }

    internal static string FormatAutoDetectUnavailableMessageForTests(Exception? exception, bool verbose) {
        return FormatAutoDetectUnavailableMessage(exception, verbose);
    }

    internal static (string RecommendedPathId, string RecommendedReason) ResolveAutoDetectPromptRecommendationForTests(
        SetupOnboardingAutoDetectResult? autoDetect) {
        return ResolveAutoDetectPromptRecommendation(autoDetect);
    }

    private static string NormalizeAutoDetectRecommendedReason(string? recommendedReason) {
        return string.IsNullOrWhiteSpace(recommendedReason)
            ? "No recommendation details provided."
            : recommendedReason.Trim();
    }

    private static string FormatAutoDetectUnavailableMessage(Exception? exception, bool verbose) {
        if (exception is null) {
            return "Auto-detect unavailable. Continuing with manual path selection.";
        }

        if (verbose) {
            return $"Auto-detect unavailable ({exception.GetType().Name}): {exception}";
        }

        return $"Auto-detect unavailable: {exception.Message}. Re-run with --verbose for full exception details.";
    }

    private static (string RecommendedPathId, string RecommendedReason) ResolveAutoDetectPromptRecommendation(
        SetupOnboardingAutoDetectResult? autoDetect) {
        if (autoDetect is null) {
            return (
                SetupOnboardingPaths.NewSetup,
                "Auto-detect unavailable. Choose onboarding path manually.");
        }

        return (
            autoDetect.RecommendedPath,
            NormalizeAutoDetectRecommendedReason(autoDetect.RecommendedReason));
    }
}
