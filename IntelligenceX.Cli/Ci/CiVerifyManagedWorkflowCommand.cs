using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiVerifyManagedWorkflowCommand {
    private static readonly Regex BeginMarker = new(@"(?m)^[ \t]*# INTELLIGENCEX:BEGIN[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex EndMarker = new(@"(?m)^[ \t]*# INTELLIGENCEX:END[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex ReviewJob = new(@"(?m)^[ \t]*review:[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex ReusableWorkflow = new(@"(?m)^[ \t]*uses:[ \t]+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex ForkGate = new(@"(?m)^[ \t]*if:[ \t]+\$\{\{.+head\.repo\.fork.+\}\}[ \t]*$", RegexOptions.Compiled);
    private static readonly Regex ProviderInput = new(@"(?m)^[ \t]*provider:[ \t]+", RegexOptions.Compiled);
    private static readonly Regex ModelInput = new(@"(?m)^[ \t]*model:[ \t]+", RegexOptions.Compiled);
    private static readonly Regex InheritedSecrets = new(@"(?m)^[ \t]*secrets:[ \t]*inherit[ \t]*$", RegexOptions.Compiled);

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

        var workflowPath = Path.GetFullPath(options.WorkflowPath ?? ".github/workflows/review-intelligencex.yml");
        if (!File.Exists(workflowPath)) {
            Console.Error.WriteLine($"ERROR: workflow file not found: {options.WorkflowPath}");
            return Task.FromResult(1);
        }

        var content = File.ReadAllText(workflowPath);
        if (!TryExtractManagedBlock(content, out var managedBlock, out var error)) {
            Console.Error.WriteLine(error);
            return Task.FromResult(1);
        }

        if (!ReviewJob.IsMatch(managedBlock)) {
            Console.Error.WriteLine("ERROR: missing review job in managed block");
            return Task.FromResult(1);
        }
        if (!ReusableWorkflow.IsMatch(managedBlock)) {
            Console.Error.WriteLine("ERROR: missing reusable review workflow reference in managed block");
            return Task.FromResult(1);
        }
        if (!ForkGate.IsMatch(managedBlock)) {
            Console.Error.WriteLine("ERROR: missing fork safety gate in managed block");
            return Task.FromResult(1);
        }
        if (!ProviderInput.IsMatch(managedBlock)) {
            Console.Error.WriteLine("ERROR: missing provider input in managed block");
            return Task.FromResult(1);
        }
        if (!ModelInput.IsMatch(managedBlock)) {
            Console.Error.WriteLine("ERROR: missing model input in managed block");
            return Task.FromResult(1);
        }

        if (InheritedSecrets.IsMatch(managedBlock)) {
            Console.WriteLine("OK: workflow uses inherited secrets");
        } else if (managedBlock.Contains("INTELLIGENCEX_AUTH_B64", StringComparison.Ordinal)) {
            Console.WriteLine("OK: workflow uses explicit secrets block");
        } else {
            Console.Error.WriteLine("ERROR: workflow has neither secrets: inherit nor explicit INTELLIGENCEX secrets in managed block");
            return Task.FromResult(1);
        }

        Console.WriteLine($"OK: managed workflow validation passed ({options.WorkflowPath})");
        return Task.FromResult(0);
    }

    internal static bool TryExtractManagedBlock(string content, out string managedBlock, out string error) {
        managedBlock = string.Empty;
        error = string.Empty;

        var begin = BeginMarker.Match(content);
        if (!begin.Success) {
            error = "ERROR: missing INTELLIGENCEX:BEGIN marker";
            return false;
        }

        var end = EndMarker.Match(content, begin.Index + begin.Length);
        if (!end.Success) {
            error = "ERROR: missing INTELLIGENCEX:END marker";
            return false;
        }

        if (end.Index <= begin.Index + begin.Length) {
            error = "ERROR: empty managed workflow block";
            return false;
        }

        managedBlock = content.Substring(begin.Index + begin.Length, end.Index - begin.Index - begin.Length);
        return true;
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
                case "--workflow":
                    options.WorkflowPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                default:
                    options.Error = $"Unknown option '{arg}' for verify-managed-workflow.";
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

    private static void PrintHelp() {
        Console.WriteLine("Validate the managed IntelligenceX reviewer workflow block.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex ci verify-managed-workflow --workflow <path>");
    }

    private sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? WorkflowPath { get; set; }
    }
}
