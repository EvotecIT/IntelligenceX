using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebRunner {
    private const string DefaultUrl = "http://127.0.0.1:1459/";

    public static async Task<int> RunAsync(string[] args) {
        var url = args.Length > 0 && args[0].StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? args[0]
            : DefaultUrl;

        using var server = new WebServer(url);
        Console.WriteLine($"Starting setup UI at {url}");
        server.Start();
        TryOpenUrl(url);

        Console.WriteLine("Press Ctrl+C to stop.");
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, evt) => {
            evt.Cancel = true;
            cts.Cancel();
        };

        try {
            await server.WaitForShutdownAsync(cts.Token).ConfigureAwait(false);
            return 0;
        } catch (OperationCanceledException) {
            return 0;
        }
    }

    private static void TryOpenUrl(string url) {
        try {
            var psi = new System.Diagnostics.ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        } catch {
            // Best effort.
        }
    }
}
