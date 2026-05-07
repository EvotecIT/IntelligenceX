using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiVerifyManagedWorkflowCommand {
    private const string PullRequestBypassContract = "github.event_name!='pull_request'";
    private const string ForkGateContract = "!github.event.pull_request.head.repo.fork";
    private const string ForceReviewLabelContract = "contains(github.event.pull_request.labels.*.name,'needs-ai-review')";
    private const string BooleanAndOperator = "&&";
    private static readonly Regex BeginMarker = new(@"(?m)^[ \t]*# INTELLIGENCEX:BEGIN[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex EndMarker = new(@"(?m)^[ \t]*# INTELLIGENCEX:END[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex ReviewJob = new(@"(?m)^[ \t]*review:[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex ReusableWorkflow = new(@"(?m)^[ \t]*uses:[ \t]+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)[ \t\r]*$", RegexOptions.Compiled);
    private static readonly Regex IfExpression = new(@"(?m)^[ \t]*if:[ \t]+\$\{\{(?<expr>.+)\}\}[ \t\r]*$", RegexOptions.Compiled);
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
            if (IsManagedSafetyGateContract(expression)) {
                return true;
            }
        }

        return false;
    }

    private static bool IsManagedSafetyGateContract(string expression) {
        var hasForkGate = false;
        var hasForceReviewGate = false;
        var terms = new List<string>();
        CollectOrTerms(expression, terms);
        foreach (var term in terms) {
            if (IsStandaloneGateTerm(term, ForkGateContract)) {
                hasForkGate = true;
            } else if (IsStandaloneForceReviewLabelGateTerm(term)) {
                hasForceReviewGate = true;
            }
        }

        return hasForkGate && hasForceReviewGate;
    }

    private static bool IsStandaloneGateTerm(string term, string contract) {
        var normalized = NormalizeGateExpression(term);
        return normalized.Equals(contract, StringComparison.Ordinal);
    }

    private static bool IsStandaloneForceReviewLabelGateTerm(string term) {
        var normalized = NormalizeGateExpression(term);
        if (normalized.Contains(BooleanAndOperator, StringComparison.Ordinal)) {
            return false;
        }

        return normalized.Equals(ForceReviewLabelContract, StringComparison.Ordinal);
    }

    private static void CollectOrTerms(string expression, List<string> terms) {
        expression = StripWrappingParentheses(expression.Trim());
        var start = 0;
        var depth = 0;
        for (var i = 0; i < expression.Length; i++) {
            if (expression[i] == '\'' || expression[i] == '"') {
                i = SkipQuotedString(expression, i);
                continue;
            }

            if (expression[i] == '(') {
                depth++;
            } else if (expression[i] == ')') {
                depth = Math.Max(0, depth - 1);
            } else if (depth == 0 &&
                       i + 1 < expression.Length &&
                       expression[i] == '|' &&
                       expression[i + 1] == '|') {
                CollectOrTerms(expression.Substring(start, i - start), terms);
                i++;
                start = i + 1;
            }
        }

        if (start == 0) {
            terms.Add(expression);
        } else {
            CollectOrTerms(expression.Substring(start), terms);
        }
    }

    private static string NormalizeGateExpression(string expression) {
        expression = StripWrappingParentheses(expression.Trim());
        var builder = new System.Text.StringBuilder(expression.Length);
        foreach (var ch in expression) {
            if (!char.IsWhiteSpace(ch)) {
                builder.Append(ch);
            }
        }
        return builder.ToString();
    }

    private static string StripWrappingParentheses(string expression) {
        while (expression.Length >= 2 &&
               expression[0] == '(' &&
               expression[expression.Length - 1] == ')' &&
               IsWrappedBySinglePair(expression)) {
            expression = expression.Substring(1, expression.Length - 2).Trim();
        }

        return expression;
    }

    private static bool IsWrappedBySinglePair(string expression) {
        var depth = 0;
        for (var i = 0; i < expression.Length; i++) {
            if (expression[i] == '\'' || expression[i] == '"') {
                i = SkipQuotedString(expression, i);
                continue;
            }

            if (expression[i] == '(') {
                depth++;
            } else if (expression[i] == ')') {
                depth--;
                if (depth == 0 && i < expression.Length - 1) {
                    return false;
                }
            }
        }

        return depth == 0;
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
