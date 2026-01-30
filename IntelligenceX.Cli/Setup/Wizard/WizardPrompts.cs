using System;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class WizardPrompts {
    public static string PromptRepo(string? current) {
        var prompt = new TextPrompt<string>("Repository (owner/name):")
            .Validate(value => value.Contains('/')
                ? ValidationResult.Success()
                : ValidationResult.Error("Enter repository as owner/name."));
        if (!string.IsNullOrWhiteSpace(current)) {
            prompt.DefaultValue(current);
        }
        return AnsiConsole.Prompt(prompt);
    }

    public static GitHubAuthMode PromptAuthMode() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<GitHubAuthMode>()
                .Title("GitHub authentication:")
                .AddChoices(GitHubAuthMode.AppInstallation, GitHubAuthMode.DeviceFlow, GitHubAuthMode.PersonalAccessToken)
                .UseConverter(mode => mode switch {
                    GitHubAuthMode.AppInstallation => "GitHub App (installation token)",
                    GitHubAuthMode.DeviceFlow => "OAuth device flow (recommended)",
                    _ => "Personal access token"
                }));
    }

    public static SetupScope PromptScope() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<SetupScope>()
                .Title("Setup scope:")
                .AddChoices(SetupScope.SingleRepo, SetupScope.MultipleRepos, SetupScope.Manual)
                .UseConverter(scope => scope switch {
                    SetupScope.SingleRepo => "Single repository",
                    SetupScope.MultipleRepos => "Multiple repositories",
                    _ => "Manual entry"
                }));
    }

    public static WizardOperation PromptOperation() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<WizardOperation>()
                .Title("Operation:")
                .AddChoices(WizardOperation.Setup, WizardOperation.UpdateSecret, WizardOperation.Cleanup)
                .UseConverter(op => op switch {
                    WizardOperation.Setup => "Setup / update workflow + config",
                    WizardOperation.UpdateSecret => "Update OpenAI auth secret only",
                    _ => "Cleanup (remove workflow/config)"
                }));
    }

    public static string? PromptFilter() {
        var filter = AnsiConsole.Prompt(
            new TextPrompt<string>("Filter repositories (optional):")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(filter) ? null : filter;
    }

    public static string PromptSingleRepo(IReadOnlyList<string> repos) {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select repository:")
                .PageSize(12)
                .AddChoices(repos));
    }

    public static List<string> PromptMultipleRepos(IReadOnlyList<string> repos) {
        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select repositories:")
            .PageSize(12)
            .NotRequired()
            .InstructionsText("[grey](Space to toggle, Enter to accept)[/]")
            .AddChoices(repos);
        return AnsiConsole.Prompt(prompt).ToList();
    }

    public static List<string> PromptManualRepos() {
        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("Repositories (comma-separated owner/name):")
                .AllowEmpty());
        if (string.IsNullOrWhiteSpace(input)) {
            return new List<string>();
        }
        return input.Split(',')
            .Select(repo => repo.Trim())
            .Where(repo => !string.IsNullOrWhiteSpace(repo))
            .ToList();
    }

    public static string? PromptGitHubToken() {
        var token = AnsiConsole.Prompt(
            new TextPrompt<string>("GitHub token:")
                .Secret()
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    public static string? PromptGitHubClientId(string? fallback) {
        var prompt = new TextPrompt<string>("GitHub OAuth client id (optional):")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(fallback)) {
            prompt.DefaultValue(fallback);
        }
        var clientId = AnsiConsole.Prompt(prompt);
        return string.IsNullOrWhiteSpace(clientId) ? null : clientId;
    }

    public static long? PromptGitHubAppId() {
        var input = AnsiConsole.Prompt(
            new TextPrompt<string>("GitHub App ID:")
                .AllowEmpty());
        if (string.IsNullOrWhiteSpace(input)) {
            return null;
        }
        return long.TryParse(input, out var id) ? id : null;
    }

    public static string? PromptGitHubAppKeyPath() {
        var path = AnsiConsole.Prompt(
            new TextPrompt<string>("GitHub App private key path (optional):")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static string? PromptGitHubAppKeyPem() {
        var pem = AnsiConsole.Prompt(
            new TextPrompt<string>("Paste GitHub App private key (PEM):")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(pem) ? null : pem;
    }

    public static long? PromptInstallation(IReadOnlyList<GitHubInstallationInfo> installs) {
        if (installs.Count == 0) {
            return null;
        }
        var prompt = new SelectionPrompt<GitHubInstallationInfo>()
            .Title("Select GitHub App installation:")
            .PageSize(12)
            .AddChoices(installs)
            .UseConverter(item => $"{item.AccountLogin} (id {item.Id})");
        var selected = AnsiConsole.Prompt(prompt);
        return selected.Id;
    }

    public static void ShowInstallAppHint(long appId) {
        AnsiConsole.MarkupLine("[yellow]No installations found for this app.[/]");
        AnsiConsole.MarkupLine($"Install it from: https://github.com/apps/{appId}/installations");
        AnsiConsole.MarkupLine("After installing, re-run the wizard.");
    }

    public static bool PromptCreateAppFromManifest() {
        return AnsiConsole.Confirm("Create GitHub App via manifest flow?", true);
    }

    public static string PromptAppName(string defaultName) {
        var prompt = new TextPrompt<string>("GitHub App name:")
            .AllowEmpty();
        prompt.DefaultValue(defaultName);
        return AnsiConsole.Prompt(prompt);
    }

    public static string? PromptAppOwner() {
        var owner = AnsiConsole.Prompt(
            new TextPrompt<string>("App owner (org login, optional):")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(owner) ? null : owner;
    }

    public static GitHubAppProfile? PromptSavedApp(IReadOnlyList<GitHubAppProfile> profiles) {
        if (profiles.Count == 0) {
            return null;
        }
        var prompt = new SelectionPrompt<GitHubAppProfile>()
            .Title("Use saved GitHub App?")
            .PageSize(10)
            .AddChoices(profiles)
            .UseConverter(profile => $"{profile.Name ?? "app"} (id {profile.AppId})");
        return AnsiConsole.Prompt(prompt);
    }

    public static bool PromptSaveApp() {
        return AnsiConsole.Confirm("Save GitHub App info for reuse?", true);
    }

    public static string? PromptAppProfileName(string? suggested) {
        var prompt = new TextPrompt<string>("Profile name (optional):")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(suggested)) {
            prompt.DefaultValue(suggested);
        }
        var name = AnsiConsole.Prompt(prompt);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static bool PromptWithConfig(bool current) {
        return AnsiConsole.Confirm("Also create .intelligencex/config.json?", current);
    }

    public static bool PromptSkipSecret(bool current) {
        return AnsiConsole.Confirm("Skip uploading OpenAI secret now?", current);
    }

    public static bool PromptManualSecret(bool current) {
        return AnsiConsole.Confirm("Manual secret (print value instead of uploading)?", current);
    }

    public static bool PromptKeepSecret(bool current) {
        return AnsiConsole.Confirm("Keep existing OpenAI secret during cleanup?", current);
    }

    public static bool PromptExplicitSecrets(bool current) {
        return AnsiConsole.Confirm("Use explicit secrets block in workflow?", current);
    }

    public static bool PromptForceOverwrite(bool current) {
        return AnsiConsole.Confirm("Force overwrite existing workflow/config?", current);
    }

    public static bool PromptUpgradeManaged(bool current) {
        return AnsiConsole.Confirm("Upgrade managed workflow section if present?", current);
    }

    public static bool PromptDryRun(bool current) {
        return AnsiConsole.Confirm("Dry run (show changes only)?", current);
    }

    public static ConfigMode PromptConfigMode() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<ConfigMode>()
                .Title("Configuration:")
                .AddChoices(ConfigMode.None, ConfigMode.Preset, ConfigMode.CustomJson)
                .UseConverter(mode => mode switch {
                    ConfigMode.None => "Workflow only (no config)",
                    ConfigMode.Preset => "Preset (recommended)",
                    _ => "Custom JSON"
                }));
    }

    public static ConfigSource PromptConfigSource() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<ConfigSource>()
                .Title("Custom config source:")
                .AddChoices(ConfigSource.Editor, ConfigSource.Path, ConfigSource.Paste)
                .UseConverter(source => source switch {
                    ConfigSource.Editor => "Open editor",
                    ConfigSource.Path => "Use file path",
                    _ => "Paste inline"
                }));
    }

    public static ConfigPreset PromptPreset() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<ConfigPreset>()
                .Title("Choose review preset:")
                .AddChoices(ConfigPreset.Balanced, ConfigPreset.Strict, ConfigPreset.Security, ConfigPreset.Minimal, ConfigPreset.Performance, ConfigPreset.Tests)
                .UseConverter(preset => preset switch {
                    ConfigPreset.Balanced => "Balanced — correctness + maintainability",
                    ConfigPreset.Strict => "Strict — more findings + edge cases",
                    ConfigPreset.Security => "Security — auth/privacy focus",
                    ConfigPreset.Minimal => "Minimal — high-level summary only",
                    ConfigPreset.Performance => "Performance — perf + scalability",
                    _ => "Tests — coverage + edge cases"
                }));
    }

    public static string? PromptConfigPath() {
        var path = AnsiConsole.Prompt(
            new TextPrompt<string>("Path to config.json (leave empty to paste JSON):")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    public static string? PromptConfigJson() {
        var json = AnsiConsole.Prompt(
            new TextPrompt<string>("Paste config.json content:")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(json) ? null : json;
    }

    public static string? PromptBranchName(string? current) {
        var prompt = new TextPrompt<string>("Branch name (optional):")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(current)) {
            prompt.DefaultValue(current);
        }
        var name = AnsiConsole.Prompt(prompt);
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static bool PromptConfirmApply() {
        return AnsiConsole.Confirm("Apply these changes?", true);
    }
}
