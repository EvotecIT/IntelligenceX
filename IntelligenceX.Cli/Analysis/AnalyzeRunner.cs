using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Analysis;
using IntelligenceX.Json;

namespace IntelligenceX.Cli.Analysis;

internal static class AnalyzeRunner {
    public static Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(1);
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "run" => AnalyzeRunCommand.RunAsync(rest),
            "export-config" => ExportConfigAsync(rest),
            "list-packs" => ListPacksAsync(rest),
            "list-rules" => ListRulesAsync(rest),
            "hotspots" => AnalyzeHotspotsCommand.RunAsync(rest),
            "validate-catalog" => ValidateCatalogAsync(rest),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    private static Task<int> ExportConfigAsync(string[] args) {
        var (configPath, outputDir, workspace) = ParseExportArgs(args);
        if (string.IsNullOrWhiteSpace(outputDir)) {
            Console.WriteLine("Missing --out <path>.");
            return Task.FromResult(1);
        }

        var resolvedWorkspace = ResolveWorkspace(workspace);
        var resolvedConfig = ResolveConfigPath(configPath, resolvedWorkspace);
        if (string.IsNullOrWhiteSpace(resolvedConfig) || !File.Exists(resolvedConfig)) {
            Console.WriteLine($"Config not found: {resolvedConfig ?? "<null>"}");
            return Task.FromResult(1);
        }

        JsonObject? root;
        try {
            root = JsonLite.Parse(File.ReadAllText(resolvedConfig))?.AsObject();
        } catch (Exception ex) {
            Console.WriteLine($"Failed to parse config: {ex.Message}");
            return Task.FromResult(1);
        }

        if (root is null) {
            Console.WriteLine("Config root must be a JSON object.");
            return Task.FromResult(1);
        }

        var reviewObj = root.GetObject("review") ?? root;
        var settings = new AnalysisSettings();
        AnalysisConfigReader.Apply(root, reviewObj, settings);
        if (!settings.Enabled) {
            Console.WriteLine("Note: analysis.enabled is false in config.");
        }
        if (settings.Packs.Count == 0) {
            Console.WriteLine("Note: no analysis packs configured.");
        }

        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(resolvedWorkspace);
        var result = AnalysisConfigExporter.Export(settings, catalog, outputDir);

        Console.WriteLine($"Exported {result.RuleCount} rules to {result.OutputDirectory}.");
        foreach (var file in result.Files) {
            Console.WriteLine($"- {file}");
        }
        foreach (var warning in result.Warnings) {
            Console.WriteLine($"Warning: {warning}");
        }
        return Task.FromResult(0);
    }

