using System;
using System.Linq;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Telemetry;

internal static class TelemetryRunner {
    public static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "usage" => await UsageTelemetryCliRunner.RunAsync(rest).ConfigureAwait(false),
            _ => PrintHelpReturn()
        };
    }

    public static void PrintHelp() {
        Console.WriteLine("Telemetry commands:");
        Console.WriteLine("  intelligencex telemetry usage <command>");
        Console.WriteLine();
        Console.WriteLine("Usage telemetry commands:");
        Console.WriteLine("  roots list       List registered telemetry source roots");
        Console.WriteLine("  roots add        Register a manual telemetry source root");
        Console.WriteLine("  discover         Discover default roots for known providers");
        Console.WriteLine("  import           Import normalized usage events into the telemetry ledger");
        Console.WriteLine("  stats            Summarize roots, providers, accounts, and event coverage");
    }

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase)
               || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
               || value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }
}
