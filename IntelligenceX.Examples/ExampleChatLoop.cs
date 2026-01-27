using System;
using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal sealed class ExampleChatLoop : IExample {
    public string Name => "chat-loop";
    public string Description => "Start a thread and chat in a loop.";

    public async Task RunAsync() {
        using var client = await ExampleHelpers.StartClientAsync().ConfigureAwait(false);
        ExampleHelpers.AttachNotifications(client);
        await ExampleHelpers.InitializeAsync(client).ConfigureAwait(false);
        await ExampleHelpers.EnsureChatGptLoginAsync(client).ConfigureAwait(false);

        var thread = await client.StartNewThreadAsync("gpt-5.1-codex").ConfigureAwait(false);
        Console.WriteLine($"Thread: {thread.Id}");

        while (true) {
            Console.Write("you> ");
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) {
                break;
            }
            await client.ChatAsync(input).ConfigureAwait(false);
            Console.WriteLine();
        }
    }
}
