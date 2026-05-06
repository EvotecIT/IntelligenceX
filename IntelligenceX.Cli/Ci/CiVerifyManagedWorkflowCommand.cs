using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiVerifyManagedWorkflowCommand {
    private static readonly Regex BeginMarker = new(@"(?m)^[ \t]*# INTELLIGENCEX:BEGIN[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex EndMarker = new(@"(?m)^[ \t]*# INTELLIGENCEX:END[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex ReviewJob = new(@"(?m)^[ \t]*review:[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex ReusableWorkflow = new(@"(?m)^[ \t]*uses:[ \t]+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex IfExpression = new(@"(?m)^[ \t]*if:[ \t]+\$\{\{(?<expr>.+)\}\}[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex ForceReviewLabelExpression = new(@"contains\(\s*github\.event\.pull_request\.labels\.\*\.name\s*,\s*['""]needs-ai-review['""]\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ProviderInput = new(@"(?m)^[ \t]*provider:[ \t]+", RegexOptions.Compiled);
    private static readonly Regex ModelInput = new(@"(?m)^[ \t]*model:[ \t]+", RegexOptions.Compiled);
    private static readonly Regex InheritedSecrets = new(@"(?m)^[ \t]*secrets:[ \t]*inherit[ \t\r]*$", RegexOptions.Compiled);

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
        if (!HasManagedForkAndForceReviewSafetyGate(managedBlock)) {
            Console.Error.WriteLine("ERROR: missing fork/dependabot safety gate in managed block");
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

    private static bool HasManagedForkAndForceReviewSafetyGate(string managedBlock) {
        foreach (Match match in IfExpression.Matches(managedBlock)) {
            var expression = match.Groups["expr"].Value;
            if (ContainsOutsideString(expression, "head.repo.fork") &&
                ContainsForceReviewLabelCall(expression)) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsForceReviewLabelCall(string expression) {
        for (var i = 0; i < expression.Length; i++) {
            if (expression[i] == '\'' || expression[i] == '"') {
                i = SkipQuotedString(expression, i);
                continue;
            }

            if (!StartsWithIgnoreCase(expression, i, "contains(")) {
                continue;
            }

            var end = FindCallEnd(expression, i + "contains".Length);
            if (end >= i && ForceReviewLabelExpression.IsMatch(expression.Substring(i, end - i + 1))) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsOutsideString(string expression, string value) {
        for (var i = 0; i < expression.Length; i++) {
            if (expression[i] == '\'' || expression[i] == '"') {
                i = SkipQuotedString(expression, i);
                continue;
            }

            if (StartsWith(expression, i, value, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static int FindCallEnd(string expression, int openParenIndex) {
        var depth = 0;
        for (var i = openParenIndex; i < expression.Length; i++) {
            if (expression[i] == '\'' || expression[i] == '"') {
                i = SkipQuotedString(expression, i);
                continue;
            }

            if (expression[i] == '(') {
                depth++;
            } else if (expression[i] == ')') {
                depth--;
                if (depth == 0) {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int SkipQuotedString(string expression, int quoteIndex) {
        var quote = expression[quoteIndex];
        for (var i = quoteIndex + 1; i < expression.Length; i++) {
            if (expression[i] != quote) {
                continue;
            }

            if (i + 1 < expression.Length && expression[i + 1] == quote) {
                i++;
                continue;
            }

            return i;
        }

        return expression.Length - 1;
    }

    private static bool StartsWithIgnoreCase(string expression, int startIndex, string value) {
        return StartsWith(expression, startIndex, value, StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWith(string expression, int startIndex, string value, StringComparison comparison) {
        return startIndex >= 0 &&
            startIndex + value.Length <= expression.Length &&
            string.Compare(expression, startIndex, value, 0, value.Length, comparison) == 0;
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
