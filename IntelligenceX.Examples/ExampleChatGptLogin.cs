using System;
using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal sealed class ExampleChatGptLogin : IExample {
    public string Name => "login-chatgpt";
    public string Description => "Login with ChatGPT and show account details.";

    public async Task RunAsync() {
        using var client = await ExampleHelpers.StartClientAsync().ConfigureAwait(false);
        await ExampleHelpers.InitializeAsync(client).ConfigureAwait(false);
        await ExampleHelpers.LoginChatGptAsync(client).ConfigureAwait(false);
        var account = await client.ReadAccountAsync().ConfigureAwait(false);
        Console.WriteLine($"Logged in as: {account.Email} ({account.PlanType})");
    }
}
