using System;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Ci;

internal static class CiRunner {
    public static Task<int> RunAsync(string[] args) {
        if (args.Length == 0) {
            PrintHelp();
            return Task.FromResult(1);
        }
        if (IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(0);
        }

        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "changed-files" => CiChangedFilesCommand.RunAsync(rest),
            "tune-reviewer-budgets" => CiTuneReviewerBudgetsCommand.RunAsync(rest),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintHelp() {
        Console.WriteLine("CI commands:");
        Console.WriteLine("  intelligencex ci changed-files --out <path> [--workspace <path>] [--base <rev>] [--head <rev>] [--strict]");
        Console.WriteLine("  intelligencex ci tune-reviewer-budgets --changed-files <path> [--changed-threshold <n>] [--catalog-threshold <n>]");
        Console.WriteLine("                              [--max-files <n>] [--max-patch-chars <n>] [--out-env <path>]");
    }
}
