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
}
