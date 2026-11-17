using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Hook;

/// <summary>Claude Code session hook for the Ledger inbox, published as Native AOT so it starts in milliseconds.
/// Usage: IntelligenceX.Ledger.Hook inbox --config &lt;reporter.json&gt;, with the hook payload on stdin.</summary>
public static class Program {
    public static async Task<int> Main(string[] args) {
        if (args.Length != 3 || args[0] != "inbox" || args[1] != "--config") {
            Console.Error.WriteLine("Usage: IntelligenceX.Ledger.Hook inbox --config <reporter-json-file>");
            return 2;
        }
        return await LedgerInbox.RunAsync(args[2], Console.In, Console.Out);
    }
}
