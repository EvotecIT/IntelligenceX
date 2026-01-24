using System;
using System.Threading.Tasks;
using IntelligenceX;

namespace IntelligenceX.Examples;

internal sealed class ExampleEasyChat : IExample {
    public string Name => "easy-chat";
    public string Description => "Use the EasySession to login and chat.";

    public async Task RunAsync() {
        await using var session = await EasySession.StartAsync().ConfigureAwait(false);
        using var sub = session.SubscribeDelta(Console.Write);
        await session.ChatAsync("Hello from the easy API!").ConfigureAwait(false);
    }
}
