using System;
using System.Threading.Tasks;
using IntelligenceX.AppServer;
using IntelligenceX.Fluent;

namespace IntelligenceX.Examples;

internal sealed class ExampleFluentChat : IExample {
    public string Name => "fluent-chat";
    public string Description => "Use the fluent API to login and chat.";

    public async Task RunAsync() {
        var options = new AppServerOptions {
            ExecutablePath = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_PATH") ?? "codex",
            Arguments = Environment.GetEnvironmentVariable("CODEX_APP_SERVER_ARGS") ?? "app-server"
        };

        await using var session = await AppServerFluent.StartAsync(options).ConfigureAwait(false);
        await session.InitializeAsync(new ClientInfo("IntelligenceX.Examples", "Fluent Example", "0.1.0")).ConfigureAwait(false);
        var login = await session.LoginChatGptAsync().ConfigureAwait(false);
        Console.WriteLine($"Open this URL: {login.Login.AuthUrl}");
        await login.WaitAsync().ConfigureAwait(false);

        var thread = await session.StartThreadAsync("gpt-5.1-codex").ConfigureAwait(false);
        await thread.SendAsync("Hello from fluent API!").ConfigureAwait(false);
    }
}
