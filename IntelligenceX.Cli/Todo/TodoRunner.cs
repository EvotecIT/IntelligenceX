using System;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Todo;

internal static class TodoRunner {
    public static Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(1);
        }
        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "sync-bot-feedback" => BotFeedbackSyncRunner.RunAsync(rest),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    private static bool IsHelp(string value) {
        return value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("help", StringComparison.OrdinalIgnoreCase);
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private static void PrintHelp() {
        Console.WriteLine("TODO commands:");
        Console.WriteLine("  intelligencex todo sync-bot-feedback [options]");
        Console.WriteLine();
        Console.WriteLine("Use `intelligencex todo sync-bot-feedback --help` for options.");
    }
}

