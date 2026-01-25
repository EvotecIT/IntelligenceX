using System;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;

namespace IntelligenceX.Examples;

internal sealed class ExampleEasyChat : IExample {
    public string Name => "easy-chat";
    public string Description => "Use the Easy helper for a single message.";

    public async Task RunAsync() {
        var result = await Easy.ChatAsync("Hello from the easy API!").ConfigureAwait(false);
        Console.WriteLine(result.Text ?? "<no text>");
    }
}
