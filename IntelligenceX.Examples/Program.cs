using System;
using System.Diagnostics;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.Json;

namespace IntelligenceX.Examples;

internal static class Program {
    private const string DefaultModel = "gpt-5.1-codex";

    private static async Task<int> Main() {
        Console.WriteLine("IntelligenceX examples");
        Console.WriteLine("Starting Codex app-server...");

        var client = await AppServerClient.StartAsync(new AppServerOptions {
            ExecutablePath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH") ?? "codex",
            Arguments = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS") ?? "app-server"
        });

        client.NotificationReceived += (_, args) => {
            var text = ExtractDelta(args.Params);
            if (!string.IsNullOrWhiteSpace(text)) {
                Console.Write(text);
            } else {
                Console.WriteLine($"\n{args.Method}: {args.Params}");
            }
        };

        client.StandardErrorReceived += (_, line) => {
            Console.Error.WriteLine($"[app-server] {line}");
        };

        await client.InitializeAsync(new ClientInfo("IntelligenceX.Examples", "IntelligenceX Examples", "0.1.0"));

        Console.WriteLine("Choose login method: 1) ChatGPT 2) API key");
        var choice = Console.ReadLine();
        if (choice == "2") {
            Console.Write("API key: ");
            var apiKey = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(apiKey)) {
                await client.LoginWithApiKeyAsync(apiKey);
            }
        } else {
            var login = await client.StartChatGptLoginAsync();
            Console.WriteLine($"Open this URL to login: {login.AuthUrl}");
            TryOpenUrl(login.AuthUrl);
            Console.WriteLine("Waiting for login completion...");
            await client.WaitForLoginCompletionAsync(login.LoginId);
        }

        var account = await client.ReadAccountAsync();
        Console.WriteLine($"Logged in as: {account.Email} ({account.PlanType})");

        var thread = await client.StartThreadAsync(DefaultModel);
        Console.WriteLine($"Thread started: {thread.Id}");

        Console.WriteLine("Send a message (empty line to quit):");
        while (true) {
            Console.Write("you> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) {
                break;
            }
            await client.StartTurnAsync(thread.Id, input);
            Console.WriteLine();
        }

        client.Dispose();
        return 0;
    }

    private static string? ExtractDelta(JsonValue? value) {
        var obj = value?.AsObject();
        var delta = obj?.GetObject("delta");
        var text = delta?.GetString("text");
        return text;
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