    private static Task<int> ListPacksAsync(string[] args) {
        var workspace = ParseWorkspace(args);
        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(ResolveWorkspace(workspace));
        if (catalog.Packs.Count == 0) {
            Console.WriteLine("No packs found.");
            return Task.FromResult(0);
        }
        foreach (var pack in catalog.Packs.Values.OrderBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)) {
            var desc = string.IsNullOrWhiteSpace(pack.Description) ? string.Empty : $" - {pack.Description}";
            Console.WriteLine($"{pack.Id}: {pack.Label}{desc}");
        }
        return Task.FromResult(0);
    }

    private static Task<int> ListRulesAsync(string[] args) {
        var options = ParseListRulesArgs(args);
        if (options.ShowHelp) {
            PrintListRulesHelp();
            return Task.FromResult(0);
        }
        if (options.Error is not null) {
            Console.WriteLine(options.Error);
            return Task.FromResult(1);
        }

        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(ResolveWorkspace(options.Workspace));
        var (rules, warnings) = ResolveListRules(catalog, options.Packs);
        var jsonOutput = options.Format.Equals("json", StringComparison.OrdinalIgnoreCase);
        if (rules.Count == 0) {
            if (jsonOutput) {
                PrintRulesJson(Array.Empty<AnalysisRule>());
            } else {
                Console.WriteLine("No rules found.");
            }
            WriteListRulesWarnings(warnings, jsonOutput);
            return Task.FromResult(0);
        }

        switch (options.Format) {
            case "text":
                foreach (var rule in rules) {
                    Console.WriteLine($"{rule.Id} [{rule.Language}] {rule.Title}");
                }
                break;
            case "markdown":
                PrintRulesMarkdown(rules);
                break;
            case "json":
                PrintRulesJson(rules);
                break;
            default:
                Console.WriteLine($"Unsupported format '{options.Format}'. Use text, markdown, or json.");
                return Task.FromResult(1);
        }

        WriteListRulesWarnings(warnings, jsonOutput);
        return Task.FromResult(0);
    }

    private static Task<int> ValidateCatalogAsync(string[] args) {
        var workspace = ResolveWorkspace(ParseWorkspace(args));
        var validation = AnalysisCatalogValidator.ValidateWorkspace(workspace);
        Console.WriteLine(validation.BuildSummary());
        foreach (var warning in validation.Warnings) {
            Console.WriteLine($"Warning: {warning}");
        }
        foreach (var error in validation.Errors) {
            Console.WriteLine($"Error: {error}");
        }
        return Task.FromResult(validation.IsValid ? 0 : 1);
    }

    private static (string? configPath, string? outputDir, string? workspace) ParseExportArgs(string[] args) {
        string? config = null;
        string? output = null;
        string? workspace = null;
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (arg.Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                config = args[++i];
                continue;
            }
            if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                output = args[++i];
                continue;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                workspace = args[++i];
            }
        }
        return (config, output, workspace);
    }

    private static string? ParseWorkspace(string[] args) {
        for (var i = 0; i < args.Length; i++) {
            if (args[i].Equals("--workspace", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length) {
                return args[i + 1];
            }
        }
        return null;
    }

    private static ListRulesOptions ParseListRulesArgs(string[] args) {
        var options = new ListRulesOptions();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (IsHelp(arg)) {
                options.ShowHelp = true;
                return options;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --workspace.";
                    return options;
                }
                options.Workspace = args[++i];
                continue;
            }
            if (arg.Equals("--format", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --format.";
                    return options;
                }
                options.Format = (args[++i] ?? string.Empty).Trim().ToLowerInvariant();
                continue;
            }
            if (arg.Equals("--pack", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --pack.";
                    return options;
                }
                AddPackValues(options.Packs, args[++i]);
                continue;
            }
            if (arg.Equals("--packs", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --packs.";
                    return options;
                }
                AddPackValues(options.Packs, args[++i]);
                continue;
            }
            options.Error = $"Unknown option '{arg}' for list-rules.";
            return options;
        }

        if (string.IsNullOrWhiteSpace(options.Format)) {
            options.Format = "text";
        }
        if (!options.Format.Equals("text", StringComparison.OrdinalIgnoreCase) &&
            !options.Format.Equals("markdown", StringComparison.OrdinalIgnoreCase) &&
            !options.Format.Equals("json", StringComparison.OrdinalIgnoreCase)) {
            options.Error = $"Unsupported format '{options.Format}'. Use text, markdown, or json.";
        }
        return options;
    }

    private static void WriteListRulesWarnings(IReadOnlyList<string> warnings, bool jsonOutput) {
        if (warnings is null || warnings.Count == 0) {
            return;
        }
        var writer = jsonOutput ? Console.Error : Console.Out;
        foreach (var warning in warnings) {
            writer.WriteLine($"Warning: {warning}");
        }
    }

    private static void AddPackValues(ICollection<string> packs, string? raw) {
        if (packs is null || string.IsNullOrWhiteSpace(raw)) {
            return;
        }
        var values = raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var value in values) {
            var normalized = value.Trim();
            if (!string.IsNullOrWhiteSpace(normalized)) {
                packs.Add(normalized);
            }
        }
    }

    private static (IReadOnlyList<AnalysisRule> Rules, IReadOnlyList<string> Warnings) ResolveListRules(
        AnalysisCatalog catalog, IReadOnlyList<string> packIds) {
        if (catalog is null) {
            return (Array.Empty<AnalysisRule>(), Array.Empty<string>());
        }
        if (packIds is null || packIds.Count == 0) {
            return (catalog.Rules.Values
                .OrderBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                .ToList(), Array.Empty<string>());
        }

        var settings = new AnalysisSettings {
            Packs = packIds
        };
        var policy = AnalysisPolicyBuilder.Build(settings, catalog);
        var rules = policy.Rules.Values
            .Select(item => item.Rule)
            .Where(rule => rule is not null)
            .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return (rules, policy.Warnings);
    }

    private static void PrintRulesMarkdown(IReadOnlyList<AnalysisRule> rules) {
        Console.WriteLine("| ID | Language | Type | Tool | Tool Rule ID | Default Severity | Category | Title | Docs |");
        Console.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var rule in rules) {
            var docs = string.IsNullOrWhiteSpace(rule.Docs)
                ? string.Empty
                : $"[link]({rule.Docs})";
            Console.WriteLine(
                $"| {EscapeMarkdown(rule.Id)} | {EscapeMarkdown(rule.Language)} | {EscapeMarkdown(rule.Type)} | {EscapeMarkdown(rule.Tool)} | {EscapeMarkdown(rule.ToolRuleId)} | {EscapeMarkdown(rule.DefaultSeverity)} | {EscapeMarkdown(rule.Category)} | {EscapeMarkdown(rule.Title)} | {EscapeMarkdown(docs)} |");
        }
    }

    private static void PrintRulesJson(IReadOnlyList<AnalysisRule> rules) {
        var items = rules.Select(rule => new Dictionary<string, object?> {
            ["id"] = rule.Id,
            ["language"] = rule.Language,
            ["type"] = rule.Type,
            ["tool"] = rule.Tool,
            ["toolRuleId"] = rule.ToolRuleId,
            ["title"] = rule.Title,
            ["description"] = rule.Description,
            ["category"] = rule.Category,
            ["defaultSeverity"] = rule.DefaultSeverity,
            ["tags"] = rule.Tags,
            ["docs"] = rule.Docs
        }).ToList();
        Console.WriteLine(JsonLite.Serialize(items));
    }

    private static string EscapeMarkdown(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }
        var text = value.Replace("|", "\\|")
            .Replace("\r", " ")
            .Replace("\n", " ");
        return text;
    }

    internal static string ResolveWorkspace(string? workspace) {
        if (!string.IsNullOrWhiteSpace(workspace)) {
            return Path.GetFullPath(workspace);
        }
        var env = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(env)) {
            return env;
        }
        return Environment.CurrentDirectory;
    }

    internal static string? ResolveConfigPath(string? explicitPath, string workspace) {
        if (!string.IsNullOrWhiteSpace(explicitPath)) {
            return Path.IsPathRooted(explicitPath)
                ? explicitPath
                : Path.Combine(workspace, explicitPath);
        }
        var env = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        if (!string.IsNullOrWhiteSpace(env)) {
            return env;
        }
        return Path.Combine(workspace, ".intelligencex", "reviewer.json");
    }

    private static void PrintHelp() {
        Console.WriteLine("Analyze commands:");
        AnalyzeRunCommand.PrintHelp();
        Console.WriteLine("  intelligencex analyze export-config --out <dir> [--config <path>] [--workspace <path>]");
        Console.WriteLine("  intelligencex analyze list-packs [--workspace <path>]");
        Console.WriteLine("  intelligencex analyze list-rules [--workspace <path>] [--format text|markdown|json] [--pack <id>] [--packs <id1,id2>]");
        Console.WriteLine("  intelligencex analyze hotspots <command> [options]");
        Console.WriteLine("  intelligencex analyze validate-catalog [--workspace <path>]");
    }

    private static void PrintListRulesHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex analyze list-rules [--workspace <path>] [--format text|markdown|json] [--pack <id>] [--packs <id1,id2>]");
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ListRulesOptions {
        public string? Workspace { get; set; }
        public string Format { get; set; } = "text";
        public List<string> Packs { get; } = new List<string>();
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
    }
}
