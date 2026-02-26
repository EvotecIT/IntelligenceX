using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Cli.Setup;
using IntelligenceX.Cli.Setup.Onboarding;
using Spectre.Console;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class WizardPrompts {
    internal const string DisableAnalysisSelection = "__disable_analysis__";

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
                .AddChoices(
                    GitHubAuthMode.DefaultDeviceFlow,
                    GitHubAuthMode.AppInstallation,
                    GitHubAuthMode.PersonalAccessToken,
                    GitHubAuthMode.CustomDeviceFlow)
                .UseConverter(mode => mode switch {
                    GitHubAuthMode.DefaultDeviceFlow => "Sign in with GitHub (recommended)",
                    GitHubAuthMode.AppInstallation => "Create your own GitHub App (for bot identity)",
                    GitHubAuthMode.PersonalAccessToken => "Personal access token",
                    GitHubAuthMode.CustomDeviceFlow => "Advanced: Custom OAuth Client ID",
                    _ => "Personal access token"
                }));
    }

    public static void ShowTrustInfo() {
        var panel = new Panel(
            "[grey]We use the IntelligenceX GitHub App to simplify setup:[/]\n" +
            "[green]✓[/] Sign in without creating tokens manually\n" +
            "[green]✓[/] Pre-fill settings when creating your own GitHub App\n\n" +
            "[yellow]Dependabot note:[/] Dependabot PR workflows usually cannot access repo secrets.\n" +
            "For those runs, review comments may appear as [grey]github-actions[/] instead of your app bot.\n\n" +
            "[grey]We don't store your credentials or access repos without\n" +
            "your approval. Everything runs locally.[/]\n\n" +
            "[dim]Override with: INTELLIGENCEX_GITHUB_CLIENT_ID env var[/]") {
            Header = new PanelHeader("[blue]About IntelligenceX GitHub App[/]"),
            Border = BoxBorder.Rounded
        };
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
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

    public static string PromptOnboardingPath(string? recommendedPathId, string? recommendedReason) {
        var paths = SetupOnboardingPaths.GetAll();
        var recommended = SetupOnboardingPaths.GetOrDefault(recommendedPathId);
        var recommendedId = recommended?.Id ?? string.Empty;
        var recommendedDisplayName = recommended?.DisplayName ?? "New Setup";
        if (!string.IsNullOrWhiteSpace(recommendedReason)) {
            AnsiConsole.MarkupLine($"[grey]Recommendation: {recommendedDisplayName}. {recommendedReason}[/]");
            AnsiConsole.WriteLine();
        }

        var orderedPaths = paths
            .OrderBy(path => string.Equals(path.Id, recommendedId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(path => path.DisplayName, StringComparer.Ordinal)
            .ToArray();
        var prompt = new SelectionPrompt<SetupOnboardingPathDefinition>()
            .Title("Choose onboarding path:")
            .PageSize(8)
            .AddChoices(orderedPaths)
            .UseConverter(path => {
                var recommendedSuffix = string.Equals(path.Id, recommendedId, StringComparison.OrdinalIgnoreCase)
                    ? " (recommended)"
                    : string.Empty;
                return $"{path.DisplayName}{recommendedSuffix} - {path.Description}";
            });
        var selected = AnsiConsole.Prompt(prompt);
        return selected.Id;
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
        return AnsiConsole.Confirm("Also create .intelligencex/reviewer.json?", current);
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

    public static bool PromptAnalysisEnabled(bool current) {
        return AnsiConsole.Confirm("Enable static analysis (recommended)?", current);
    }

    public static string? PromptAnalysisPacks(string? current) {
        while (true) {
            var choices = new List<string> {
                "(default: all-50)",
                "all-100",
                "all-500",
                "none (disable static analysis)",
                "all-security-default",
                "powershell-50",
                "javascript-50",
                "python-50",
                "list available packs",
                "custom (enter pack ids)"
            };
            if (!string.IsNullOrWhiteSpace(current) &&
                !choices.Contains(current, StringComparer.Ordinal) &&
                !string.Equals(current, "all-50", StringComparison.Ordinal)) {
                choices.Insert(1, $"(keep current: {current})");
            }
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Static analysis pack(s):")
                    .PageSize(10)
                    .AddChoices(choices));
            if (string.Equals(selected, "(default: all-50)", StringComparison.Ordinal)) {
                return null;
            }
            if (string.Equals(selected, "none (disable static analysis)", StringComparison.Ordinal)) {
                return DisableAnalysisSelection;
            }
            if (selected.StartsWith("(keep current:", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(current)) {
                return current;
            }
            if (string.Equals(selected, "list available packs", StringComparison.Ordinal)) {
                AnsiConsole.MarkupLine("[grey]Browse available packs:[/]");
                AnsiConsole.MarkupLine("[grey]  intelligencex analyze list-rules --workspace . --format markdown[/]");
                AnsiConsole.MarkupLine("[grey]  intelligencex analyze list-rules --workspace . --pack all-50[/]");
                AnsiConsole.MarkupLine("[grey]Docs: Docs/reviewer/static-analysis.md[/]");
                continue;
            }
            if (!string.Equals(selected, "custom (enter pack ids)", StringComparison.Ordinal)) {
                return selected;
            }

            var currentHint = string.IsNullOrWhiteSpace(current) ? string.Empty : $" (current: {current})";
            var prompt = new TextPrompt<string>($"Pack ids (comma-separated, empty for default){currentHint}:")
                .AllowEmpty();
            var raw = AnsiConsole.Prompt(prompt);
            if (string.IsNullOrWhiteSpace(raw)) {
                return null;
            }

            if (SetupAnalysisPacks.TryNormalizeCsv(raw, out var normalized, out var error)) {
                return normalized;
            }
            AnsiConsole.MarkupLine($"[red]{error}[/]");
        }
    }

    public static bool PromptAnalysisGateEnabled(bool current) {
        AnsiConsole.MarkupLine("[grey]Gate semantics: fail when enabled rules report vulnerability/bug findings at warning+ severity. Docs: Docs/reviewer/static-analysis.md[/]");
        return AnsiConsole.Confirm("Fail CI on static analysis findings?", current);
    }

    public static bool PromptAnalysisRunStrict(bool current) {
        AnsiConsole.MarkupLine("[grey]Runner strict semantics: fail when analyzer runner/tool execution errors occur. Docs: Docs/reviewer/static-analysis.md[/]");
        return AnsiConsole.Confirm("Fail CI on analyzer runner/tool errors?", current);
    }

    public static string? PromptAnalysisExportPath(string? current) {
        while (true) {
            var currentHint = string.IsNullOrWhiteSpace(current) ? string.Empty : $" (current: {current})";
            var raw = AnsiConsole.Prompt(
                new TextPrompt<string>($"Optional export path for IDE analyzer configs{currentHint} (empty to skip):")
                    .AllowEmpty());
            if (string.IsNullOrWhiteSpace(raw)) {
                return null;
            }
            if (SetupAnalysisExportPath.TryNormalize(raw, out var normalized, out var error)) {
                return normalized;
            }
            AnsiConsole.MarkupLine($"[red]{error}[/]");
        }
    }

    // Pack/export-path validation is shared with web onboarding helpers.

    public static SecretTarget PromptSecretTarget(int selectedRepoCount, bool ownersMatch) {
        if (selectedRepoCount <= 1 || !ownersMatch) {
            return SecretTarget.Repo;
        }
        return AnsiConsole.Prompt(
            new SelectionPrompt<SecretTarget>()
                .Title("Store OpenAI secret as:")
                .AddChoices(SecretTarget.Org, SecretTarget.Repo)
                .UseConverter(target => target switch {
                    SecretTarget.Org => "Organization secret (recommended for multi-repo)",
                    _ => "Per-repository secrets"
                }));
    }

    public static string PromptOrg(string? suggested) {
        var prompt = new TextPrompt<string>("Organization login (for org secret):")
            .Validate(value => !string.IsNullOrWhiteSpace(value)
                ? ValidationResult.Success()
                : ValidationResult.Error("Organization login is required."));
        if (!string.IsNullOrWhiteSpace(suggested)) {
            prompt.DefaultValue(suggested);
        }
        return AnsiConsole.Prompt(prompt);
    }

    public static string PromptOrgSecretVisibility() {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Org secret visibility:")
                .AddChoices("selected", "private", "all")
                .UseConverter(value => value switch {
                    "selected" => "Selected repositories only (recommended)",
                    "private" => "All private repositories",
                    _ => "All repositories"
                }));
    }

    public static bool PromptDeleteRepoSecrets(bool current) {
        return AnsiConsole.Confirm("Delete existing repo secrets after creating org secret? (prevents repo secret overriding org secret)", current);
    }

    public static string PromptProvider(string current) {
        var prompt = new SelectionPrompt<string>()
            .Title("Review provider:")
            .AddChoices("openai", "copilot")
            .UseConverter(value => value switch {
                "openai" => "ChatGPT / OpenAI (recommended)",
                "copilot" => "GitHub Copilot (requires Copilot CLI)",
                _ => value
            });
        var selection = AnsiConsole.Prompt(prompt);
        return string.IsNullOrWhiteSpace(selection) ? current : selection;
    }

    public static string? PromptOpenAiAccountId(string? current) {
        var prompt = new TextPrompt<string>("Primary OpenAI account id (optional):")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(current)) {
            prompt.DefaultValue(current);
        }
        var value = AnsiConsole.Prompt(prompt);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? PromptOpenAiAccountIds(string? current, string? primaryAccountId) {
        var prompt = new TextPrompt<string>("OpenAI account ids (comma-separated, optional):")
            .AllowEmpty();
        if (!string.IsNullOrWhiteSpace(current)) {
            prompt.DefaultValue(current);
        } else if (!string.IsNullOrWhiteSpace(primaryAccountId)) {
            prompt.DefaultValue(primaryAccountId);
        }
        var raw = AnsiConsole.Prompt(prompt);
        if (string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(primaryAccountId)) {
            return null;
        }
        var values = (raw ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!string.IsNullOrWhiteSpace(primaryAccountId) &&
            !values.Any(value => string.Equals(value, primaryAccountId, StringComparison.OrdinalIgnoreCase))) {
            values.Insert(0, primaryAccountId!);
        }
        return values.Count == 0 ? null : string.Join(",", values);
    }

    public static string PromptOpenAiAccountRotation(string current) {
        var normalizedCurrent = string.IsNullOrWhiteSpace(current) ? "first-available" : current.Trim().ToLowerInvariant();
        var choices = new List<string> { "first-available", "round-robin", "sticky" };
        if (choices.Contains(normalizedCurrent, StringComparer.OrdinalIgnoreCase)) {
            choices.RemoveAll(value => string.Equals(value, normalizedCurrent, StringComparison.OrdinalIgnoreCase));
            choices.Insert(0, normalizedCurrent);
        }
        var prompt = new SelectionPrompt<string>()
            .Title("OpenAI account rotation:")
            .AddChoices(choices)
            .UseConverter(value => value switch {
                "first-available" => "first-available (use first account, fallback optional)",
                "round-robin" => "round-robin (rotate by workflow run id)",
                "sticky" => "sticky (prefer primary account id)",
                _ => value
            });
        return AnsiConsole.Prompt(prompt);
    }

    public static bool PromptOpenAiAccountFailover(bool current) {
        return AnsiConsole.Confirm("Enable OpenAI account failover?", current);
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
                .AddChoices(ConfigMode.None, ConfigMode.Preset, ConfigMode.Existing, ConfigMode.CustomJson)
                .UseConverter(mode => mode switch {
                    ConfigMode.None => "Workflow only (no config)",
                    ConfigMode.Preset => "Preset (recommended)",
                    ConfigMode.Existing => "Load existing config from repo",
                    _ => "Custom JSON"
                }));
    }

    public static string PromptRepoForInspection(IReadOnlyList<string> repos, string title) {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .PageSize(12)
                .AddChoices(repos));
    }

    public static bool PromptViewWorkflowPreview() {
        return AnsiConsole.Confirm("View existing workflow preview?", false);
    }

    public static bool PromptEditLoadedConfig() {
        return AnsiConsole.Confirm("Edit loaded config in editor?", false);
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

    public static bool PromptCheckUsage() {
        return AnsiConsole.Confirm("Check ChatGPT usage/credits now?", true);
    }

    public static string? PromptAuthBundlePath() {
        var path = AnsiConsole.Prompt(
            new TextPrompt<string>("Auth bundle path (leave blank to skip):")
                .AllowEmpty());
        return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }

    public static bool PromptIncludeUsageEvents() {
        return AnsiConsole.Confirm("Include credit usage events?", false);
    }
}
