using System;
using System.IO;
using System.Threading.Tasks;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Ci;

internal static class CiReviewerFailureGateCommand {
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

        var source = await EvaluateAsync("Source reviewer", options.SourceReviewerExit, options.SourceLogPath)
            .ConfigureAwait(false);
        if (source.ShouldFail) {
            return 1;
        }

        var releaseUnix = await EvaluateAsync("Release reviewer (unix)", options.ReleaseUnixExit, options.ReleaseUnixLogPath)
            .ConfigureAwait(false);
        if (releaseUnix.ShouldFail) {
            return 1;
        }

        var releaseWindows = await EvaluateAsync("Release reviewer (windows)", options.ReleaseWindowsExit, options.ReleaseWindowsLogPath)
            .ConfigureAwait(false);
        return releaseWindows.ShouldFail ? 1 : 0;
    }

    private static async Task<GateResult> EvaluateAsync(string stage, string? exitCode, string? logPath) {
        if (!HasNonZeroExit(exitCode)) {
            return GateResult.Pass;
        }

        var logText = string.Empty;
        if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath)) {
            logText = await File.ReadAllTextAsync(logPath).ConfigureAwait(false);
        }

        var failure = ReviewDiagnostics.ClassifyWorkflowFailureLog(logText);
        if (!failure.ShouldFailWorkflow) {
            Console.WriteLine($"{stage} exited with {exitCode}, classified as '{failure.Kind}', and remains fail-open by policy.");
            return GateResult.Pass;
        }

        Console.Error.WriteLine($"{stage} exited with {exitCode}, classified as '{failure.Kind}', and must fail this required check.");
        Console.Error.WriteLine(failure.Detail);
        return GateResult.Fail;
    }

    private static bool HasNonZeroExit(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        return !value.Trim().Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    private static Options ParseArgs(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg.ToLowerInvariant()) {
                case "help":
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    return options;
                case "--source-reviewer-exit":
                    options.SourceReviewerExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--source-log":
                    options.SourceLogPath = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--release-unix-exit":
                    options.ReleaseUnixExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--release-unix-log":
                    options.ReleaseUnixLogPath = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--release-windows-exit":
                    options.ReleaseWindowsExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--release-windows-log":
                    options.ReleaseWindowsLogPath = ReadOptionalValue(args, ref i, arg, options);
                    break;
                default:
                    options.Error = $"Unknown option '{arg}' for reviewer-failure-gate.";
                    return options;
            }
        }

        return options;
    }

    private static string? ReadOptionalValue(string[] args, ref int index, string name, Options options) {
        if (index + 1 >= args.Length) {
            options.Error = $"Missing value for {name}.";
            return null;
        }

        index++;
        var value = args[index];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void PrintHelp() {
        Console.WriteLine("Fail required reviewer CI checks for non-passable reviewer failures such as auth-remediation failures.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex ci reviewer-failure-gate [--source-reviewer-exit <n>] [--source-log <path>] [release options]");
    }

    private readonly record struct GateResult(bool ShouldFail) {
        public static GateResult Pass { get; } = new(false);
        public static GateResult Fail { get; } = new(true);
    }

    private sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? SourceReviewerExit { get; set; }
        public string? SourceLogPath { get; set; }
        public string? ReleaseUnixExit { get; set; }
        public string? ReleaseUnixLogPath { get; set; }
        public string? ReleaseWindowsExit { get; set; }
        public string? ReleaseWindowsLogPath { get; set; }
    }
}
