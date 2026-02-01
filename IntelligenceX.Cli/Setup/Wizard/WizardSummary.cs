using Spectre.Console;
using IntelligenceX.Cli.Setup.Host;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class WizardSummary {
    public static void Render(SetupPlan plan, IReadOnlyList<string> repos, string? workflowStatus = null, string? configSource = null,
        string? authLabel = null, string? usageLabel = null) {
        var table = new Table()
            .RoundedBorder()
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Repos", FormatRepoList(repos));
        table.AddRow("Operation", DescribeOperation(plan));
        table.AddRow("With config", plan.WithConfig ? "yes" : "no");
        table.AddRow("Config detail", DescribeConfig(plan));
        if (!string.IsNullOrWhiteSpace(configSource)) {
            table.AddRow("Config source", configSource);
        }
        if (!string.IsNullOrWhiteSpace(workflowStatus)) {
            table.AddRow("Workflow status", workflowStatus);
        }
        if (!string.IsNullOrWhiteSpace(plan.Provider)) {
            table.AddRow("Provider", plan.Provider);
        }
        table.AddRow("Skip secret", plan.SkipSecret ? "yes" : "no");
        table.AddRow("Manual secret", plan.ManualSecret ? "yes" : "no");
        table.AddRow("Explicit secrets", plan.ExplicitSecrets ? "yes" : "no");
        if (plan.Cleanup) {
            table.AddRow("Keep secret", plan.KeepSecret ? "yes" : "no");
        }
        table.AddRow("Upgrade", plan.Upgrade ? "yes" : "no");
        table.AddRow("Force", plan.Force ? "yes" : "no");
        table.AddRow("Dry run", plan.DryRun ? "yes" : "no");
        table.AddRow("Branch", string.IsNullOrWhiteSpace(plan.BranchName) ? "(auto)" : plan.BranchName);
        table.AddRow("GitHub auth", string.IsNullOrWhiteSpace(authLabel) ? "token" : authLabel);
        if (!string.IsNullOrWhiteSpace(usageLabel)) {
            table.AddRow("Usage (cached)", usageLabel!);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
    }

    private static string FormatRepoList(IReadOnlyList<string> repos) {
        if (repos.Count == 0) {
            return "(none)";
        }
        if (repos.Count == 1) {
            return repos[0];
        }
        var preview = repos.Take(3).ToList();
        var remaining = repos.Count - preview.Count;
        var summary = string.Join(", ", preview);
        return remaining > 0 ? $"{summary} (+{remaining} more)" : summary;
    }

    private static string DescribeConfig(SetupPlan plan) {
        if (!plan.WithConfig) {
            return "none";
        }
        if (!string.IsNullOrWhiteSpace(plan.ConfigPath)) {
            return $"custom ({plan.ConfigPath})";
        }
        if (!string.IsNullOrWhiteSpace(plan.ConfigJson)) {
            return "custom (inline)";
        }
        if (!string.IsNullOrWhiteSpace(plan.ReviewProfile)) {
            return $"preset ({plan.ReviewProfile})";
        }
        return "default";
    }

    private static string DescribeOperation(SetupPlan plan) {
        if (plan.Cleanup) {
            return "cleanup";
        }
        if (plan.UpdateSecret) {
            return "update secret";
        }
        return "setup";
    }
}
