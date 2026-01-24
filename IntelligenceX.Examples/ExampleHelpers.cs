using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IntelligenceX.AppServer;

namespace IntelligenceX.Examples;

internal static class ExampleHelpers {
    public static async Task<AppServerClient> StartClientAsync() {
        var options = new AppServerOptions {
            ExecutablePath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH") ?? "codex",
            Arguments = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS") ?? "app-server"
        };
        return await AppServerClient.StartAsync(options).ConfigureAwait(false);
    }

    public static async Task InitializeAsync(AppServerClient client) {
        await client.InitializeAsync(new ClientInfo("IntelligenceX.Examples", "IntelligenceX Examples", "0.1.0")).ConfigureAwait(false);
    }

    public static async Task LoginChatGptAsync(AppServerClient client) {
        var login = await client.StartChatGptLoginAsync().ConfigureAwait(false);
        Console.WriteLine($"Open this URL to login: {login.AuthUrl}");
        TryOpenUrl(login.AuthUrl);
        Console.WriteLine("Waiting for login completion...");
        await client.WaitForLoginCompletionAsync(login.LoginId).ConfigureAwait(false);
    }

    public static async Task LoginApiKeyAsync(AppServerClient client) {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey)) {
            Console.Write("API key: ");
            apiKey = Console.ReadLine();
        }
        if (string.IsNullOrWhiteSpace(apiKey)) {
            throw new InvalidOperationException("API key is required.");
        }
        await client.LoginWithApiKeyAsync(apiKey).ConfigureAwait(false);
    }

    public static void AttachNotifications(AppServerClient client) {
        client.NotificationReceived += (_, args) => {
            var text = args.Params?.AsObject()?.GetObject("delta")?.GetString("text");
            if (!string.IsNullOrWhiteSpace(text)) {
                Console.Write(text);
            }
        };

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
}
