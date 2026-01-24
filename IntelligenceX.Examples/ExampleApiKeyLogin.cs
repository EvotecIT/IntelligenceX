using System;
using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal sealed class ExampleApiKeyLogin : IExample {
    public string Name => "login-apikey";
    public string Description => "Login with API key and show account details.";

    public async Task RunAsync() {
        using var client = await ExampleHelpers.StartClientAsync().ConfigureAwait(false);
        await ExampleHelpers.InitializeAsync(client).ConfigureAwait(false);
        await ExampleHelpers.LoginApiKeyAsync(client).ConfigureAwait(false);
        var account = await client.ReadAccountAsync().ConfigureAwait(false);
        Console.WriteLine($"Logged in as: {account.Email} ({account.PlanType})");
    }
}
