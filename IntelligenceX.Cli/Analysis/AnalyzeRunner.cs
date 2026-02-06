using System;
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
        var workspace = ParseWorkspace(args);
        var catalog = AnalysisCatalogLoader.LoadFromWorkspace(ResolveWorkspace(workspace));
        if (catalog.Rules.Count == 0) {
            Console.WriteLine("No rules found.");
            return Task.FromResult(0);
        }
        foreach (var rule in catalog.Rules.Values.OrderBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)) {
            Console.WriteLine($"{rule.Id} [{rule.Language}] {rule.Title}");
        }
        return Task.FromResult(0);
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
        Console.WriteLine("  intelligencex analyze list-rules [--workspace <path>]");
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
}
