using System;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Cli.Setup.Web;

internal static class WebRunner {
    private const string DefaultUrl = "http://127.0.0.1:1461/";

    public static async Task<int> RunAsync(string[] args) {
        var url = DefaultUrl;
        if (args.Length > 0 && args[0].StartsWith("http", StringComparison.OrdinalIgnoreCase)) {
            if (Uri.TryCreate(args[0], UriKind.Absolute, out var parsed)
                && parsed.Scheme == Uri.UriSchemeHttp) {
                if (parsed.IsLoopback) {
                    var builder = new UriBuilder(parsed) {
                        Path = "/",
                        Query = string.Empty,
                        Fragment = string.Empty
                    };
                    url = builder.Uri.ToString();
                    if (parsed.AbsolutePath != "/") {
                        Console.WriteLine("Normalized setup UI URL to root path.");
                    }
                } else {
                    Console.WriteLine("Non-loopback hosts are not allowed for setup UI. Using localhost instead.");
                }
            } else {
                Console.WriteLine("Only http:// loopback URLs are supported. Using localhost instead.");
            }
        }

        using var server = new WebServer(url);
        Console.WriteLine($"Starting setup UI at {url}");
        try {
            server.Start();
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to start setup UI at {url}: {ex.Message}");
            Console.Error.WriteLine("Ensure the port is free and you have permission to bind the URL.");
            return 1;
        }
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
