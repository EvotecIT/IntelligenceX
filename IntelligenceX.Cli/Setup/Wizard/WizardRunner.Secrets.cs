using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.Auth;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static partial class WizardRunner {
    private static bool IsOpenAiProvider(string provider) {
        return string.Equals(provider, "openai", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, "chatgpt", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPassAuthB64(WizardState state) {
        if (!IsOpenAiProvider(state.Provider)) {
            return false;
        }
        if (state.SkipSecret || state.ManualSecret) {
            return false;
        }
        if (state.SecretTarget == SecretTarget.Org) {
            return false;
        }
        return !string.IsNullOrWhiteSpace(state.OpenAiAuthB64);
    }

    private static string DescribeSecretTarget(WizardState state) {
        if (!IsOpenAiProvider(state.Provider) || state.SkipSecret || state.ManualSecret || state.Operation == WizardOperation.Cleanup) {
            return string.Empty;
        }
        if (state.SecretTarget == SecretTarget.Org) {
            var org = string.IsNullOrWhiteSpace(state.SecretOrg) ? "(org)" : state.SecretOrg;
            var vis = string.IsNullOrWhiteSpace(state.SecretVisibility) ? "all" : state.SecretVisibility;
            return $"org ({org}, {vis})";
        }
        return "repo";
    }

    private static bool TryGetCommonOwner(IReadOnlyList<string> repos, out string? owner) {
        owner = null;
        foreach (var repo in repos) {
            var parts = repo.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) {
                return false;
            }
            if (owner is null) {
                owner = parts[0];
                continue;
            }
            if (!string.Equals(owner, parts[0], StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }
        return owner is not null;
    }

    private static async Task<bool> EnsureOpenAiAuthB64Async(WizardState state) {
        if (!string.IsNullOrWhiteSpace(state.OpenAiAuthB64)) {
            return true;
        }
        try {
            var config = OAuthConfig.FromEnvironment();
            config.Validate();
            var service = new OAuthLoginService();
            var loginOptions = new OAuthLoginOptions(config) {
                UseLocalListener = true,
                OnAuthUrl = url => {
                    AnsiConsole.MarkupLine($"Open: [blue]{url}[/]");
                    TryOpenUrl(url);
                    return Task.CompletedTask;
                },
                OnPrompt = prompt => Task.FromResult(AnsiConsole.Prompt(new TextPrompt<string>(prompt).AllowEmpty()))
            };
            var result = await service.LoginAsync(loginOptions).ConfigureAwait(false);
            var json = AuthBundleSerializer.Serialize(result.Bundle);
            state.OpenAiAuthB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
            return true;
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]OpenAI login failed: {ex.Message}[/]");
            return false;
        }
    }

    private static async Task<bool> EnsureOrgSecretAsync(WizardState state) {
        if (string.IsNullOrWhiteSpace(state.GitHubToken)) {
            AnsiConsole.MarkupLine("[red]Missing GitHub token.[/]");
            return false;
        }
        if (string.IsNullOrWhiteSpace(state.SecretOrg)) {
            AnsiConsole.MarkupLine("[red]Missing org login for org secret.[/]");
            return false;
        }
        if (string.IsNullOrWhiteSpace(state.OpenAiAuthB64)) {
            AnsiConsole.MarkupLine("[red]Missing OpenAI auth export.[/]");
            return false;
        }

        if (!TryGetCommonOwner(state.SelectedRepos, out var owner) || string.IsNullOrWhiteSpace(owner)) {
            AnsiConsole.MarkupLine("[red]Org secret requires all selected repositories to be under the same owner.[/]");
            return false;
        }
        if (!string.Equals(owner, state.SecretOrg, StringComparison.OrdinalIgnoreCase)) {
            AnsiConsole.MarkupLine($"[yellow]Warning: selected repos owner is {owner}, but org secret is {state.SecretOrg}. This may fail if {state.SecretOrg} is not the org.[/]");
        }

        try {
            using var secrets = new GitHubSecretsClient(state.GitHubToken!, DefaultGitHubApi);
            IReadOnlyList<long>? repoIds = null;
            var visibility = string.IsNullOrWhiteSpace(state.SecretVisibility) ? "all" : state.SecretVisibility.Trim();
            if (string.Equals(visibility, "selected", StringComparison.OrdinalIgnoreCase)) {
                var ids = new List<long>();
                foreach (var repoFullName in state.SelectedRepos) {
                    var parts = repoFullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) {
                        // Fail fast: selected visibility with invalid repo list is likely to surprise users.
                        throw new InvalidOperationException($"Invalid repo name: {repoFullName}");
                    }
                    ids.Add(await secrets.GetRepoIdAsync(parts[0], parts[1]).ConfigureAwait(false));
                }
                repoIds = ids;
            }

            await secrets.SetOrgSecretAsync(state.SecretOrg!, "INTELLIGENCEX_AUTH_B64", state.OpenAiAuthB64!, visibility, repoIds).ConfigureAwait(false);

            if (state.DeleteRepoSecretsAfterOrgSecret) {
                foreach (var repoFullName in state.SelectedRepos) {
                    var parts = repoFullName.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2) {
                        continue;
                    }
                    try {
                        await secrets.DeleteRepoSecretAsync(parts[0], parts[1], "INTELLIGENCEX_AUTH_B64").ConfigureAwait(false);
                    } catch (Exception ex) {
                        AnsiConsole.MarkupLine($"[yellow]Warning: failed to delete repo secret for {repoFullName}: {ex.Message}[/]");
                    }
                }
            }

            AnsiConsole.MarkupLine($"[green]Org secret updated: {state.SecretOrg}/INTELLIGENCEX_AUTH_B64[/]");
            return true;
        } catch (Exception ex) {
            AnsiConsole.MarkupLine($"[red]Failed to set org secret: {ex.Message}[/]");
            return false;
        }
    }

    private static void TryOpenUrl(string url) {
        try {
            Process.Start(new ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            });
        } catch {
            // Ignore failures.
        }
    }
}

