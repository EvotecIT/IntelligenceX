using System;
using System.IO;
using System.Linq;
using System.Text;
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

        if (!TryWriteEnvLines(envTarget!, ("INPUT_MAX_FILES", options.MaxFiles.ToString()), ("INPUT_MAX_PATCH_CHARS", options.MaxPatchChars.ToString()),
                out var error)) {
            Console.Error.WriteLine(error);
            return Task.FromResult(1);
        }

        Console.WriteLine($"Detected large diff (changed={changed}, catalog={catalog}); set INPUT_MAX_FILES={options.MaxFiles}, INPUT_MAX_PATCH_CHARS={options.MaxPatchChars}.");
        return Task.FromResult(0);
    }

    private static string? ResolveEnvTarget(string? outEnv) {
        var candidate = !string.IsNullOrWhiteSpace(outEnv)
            ? outEnv
            : Environment.GetEnvironmentVariable("GITHUB_ENV");
        if (string.IsNullOrWhiteSpace(candidate)) {
            return null;
        }
        if (candidate.Contains('\n') || candidate.Contains('\r')) {
            return null;
        }
        try {
            var dir = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }
        } catch {
            return null;
        }
        return candidate;
    }

    private static bool TryWriteEnvLines(string path, (string Key, string Value) first, (string Key, string Value) second, out string error) {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path)) {
            error = "Missing env-file path.";
            return false;
        }

        if (!TryValidateEnvPair(first.Key, first.Value, out error) || !TryValidateEnvPair(second.Key, second.Value, out error)) {
            return false;
        }

        try {
            using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
            stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            WriteGitHubEnvEntry(writer, first.Key, first.Value);
            WriteGitHubEnvEntry(writer, second.Key, second.Value);
            writer.Flush();
            return true;
        } catch (Exception ex) {
            error = $"Failed to write budgets to {path}: {ex.Message}";
            return false;
        }
    }

    private static void WriteGitHubEnvEntry(TextWriter writer, string key, string value) {
        // Use the documented env-file heredoc format to be robust to special characters and multiline values.
        // https://docs.github.com/actions/using-workflows/workflow-commands-for-github-actions#environment-files
        var delimiter = $"IX_{Guid.NewGuid():N}";
        for (var i = 0; i < 5; i++) {
            if (!value.Contains(delimiter, StringComparison.Ordinal)) {
                break;
            }
            delimiter = $"IX_{Guid.NewGuid():N}";
        }
        if (value.Contains(delimiter, StringComparison.Ordinal)) {
            throw new InvalidOperationException("Failed to find a safe GitHub env-file delimiter.");
        }
        writer.Write(key);
        writer.Write("<<");
        writer.WriteLine(delimiter);
        writer.WriteLine(value);
        writer.WriteLine(delimiter);
    }

    private static bool TryValidateEnvPair(string key, string value, out string error) {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(key) || key.Contains('\n') || key.Contains('\r') || key.Contains('=')) {
            error = "Invalid env key.";
            return false;
        }
        if (value is null || value.Contains('\0')) {
            error = "Invalid env value.";
            return false;
        }
        return true;
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
        Console.WriteLine("  --out-env <path>           Write GitHub env-file entries to this file (defaults to $GITHUB_ENV)");
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
