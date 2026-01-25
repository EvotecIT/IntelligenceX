using System;
using System.Threading.Tasks;
using IntelligenceX.Copilot;

namespace IntelligenceX.Examples;

internal sealed class ExampleCopilotChat : IExample {
    public string Name => "copilot-chat";
    public string Description => "Connect to Copilot CLI and send a prompt.";

    public async Task RunAsync() {
        await using var client = await CopilotClient.StartAsync().ConfigureAwait(false);
        var auth = await client.GetAuthStatusAsync().ConfigureAwait(false);
        if (!auth.IsAuthenticated) {
            Console.WriteLine("Copilot CLI is not authenticated. Run `copilot` to login first.");
            return;
        }

        var session = await client.CreateSessionAsync(new CopilotSessionOptions {
            Model = "gpt-5"
        }).ConfigureAwait(false);

        var response = await session.SendAndWaitAsync(new CopilotMessageOptions {
            Prompt = "Say hello from Copilot CLI."
        }).ConfigureAwait(false);

        Console.WriteLine(response ?? "<no response>");
    }
}
