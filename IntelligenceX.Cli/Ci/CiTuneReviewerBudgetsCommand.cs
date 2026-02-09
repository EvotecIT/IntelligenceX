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
        if (options.ChangedFilesPath!.Contains('\n') || options.ChangedFilesPath!.Contains('\r') || options.ChangedFilesPath!.Contains('\0')) {
            Console.Error.WriteLine("Invalid --changed-files path.");
            return Task.FromResult(1);
        }

        var workspaceRoot = ResolveWorkspaceRoot();
        var changedFilesPath = ResolvePathWithinWorkspace(workspaceRoot, options.ChangedFilesPath!);
        if (!CiPathSafety.IsUnderRoot(changedFilesPath, workspaceRoot)) {
            Console.Error.WriteLine($"changed-files path must be within the workspace. changed-files={changedFilesPath} workspace={workspaceRoot}");
            return Task.FromResult(1);
        }
        if (!File.Exists(changedFilesPath)) {
            Console.Error.WriteLine($"No changed-files file found at {changedFilesPath}; leaving budgets unchanged.");
            return Task.FromResult(0);
        }

        var changed = 0;
        var catalog = 0;
        foreach (var raw in File.ReadLines(changedFilesPath)) {
            var value = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value)) {
                continue;
            }
            changed++;
            if (value.StartsWith("Analysis/Catalog/rules/", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Analysis/Catalog/overrides/", StringComparison.OrdinalIgnoreCase)) {
                catalog++;
            }
        }

        var large = changed > options.ChangedThreshold || catalog > options.CatalogThreshold;
        if (!large) {
            Console.Error.WriteLine($"Diff size within default limits (changed={changed}, catalog={catalog}); leaving budgets unchanged.");
            return Task.FromResult(0);
        }

        if (!TryResolveEnvTarget(workspaceRoot, options.OutEnv, out var envTarget, out var envError)) {
            Console.Error.WriteLine(envError);
            return Task.FromResult(1);
        }
        if (string.IsNullOrWhiteSpace(envTarget)) {
            Console.Error.WriteLine("Detected large diff but no environment file available (GITHUB_ENV not set and --out-env not provided).");
            return Task.FromResult(0);
        }

        if (!TryWriteEnvLines(envTarget!, ("INPUT_MAX_FILES", options.MaxFiles.ToString()), ("INPUT_MAX_PATCH_CHARS", options.MaxPatchChars.ToString()),
                out var error)) {
            Console.Error.WriteLine(error);
            return Task.FromResult(1);
        }

        Console.Error.WriteLine($"Detected large diff (changed={changed}, catalog={catalog}); set INPUT_MAX_FILES={options.MaxFiles}, INPUT_MAX_PATCH_CHARS={options.MaxPatchChars}.");
        return Task.FromResult(0);
    }

	    private static bool TryResolveEnvTarget(string workspaceRoot, string? outEnv, out string? envPath, out string error) {
	        envPath = null;
	        error = string.Empty;

	        var defaultEnv = Environment.GetEnvironmentVariable("GITHUB_ENV");
	        var isGitHubActions = string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

	        string? candidate = null;
	        if (!string.IsNullOrWhiteSpace(outEnv)) {
	            candidate = outEnv;
	        } else {
	            candidate = defaultEnv;
	        }
	        if (string.IsNullOrWhiteSpace(candidate)) {
	            // No env-file available (or explicitly provided).
	            return true;
	        }
	        if (candidate.Contains('\n') || candidate.Contains('\r') || candidate.Contains('\0')) {
	            error = "Invalid env-file path.";
	            return false;
        }

        string resolvedCandidate;
        try {
            // Important: $GITHUB_ENV is often an absolute path outside the workspace; do not force it under workspace root.
            resolvedCandidate = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(Path.Combine(workspaceRoot, candidate));
        } catch (Exception ex) {
            error = $"Invalid env-file path: {ex.Message}";
            return false;
        }

        // If the workflow explicitly provides --out-env, treat it as a sharp edge and restrict where it can write.
        // Allow exactly $GITHUB_ENV (even if outside workspace), otherwise require it to be under the workspace root.
	        if (!string.IsNullOrWhiteSpace(outEnv)) {
	            var matchesGitHubEnv = false;
	            if (!string.IsNullOrWhiteSpace(defaultEnv) && !defaultEnv.Contains('\n') && !defaultEnv.Contains('\r') && !defaultEnv.Contains('\0')) {
	                try {
                    var fullDefault = Path.GetFullPath(defaultEnv);
                    matchesGitHubEnv = PathsEqual(resolvedCandidate, fullDefault);
                } catch {
                    matchesGitHubEnv = false;
                }
            }
	            if (!matchesGitHubEnv && !IsUnderRoot(resolvedCandidate, workspaceRoot)) {
	                error = $"Env-file output path must be within the workspace (or equal to $GITHUB_ENV). out-env={resolvedCandidate} workspace={workspaceRoot}";
	                return false;
	            }
	        } else if (!isGitHubActions && !File.Exists(resolvedCandidate)) {
	            // Avoid writing to arbitrary paths during local runs just because GITHUB_ENV is set.
	            // In GitHub Actions the env file exists and should be used.
	            return true;
	        }

        try {
            var dir = Path.GetDirectoryName(resolvedCandidate);
            if (!string.IsNullOrWhiteSpace(dir)) {
                Directory.CreateDirectory(dir);
            }
        } catch (Exception ex) {
            error = $"Failed to prepare env-file output path: {ex.Message}";
            return false;
        }

        envPath = resolvedCandidate;
        return true;
    }

    private static string ResolveWorkspaceRoot() {
        var workspace = Environment.GetEnvironmentVariable("GITHUB_WORKSPACE");
        var candidate = string.IsNullOrWhiteSpace(workspace) ? Environment.CurrentDirectory : workspace!;
        try {
            return Path.GetFullPath(candidate);
        } catch {
            return Path.GetFullPath(Environment.CurrentDirectory);
        }
    }

    private static string ResolvePathWithinWorkspace(string workspaceRoot, string path) {
        if (Path.IsPathRooted(path)) {
            return Path.GetFullPath(path);
        }
        return Path.GetFullPath(Path.Combine(workspaceRoot, path));
    }

    private static bool IsUnderRoot(string path, string root) => CiPathSafety.IsUnderRoot(path, root);

    private static bool PathsEqual(string left, string right) {
        var normalizedLeft = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRight = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(normalizedLeft, normalizedRight, comparison);
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
        // Ensure there's always a newline boundary between consecutive entries.
        writer.WriteLine();
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
                var value = args[++i];
                if (value.Contains('\0') || value.Contains('\n') || value.Contains('\r')) {
                    options.Error = "Invalid value for --changed-files.";
                    return options;
                }
                options.ChangedFilesPath = value;
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
                var value = args[++i];
                if (value.Contains('\0') || value.Contains('\n') || value.Contains('\r')) {
                    options.Error = "Invalid value for --out-env.";
                    return options;
                }
                options.OutEnv = value;
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
