using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.Examples;

internal static class ExampleHelpers {
    public static async Task<IntelligenceXClient> StartClientAsync() {
        var options = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.Native
        };

        var codexPath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH");
        if (!string.IsNullOrWhiteSpace(codexPath)) {
            options.TransportKind = OpenAITransportKind.AppServer;
            options.AppServerOptions.ExecutablePath = codexPath!;
            options.AppServerOptions.Arguments = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS") ?? "app-server";
        }

        return await IntelligenceXClient.ConnectAsync(options).ConfigureAwait(false);
    }

    public static async Task InitializeAsync(IntelligenceXClient client) {
        await client.InitializeAsync(new ClientInfo("IntelligenceX.Examples", "IntelligenceX Examples", "0.1.0")).ConfigureAwait(false);
    }

    public static async Task LoginChatGptAsync(IntelligenceXClient client) {
        await client.LoginChatGptAndWaitAsync(url => {
            Console.WriteLine($"Open this URL to login: {url}");
            TryOpenUrl(url);
        }).ConfigureAwait(false);
    }

    public static async Task EnsureChatGptLoginAsync(IntelligenceXClient client) {
        await client.EnsureChatGptLoginAsync(onUrl: url => {
            Console.WriteLine($"Open this URL to login: {url}");
            TryOpenUrl(url);
        }).ConfigureAwait(false);
    }

    public static async Task LoginApiKeyAsync(IntelligenceXClient client) {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) {
            Console.Write("API key: ");
            apiKey = Console.ReadLine();
        }
        if (string.IsNullOrWhiteSpace(apiKey)) {
            throw new InvalidOperationException("API key is required.");
        }
        await client.LoginApiKeyAsync(apiKey).ConfigureAwait(false);
    }

    public static void AttachNotifications(IntelligenceXClient client) {
        client.SubscribeDelta(text => {
            if (!string.IsNullOrWhiteSpace(text)) {
                Console.Write(text);
            }
        });
        client.StandardErrorReceived += (_, line) => {
            Console.Error.WriteLine($"[app-server] {line}");
        };
    }

    private static void TryOpenUrl(string url) {
        try {
            var psi = new ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
        } catch {
            // Ignore failures to open browser.
        }
    }

    // Auth-missing detection lives in the main library (IntelligenceXClient.EnsureChatGptLoginAsync).
}
