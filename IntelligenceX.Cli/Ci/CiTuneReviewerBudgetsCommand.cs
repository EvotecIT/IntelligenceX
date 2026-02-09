using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiTuneReviewerBudgetsCommand {
    public static Task<int> RunAsync(string[] args) {
        var options = ParseArgs(args);
        if (options.ShowHelp) {
            PrintHelp();
            return Task.FromResult(0);
        }
        if (options.Error is not null) {
            Console.Error.WriteLine(options.Error);
            return Task.FromResult(1);
        }
        if (string.IsNullOrWhiteSpace(options.ChangedFilesPath)) {
            Console.Error.WriteLine("Missing --changed-files <path>.");
            return Task.FromResult(1);
        }

        if (!File.Exists(options.ChangedFilesPath!)) {
            Console.WriteLine($"No changed-files file found at {options.ChangedFilesPath}; leaving budgets unchanged.");
            return Task.FromResult(0);
        }

        var lines = File.ReadAllLines(options.ChangedFilesPath!)
            .Select(value => (value ?? string.Empty).Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        var changed = lines.Length;
        var catalog = lines.Count(value =>
            value.StartsWith("Analysis/Catalog/rules/", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("Analysis/Catalog/overrides/", StringComparison.OrdinalIgnoreCase));

        var large = changed > options.ChangedThreshold || catalog > options.CatalogThreshold;
        if (!large) {
            Console.WriteLine($"Diff size within default limits (changed={changed}, catalog={catalog}); leaving budgets unchanged.");
            return Task.FromResult(0);
        }

        var envTarget = ResolveEnvTarget(options.OutEnv);
        if (string.IsNullOrWhiteSpace(envTarget)) {
            Console.WriteLine("Detected large diff but no environment file available (GITHUB_ENV not set and --out-env not provided).");
            return Task.FromResult(0);
        }

        try {
            File.AppendAllText(envTarget!, $"INPUT_MAX_FILES={options.MaxFiles}{Environment.NewLine}");
            File.AppendAllText(envTarget!, $"INPUT_MAX_PATCH_CHARS={options.MaxPatchChars}{Environment.NewLine}");
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write budgets to {envTarget}: {ex.Message}");
            return Task.FromResult(1);
        }

        Console.WriteLine($"Detected large diff (changed={changed}, catalog={catalog}); set INPUT_MAX_FILES={options.MaxFiles}, INPUT_MAX_PATCH_CHARS={options.MaxPatchChars}.");
        return Task.FromResult(0);
    }

    private static string? ResolveEnvTarget(string? outEnv) {
        if (!string.IsNullOrWhiteSpace(outEnv)) {
            var dir = Path.GetDirectoryName(outEnv);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }
            return outEnv;
        }
        var env = Environment.GetEnvironmentVariable("GITHUB_ENV");
        return string.IsNullOrWhiteSpace(env) ? null : env;
    }

    private static Options ParseArgs(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (IsHelp(arg)) {
                options.ShowHelp = true;
                return options;
            }
            if (arg.Equals("--changed-files", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --changed-files.";
                    return options;
                }
                options.ChangedFilesPath = args[++i];
                continue;
            }
            if (arg.Equals("--changed-threshold", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --changed-threshold.";
                    return options;
                }
                if (!int.TryParse(args[++i], out var parsed) || parsed < 0) {
                    options.Error = "Invalid --changed-threshold value.";
                    return options;
                }
                options.ChangedThreshold = parsed;
                continue;
            }
            if (arg.Equals("--catalog-threshold", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --catalog-threshold.";
                    return options;
                }
                if (!int.TryParse(args[++i], out var parsed) || parsed < 0) {
                    options.Error = "Invalid --catalog-threshold value.";
                    return options;
                }
                options.CatalogThreshold = parsed;
                continue;
            }
            if (arg.Equals("--max-files", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --max-files.";
                    return options;
                }
                if (!int.TryParse(args[++i], out var parsed) || parsed <= 0) {
                    options.Error = "Invalid --max-files value.";
                    return options;
                }
                options.MaxFiles = parsed;
                continue;
            }
            if (arg.Equals("--max-patch-chars", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --max-patch-chars.";
                    return options;
                }
                if (!int.TryParse(args[++i], out var parsed) || parsed <= 0) {
                    options.Error = "Invalid --max-patch-chars value.";
                    return options;
                }
                options.MaxPatchChars = parsed;
                continue;
            }
            if (arg.Equals("--out-env", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --out-env.";
                    return options;
                }
                options.OutEnv = args[++i];
                continue;
            }
            options.Error = $"Unknown option '{arg}' for tune-reviewer-budgets.";
            return options;
        }
        return options;
    }

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp() {
        Console.WriteLine("Tune reviewer budgets in GitHub Actions based on a changed-files list.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex ci tune-reviewer-budgets --changed-files <path> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --changed-threshold <n>    Default: 30");
        Console.WriteLine("  --catalog-threshold <n>    Default: 10");
        Console.WriteLine("  --max-files <n>            Default: 200");
        Console.WriteLine("  --max-patch-chars <n>      Default: 120000");
        Console.WriteLine("  --out-env <path>           Write KEY=VALUE lines to this env file (defaults to $GITHUB_ENV)");
    }

    private sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? ChangedFilesPath { get; set; }
        public int ChangedThreshold { get; set; } = 30;
        public int CatalogThreshold { get; set; } = 10;
        public int MaxFiles { get; set; } = 200;
        public int MaxPatchChars { get; set; } = 120000;
        public string? OutEnv { get; set; }
    }
}

