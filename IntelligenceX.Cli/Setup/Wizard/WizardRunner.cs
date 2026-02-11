using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Cli.Setup;
using IntelligenceX.Cli.Setup.Host;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static partial class WizardRunner {
    private const string DefaultGitHubApi = "https://api.github.com";
    private const string DefaultGitHubAuth = "https://github.com";

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
            state.ExplicitSecrets = false;
            state.Upgrade = false;
            state.Force = false;
        }
        if (state.Operation == WizardOperation.Cleanup) {
            state.KeepSecret = WizardPrompts.PromptKeepSecret(state.KeepSecret);
        }
        state.DryRun = WizardPrompts.PromptDryRun(state.DryRun);
        state.BranchName = WizardPrompts.PromptBranchName(state.BranchName);

        if (IsOpenAiProvider(state.Provider) && !state.SkipSecret && !state.ManualSecret && state.Operation != WizardOperation.Cleanup) {
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

        if (IsOpenAiProvider(state.Provider) && !state.SkipSecret && !state.ManualSecret && state.Operation != WizardOperation.Cleanup) {
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
}
