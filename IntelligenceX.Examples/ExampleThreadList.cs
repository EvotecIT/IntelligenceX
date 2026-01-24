using System;
using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal sealed class ExampleThreadList : IExample {
    public string Name => "thread-list";
    public string Description => "List threads and print basic metadata.";

    public async Task RunAsync() {
        using var client = await ExampleHelpers.StartClientAsync().ConfigureAwait(false);
        await ExampleHelpers.InitializeAsync(client).ConfigureAwait(false);
        await ExampleHelpers.LoginChatGptAsync(client).ConfigureAwait(false);

        var result = await client.ListThreadsAsync(limit: 10, sortKey: "updated_at").ConfigureAwait(false);
        foreach (var thread in result.Data) {
            Console.WriteLine($"{thread.Id} | {thread.Preview} | {thread.ModelProvider}");
        }
    }
}
