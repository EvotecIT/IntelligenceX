using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.Service;

internal static class Program {
    public static async Task<int> Main(string[] args) {
        var options = ServiceOptions.Parse(args, out var error);
        if (!string.IsNullOrWhiteSpace(error)) {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            ServiceOptions.WriteHelp();
            return 2;
        }

        if (options.ShowHelp) {
            ServiceOptions.WriteHelp();
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => {
            e.Cancel = true;
            cts.Cancel();
        };

        try {
            var server = new ChatServiceServer(options);
            await server.RunAsync(cts.Token).ConfigureAwait(false);
            return 0;
        } catch (OperationCanceledException) {
            return 130;
        }
    }
}

