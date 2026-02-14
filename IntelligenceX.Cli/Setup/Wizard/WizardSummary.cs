using Spectre.Console;
using IntelligenceX.Cli.Setup.Host;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using IntelligenceX.Cli.Setup;

namespace IntelligenceX.Cli.Setup.Wizard;

internal static class WizardSummary {
    public static void Render(SetupPlan plan, IReadOnlyList<string> repos, string? workflowStatus = null, string? configSource = null,
        string? authLabel = null, string? usageLabel = null, string? secretLabel = null) {
        var table = new Table()
            .RoundedBorder()
            .AddColumn("Setting")
            .AddColumn("Value");

        table.AddRow("Repos", FormatRepoList(repos));
        table.AddRow("Operation", DescribeOperation(plan));
        table.AddRow("With config", plan.WithConfig ? "yes" : "no");
        table.AddRow("Config detail", DescribeConfig(plan));
        if (plan.WithConfig) {
            var configOverride = !string.IsNullOrWhiteSpace(plan.ConfigJson) || !string.IsNullOrWhiteSpace(plan.ConfigPath);
            var analysis = configOverride
                ? "(ignored: custom config override)"
                : plan.AnalysisEnabled.HasValue
                    ? (plan.AnalysisEnabled.Value ? "enabled" : "disabled")
                    : "(default)";
            table.AddRow("Static analysis", analysis);
            if (!configOverride && plan.AnalysisEnabled == true) {
                table.AddRow("Analysis packs", string.IsNullOrWhiteSpace(plan.AnalysisPacks) ? "(default)" : plan.AnalysisPacks!);
                table.AddRow("Analysis gate", plan.AnalysisGateEnabled == true ? "enabled" : "disabled");
                table.AddRow("Analysis runner strict", plan.AnalysisRunStrict == true ? "enabled" : "disabled");
                table.AddRow("Analysis export path",
                    string.IsNullOrWhiteSpace(plan.AnalysisExportPath) ? "(disabled)" : plan.AnalysisExportPath!);
            }
        }
        if (!string.IsNullOrWhiteSpace(configSource)) {
            table.AddRow("Config source", configSource);
        }
        if (!string.IsNullOrWhiteSpace(workflowStatus)) {
            table.AddRow("Workflow status", workflowStatus);
        }
        if (!string.IsNullOrWhiteSpace(plan.Provider)) {
            table.AddRow("Provider", plan.Provider);
            if ((string.Equals(plan.Provider, "openai", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(plan.Provider, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(plan.Provider, "codex", StringComparison.OrdinalIgnoreCase)) &&
                plan.WithConfig &&
                string.IsNullOrWhiteSpace(plan.ConfigJson) &&
                string.IsNullOrWhiteSpace(plan.ConfigPath)) {
                table.AddRow("OpenAI primary account",
                    string.IsNullOrWhiteSpace(plan.OpenAIAccountId) ? "(auto)" : plan.OpenAIAccountId!);
                table.AddRow("OpenAI account ids",
                    string.IsNullOrWhiteSpace(plan.OpenAIAccountIds) ? "(not set)" : plan.OpenAIAccountIds!);
                table.AddRow("OpenAI account rotation",
                    string.IsNullOrWhiteSpace(plan.OpenAIAccountRotation) ? "(default)" : plan.OpenAIAccountRotation!);
                table.AddRow("OpenAI account failover",
                    plan.OpenAIAccountFailover.HasValue
                        ? (plan.OpenAIAccountFailover.Value ? "enabled" : "disabled")
                        : "(default)");
            }
        }
        table.AddRow("Skip secret", plan.SkipSecret ? "yes" : "no");
        table.AddRow("Manual secret", plan.ManualSecret ? "yes" : "no");
        table.AddRow("Explicit secrets", plan.ExplicitSecrets ? "yes" : "no");
        if (!string.IsNullOrWhiteSpace(secretLabel)) {
            table.AddRow("Secret target", secretLabel);
        }
        if (!plan.SkipSecret && !plan.ManualSecret) {
            // Best-effort hint: in org-secret mode, wizard sets the secret once and passes --skip-secret to per-repo setup.
            // In repo-secret mode, wizard passes --auth-b64 to avoid repeated logins.
            table.AddRow("Secret strategy", "auto");
        }
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

        var effectiveConfig = BuildEffectiveConfigPreview(plan);
        if (!string.IsNullOrWhiteSpace(effectiveConfig)) {
            var panel = new Panel(new Text(effectiveConfig)) {
                Header = new PanelHeader("Effective Reviewer Config"),
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();
        }
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

    private static string? BuildEffectiveConfigPreview(SetupPlan plan) {
        if (plan.Cleanup || plan.UpdateSecret) {
            return null;
        }
        if (!plan.WithConfig) {
            return "Reviewer config will not be written (with-config disabled).";
        }
        if (!string.IsNullOrWhiteSpace(plan.ConfigPath)) {
            return $"Using config file override path:\n{plan.ConfigPath}";
        }
        if (!string.IsNullOrWhiteSpace(plan.ConfigJson)) {
            try {
                var parsed = JsonNode.Parse(plan.ConfigJson!);
                return parsed?.ToJsonString(CliJson.Indented) ?? plan.ConfigJson;
            } catch (JsonException) {
                return plan.ConfigJson;
            }
        }

        try {
            var args = SetupArgsBuilder.FromPlan(plan);
            var generated = SetupRunner.BuildReviewerConfigJson(args);
            if (string.IsNullOrWhiteSpace(generated)) {
                return "Effective config preview is unavailable.";
            }
            return generated;
        } catch (Exception ex) {
            Trace.TraceWarning($"Effective config preview generation failed: {ex.GetType().Name}: {ex.Message}");
            return "Effective config preview is unavailable.";
        }
    }
}
