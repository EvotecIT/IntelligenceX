using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiReviewerRunSummaryCommand {
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

        var markdown = BuildSummary(options);
        var summaryPath = ResolveSummaryPath(options);
        if (string.IsNullOrWhiteSpace(summaryPath)) {
            Console.Write(markdown);
            return Task.FromResult(0);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(summaryPath));
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.AppendAllText(summaryPath, markdown, Encoding.UTF8);
        return Task.FromResult(0);
    }

    internal static string BuildSummary(Options options) {
        var builder = new StringBuilder();
        builder.AppendLine("## IntelligenceX Reviewer");
        builder.AppendLine();
        builder.AppendLine("| Stage | Result | Exit code | Notes |");
        builder.AppendLine("| --- | --- | --- | --- |");
        AppendStage(builder, "Source build", options.SourceBuildOutcome, options.SourceBuildExit,
            "Required before source reviewer runs.");
        AppendStage(builder, "Analysis pre-run", options.AnalysisPreRunOutcome, options.AnalysisPreRunExit,
            "Best-effort; enforcing gate is reported separately.");
        AppendStage(builder, "Source reviewer", options.SourceReviewerOutcome, options.SourceReviewerExit,
            "Runs when reviewer_source is source and build succeeds.");
        AppendStage(builder, "Release reviewer (unix)", options.ReleaseUnixOutcome, options.ReleaseUnixExit,
            "Runs for release reviewer on Linux/macOS.");
        AppendStage(builder, "Release reviewer (windows)", options.ReleaseWindowsOutcome, options.ReleaseWindowsExit,
            "Runs for release reviewer on Windows.");
        builder.AppendLine();

        if (HasNonZeroExit(options.SourceBuildExit) ||
            HasNonZeroExit(options.AnalysisPreRunExit) ||
            HasNonZeroExit(options.SourceReviewerExit) ||
            HasNonZeroExit(options.ReleaseUnixExit) ||
            HasNonZeroExit(options.ReleaseWindowsExit)) {
            builder.AppendLine("> Reviewer or analysis execution produced a non-zero exit code. Selected runtime failures remain fail-open by policy; non-passable reviewer failures are enforced by the final reviewer failure policy gate. The PR comment contains the user-facing failure summary when a PR number is available.");
            builder.AppendLine();
        }

        builder.AppendLine("- Sticky comment deletion is treated as an intentional reset: the next run starts with fresh IX context.");
        builder.AppendLine("- Durable reviewer artifacts, when enabled, are per-run evidence for Actions and audits; they do not override an owner-deleted sticky comment.");
        return builder.ToString();
    }

    private static void AppendStage(StringBuilder builder, string stage, string? outcome, string? exitCode, string notes) {
        builder.Append("| ");
        builder.Append(stage);
        builder.Append(" | `");
        builder.Append(NormalizeCell(outcome));
        builder.Append("` | `");
        builder.Append(NormalizeCell(exitCode));
        builder.Append("` | ");
        builder.Append(notes);
        builder.AppendLine(" |");
    }

    private static bool HasNonZeroExit(string? value) {
        value = NormalizeCell(value);
        return !value.Equals("n/a", StringComparison.OrdinalIgnoreCase) &&
               !value.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCell(string? value) {
        return string.IsNullOrWhiteSpace(value) ? "n/a" : value.Trim().Replace("|", "\\|", StringComparison.Ordinal);
    }

    private static string? ResolveSummaryPath(Options options) {
        if (!string.IsNullOrWhiteSpace(options.SummaryPath)) {
            return options.SummaryPath;
        }
        var envPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        return string.IsNullOrWhiteSpace(envPath) ? null : envPath;
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
                case "--summary":
                    options.SummaryPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--source-build-outcome":
                    options.SourceBuildOutcome = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--source-build-exit":
                    options.SourceBuildExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--analysis-pre-run-outcome":
                    options.AnalysisPreRunOutcome = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--analysis-pre-run-exit":
                    options.AnalysisPreRunExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--source-reviewer-outcome":
                    options.SourceReviewerOutcome = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--source-reviewer-exit":
                    options.SourceReviewerExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--release-unix-outcome":
                    options.ReleaseUnixOutcome = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--release-unix-exit":
                    options.ReleaseUnixExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--release-windows-outcome":
                    options.ReleaseWindowsOutcome = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--release-windows-exit":
                    options.ReleaseWindowsExit = ReadOptionalValue(args, ref i, arg, options);
                    break;
                default:
                    options.Error = $"Unknown option '{arg}' for reviewer-run-summary.";
                    return options;
            }
        }
        return options;
    }

    private static string? ReadRequiredValue(string[] args, ref int index, string name, Options options) {
        if (index + 1 >= args.Length) {
            options.Error = $"Missing value for {name}.";
            return null;
        }
        index++;
        var value = args[index];
        if (string.IsNullOrWhiteSpace(value)) {
            options.Error = $"Empty value for {name}.";
            return null;
        }
        return value.Trim();
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
        Console.WriteLine("Write the reviewer Actions step summary.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex ci reviewer-run-summary [--summary <path>] [stage outcome/exit options]");
    }

    internal sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? SummaryPath { get; set; }
        public string? SourceBuildOutcome { get; set; }
        public string? SourceBuildExit { get; set; }
        public string? AnalysisPreRunOutcome { get; set; }
        public string? AnalysisPreRunExit { get; set; }
        public string? SourceReviewerOutcome { get; set; }
        public string? SourceReviewerExit { get; set; }
        public string? ReleaseUnixOutcome { get; set; }
        public string? ReleaseUnixExit { get; set; }
        public string? ReleaseWindowsOutcome { get; set; }
        public string? ReleaseWindowsExit { get; set; }
    }
}
