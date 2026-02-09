using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiChangedFilesCommand {
    private static readonly Encoding Utf8Tolerant = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseArgs(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }
        if (options.Error is not null) {
            Console.Error.WriteLine(options.Error);
            return 1;
        }
        if (string.IsNullOrWhiteSpace(options.OutputPath)) {
            Console.Error.WriteLine("Missing --out <path>.");
            return 1;
        }

        var workspace = string.IsNullOrWhiteSpace(options.Workspace) ? Environment.CurrentDirectory : options.Workspace!;
        if (!Directory.Exists(workspace)) {
            Console.Error.WriteLine($"Workspace directory not found: {workspace}");
            return 1;
        }
        var workspaceRoot = Path.GetFullPath(workspace);
        var outputPath = Path.GetFullPath(Path.IsPathRooted(options.OutputPath!)
            ? options.OutputPath!
            : Path.Combine(workspaceRoot, options.OutputPath!));
        if (!CiPathSafety.IsUnderRoot(outputPath, workspaceRoot)) {
            Console.Error.WriteLine($"Output path must be within the workspace. out={outputPath} workspace={workspaceRoot}");
            return 1;
        }
        try {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir)) {
                Directory.CreateDirectory(outputDir);
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to create output directory: {ex.Message}");
            return 1;
        }

        var (success, lines, message) = await TryComputeChangedFilesAsync(workspaceRoot, options.Base, options.Head).ConfigureAwait(false);
        if (!success) {
            // Don't silently produce an empty list on git failures; fall back to a conservative file list.
            var (listed, fallbackLines, fallbackMessage) = await TryListAllFilesAsync(workspaceRoot).ConfigureAwait(false);
            if (listed && fallbackLines.Count > 0) {
                lines = fallbackLines;
                message = string.IsNullOrWhiteSpace(message)
                    ? "Warning: failed to compute diff changed files; fell back to `git ls-files`."
                    : (message + "\nWarning: fell back to `git ls-files`.");
            } else if (!string.IsNullOrWhiteSpace(fallbackMessage)) {
                message = string.IsNullOrWhiteSpace(message) ? fallbackMessage : (message + "\n" + fallbackMessage);
            }
        }

        // The downstream consumers of changed-files.txt are line-based; fail if the repo contains unsafe filenames.
        // Note: git paths cannot contain NUL; `-z` uses NUL as the record separator.
        foreach (var value in lines) {
            if (value.Contains('\n') || value.Contains('\r')) {
                Console.Error.WriteLine("Changed-files contains a path with newline/CR characters, which is not representable in a line-delimited file.");
                return 1;
            }
        }

        lines = lines
            .Where(value => !string.IsNullOrEmpty(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();
        try {
            File.WriteAllLines(outputPath, lines, Utf8Tolerant);
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to write {outputPath}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Changed files: {lines.Count} (written to {outputPath})");
        if (!string.IsNullOrWhiteSpace(message)) {
            var writer = success ? Console.Out : Console.Error;
            writer.WriteLine(message);
        }
        if (!success && options.Strict) {
            return 1;
        }
        return 0;
    }

    private static async Task<(bool Success, List<string> Lines, string Message)> TryComputeChangedFilesAsync(string workspace, string? baseRev, string? headRev) {
        // Preferred: explicit refs (e.g., GitHub PR base/head SHAs).
        var baseProvided = !string.IsNullOrWhiteSpace(baseRev);
        var headProvided = !string.IsNullOrWhiteSpace(headRev);
        if (baseProvided || headProvided) {
            var resolvedBase = (baseRev ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(resolvedBase)) {
                return (false, new List<string>(), "Missing base revision for diff range.");
            }
            var resolvedHead = headProvided ? headRev!.Trim() : "HEAD";
            var range = $"{resolvedBase}...{resolvedHead}";
            var (exit, stdout, stderr) = await GitCli.RunBytesAsync(workspace, "diff", "--name-only", "-z", range, "--").ConfigureAwait(false);
            if (exit == 0) {
                return (true, SplitGitPaths(stdout), string.Empty);
            }
            return (false, new List<string>(), $"Warning: git diff --name-only {range} failed (exit {exit}). {TrimOneLine(DecodeText(stderr))}");
        }

        // Fallback: if this is a merge commit (common for PR workflows), diff the merge parents.
        var hasSecondParent = await HasRevisionAsync(workspace, "HEAD^2").ConfigureAwait(false);
        if (hasSecondParent) {
            var baseParent = await RevParseAsync(workspace, "HEAD^1").ConfigureAwait(false);
            var headParent = await RevParseAsync(workspace, "HEAD^2").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(baseParent) && !string.IsNullOrWhiteSpace(headParent)) {
                var range = $"{baseParent}...{headParent}";
                var (exit, stdout, stderr) = await GitCli.RunBytesAsync(workspace, "diff", "--name-only", "-z", range, "--").ConfigureAwait(false);
                if (exit == 0) {
                    return (true, SplitGitPaths(stdout), string.Empty);
                }
                return (false, new List<string>(), $"Warning: git diff --name-only {range} failed (exit {exit}). {TrimOneLine(DecodeText(stderr))}");
            }
        }

        // Last resort: plain diff (may be empty in CI, but should not fail the pipeline).
        {
            var (exit, stdout, stderr) = await GitCli.RunBytesAsync(workspace, "diff", "--name-only", "-z", "--").ConfigureAwait(false);
            if (exit == 0) {
                return (true, SplitGitPaths(stdout), string.Empty);
            }
            return (false, new List<string>(), $"Warning: git diff --name-only failed (exit {exit}). {TrimOneLine(DecodeText(stderr))}");
        }
    }

    private static async Task<(bool Success, List<string> Lines, string Message)> TryListAllFilesAsync(string workspace) {
        var (exit, stdout, stderr) = await GitCli.RunBytesAsync(workspace, "ls-files", "-z").ConfigureAwait(false);
        if (exit == 0) {
            return (true, SplitGitPaths(stdout), string.Empty);
        }
        return (false, new List<string>(), $"Warning: git ls-files failed (exit {exit}). {TrimOneLine(DecodeText(stderr))}");
    }

    private static async Task<bool> HasRevisionAsync(string workspace, string rev) {
        var (exit, _, _) = await GitCli.RunAsync(workspace, "rev-parse", "--verify", "-q", rev).ConfigureAwait(false);
        return exit == 0;
    }

    private static async Task<string?> RevParseAsync(string workspace, string rev) {
        var (exit, stdout, _) = await GitCli.RunAsync(workspace, "rev-parse", rev).ConfigureAwait(false);
        if (exit != 0) {
            return null;
        }
        var resolved = stdout.Trim();
        return string.IsNullOrWhiteSpace(resolved) ? null : resolved;
    }

    private static List<string> SplitGitPaths(byte[] stdout) {
        if (stdout is null || stdout.Length == 0) {
            return new List<string>();
        }
        var results = new List<string>();
        var start = 0;
        for (var i = 0; i < stdout.Length; i++) {
            if (stdout[i] != 0) {
                continue;
            }
            if (i > start) {
                results.Add(Utf8Tolerant.GetString(stdout, start, i - start));
            }
            start = i + 1;
        }
        if (start < stdout.Length) {
            results.Add(Utf8Tolerant.GetString(stdout, start, stdout.Length - start));
        }
        return results;
    }

    private static string TrimOneLine(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        var trimmed = text.Trim();
        var nl = trimmed.IndexOfAny(new[] { '\r', '\n' });
        return nl >= 0 ? trimmed[..nl].Trim() : trimmed;
    }

    private static string DecodeText(byte[] bytes) {
        if (bytes is null || bytes.Length == 0) {
            return string.Empty;
        }
        return Utf8Tolerant.GetString(bytes);
    }

    private static Options ParseArgs(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            if (IsHelp(arg)) {
                options.ShowHelp = true;
                return options;
            }
            if (arg.Equals("--out", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --out.";
                    return options;
                }
                options.OutputPath = args[++i];
                continue;
            }
            if (arg.Equals("--workspace", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --workspace.";
                    return options;
                }
                options.Workspace = args[++i];
                continue;
            }
            if (arg.Equals("--base", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --base.";
                    return options;
                }
                options.Base = args[++i];
                continue;
            }
            if (arg.Equals("--head", StringComparison.OrdinalIgnoreCase)) {
                if (i + 1 >= args.Length) {
                    options.Error = "Missing value for --head.";
                    return options;
                }
                options.Head = args[++i];
                continue;
            }
            if (arg.Equals("--strict", StringComparison.OrdinalIgnoreCase)) {
                options.Strict = true;
                continue;
            }
            options.Error = $"Unknown option '{arg}' for changed-files.";
            return options;
        }

        if (!string.IsNullOrWhiteSpace(options.Head) && string.IsNullOrWhiteSpace(options.Base)) {
            options.Error = "Option --head requires --base.";
        }
        if (options.Error is null) {
            if (!string.IsNullOrWhiteSpace(options.Base) && !IsSafeRevision(options.Base!)) {
                options.Error = "Invalid --base revision (must not start with '-' and must not contain whitespace/newlines).";
            } else if (!string.IsNullOrWhiteSpace(options.Head) && !IsSafeRevision(options.Head!)) {
                options.Error = "Invalid --head revision (must not start with '-' and must not contain whitespace/newlines).";
            }
        }
        return options;
    }

    private static bool IsSafeRevision(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        var trimmed = value.Trim();
        if (trimmed.StartsWith("-", StringComparison.Ordinal)) {
            return false;
        }
        if (trimmed.IndexOfAny(new[] { '\n', '\r', '\0' }) >= 0) {
            return false;
        }
        return !trimmed.Any(char.IsWhiteSpace);
    }

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp() {
        Console.WriteLine("Compute changed files list for CI:");
        Console.WriteLine("  intelligencex ci changed-files --out <path> [--workspace <path>] [--base <rev>] [--head <rev>] [--strict]");
        Console.WriteLine();
        Console.WriteLine("Notes:");
        Console.WriteLine("  If --base/--head are not provided, this command will attempt to detect a PR merge commit and diff HEAD^1...HEAD^2.");
    }

    private sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? OutputPath { get; set; }
        public string? Workspace { get; set; }
        public string? Base { get; set; }
        public string? Head { get; set; }
        public bool Strict { get; set; }
    }
}
