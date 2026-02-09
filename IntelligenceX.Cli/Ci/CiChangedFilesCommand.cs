using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiChangedFilesCommand {
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
        var outputPath = options.OutputPath!;
        try {
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir)) {
                Directory.CreateDirectory(outputDir);
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to create output directory: {ex.Message}");
            return 1;
        }

        var (success, lines, message) = await TryComputeChangedFilesAsync(workspace, options.Base, options.Head).ConfigureAwait(false);
        try {
            File.WriteAllLines(outputPath, lines);
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
        if (!string.IsNullOrWhiteSpace(baseRev) || !string.IsNullOrWhiteSpace(headRev)) {
            var resolvedBase = (baseRev ?? string.Empty).Trim();
            var resolvedHead = string.IsNullOrWhiteSpace(headRev) ? "HEAD" : headRev!.Trim();
            if (string.IsNullOrWhiteSpace(resolvedBase)) {
                resolvedBase = "HEAD";
            }
            var (exit, stdout, stderr) = await GitCli.RunAsync(workspace, "diff", "--name-only", resolvedBase, resolvedHead).ConfigureAwait(false);
            if (exit == 0) {
                return (true, SplitLines(stdout), string.Empty);
            }
            return (false, new List<string>(), $"Warning: git diff --name-only {resolvedBase} {resolvedHead} failed (exit {exit}). {TrimOneLine(stderr)}");
        }

        // Fallback: if this is a merge commit (common for PR workflows), diff the merge parents.
        var hasSecondParent = await HasRevisionAsync(workspace, "HEAD^2").ConfigureAwait(false);
        if (hasSecondParent) {
            var baseParent = await RevParseAsync(workspace, "HEAD^1").ConfigureAwait(false);
            var headParent = await RevParseAsync(workspace, "HEAD^2").ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(baseParent) && !string.IsNullOrWhiteSpace(headParent)) {
                var (exit, stdout, stderr) = await GitCli.RunAsync(workspace, "diff", "--name-only", baseParent!, headParent!).ConfigureAwait(false);
                if (exit == 0) {
                    return (true, SplitLines(stdout), string.Empty);
                }
                return (false, new List<string>(), $"Warning: git diff --name-only {baseParent} {headParent} failed (exit {exit}). {TrimOneLine(stderr)}");
            }
        }

        // Last resort: plain diff (may be empty in CI, but should not fail the pipeline).
        {
            var (exit, stdout, stderr) = await GitCli.RunAsync(workspace, "diff", "--name-only").ConfigureAwait(false);
            if (exit == 0) {
                return (true, SplitLines(stdout), string.Empty);
            }
            return (false, new List<string>(), $"Warning: git diff --name-only failed (exit {exit}). {TrimOneLine(stderr)}");
        }
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

    private static List<string> SplitLines(string? stdout) {
        if (string.IsNullOrWhiteSpace(stdout)) {
            return new List<string>();
        }
        return stdout
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static string TrimOneLine(string? text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        var trimmed = text.Trim();
        var nl = trimmed.IndexOfAny(new[] { '\r', '\n' });
        return nl >= 0 ? trimmed[..nl].Trim() : trimmed;
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
        return options;
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
        Console.WriteLine("  If --base/--head are not provided, this command will attempt to detect a PR merge commit and diff HEAD^1..HEAD^2.");
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

