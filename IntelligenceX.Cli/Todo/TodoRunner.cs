using System;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Todo;

internal static class TodoRunner {
    public static Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(0);
        }
        var command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "sync-bot-feedback" => BotFeedbackSyncRunner.RunAsync(rest),
            "build-triage-index" => TriageIndexRunner.RunAsync(rest),
            "triage-index" => TriageIndexRunner.RunAsync(rest),
            "vision-check" => VisionCheckRunner.RunAsync(rest),
            _ => Task.FromResult(PrintHelpReturn(command))
        };
    }

    private static bool IsHelp(string value) {
        return value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("help", StringComparison.OrdinalIgnoreCase);
    }

    private static int PrintHelpReturn(string? command) {
        if (!string.IsNullOrWhiteSpace(command)) {
            Console.Error.WriteLine($"Unknown todo command: {command}");
        }
        PrintHelp();
        return 1;
    }

    private static void PrintHelp() {
        Console.WriteLine("TODO commands:");
        Console.WriteLine("  intelligencex todo sync-bot-feedback [options]");
        Console.WriteLine("  intelligencex todo build-triage-index [options]");
        Console.WriteLine("  intelligencex todo vision-check [options]");
        Console.WriteLine();
        Console.WriteLine("Use `intelligencex todo sync-bot-feedback --help` for options.");
        Console.WriteLine("Use `intelligencex todo build-triage-index --help` for options.");
        Console.WriteLine("Use `intelligencex todo vision-check --help` for options.");
    }
}
