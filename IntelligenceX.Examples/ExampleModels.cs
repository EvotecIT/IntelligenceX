using System;
using System.Threading.Tasks;

namespace IntelligenceX.Examples;

internal sealed class ExampleModels : IExample {
    public string Name => "models";
    public string Description => "List available ChatGPT Codex models for the current account.";

    public async Task RunAsync() {
        using var client = await ExampleHelpers.StartClientAsync().ConfigureAwait(false);
        await ExampleHelpers.InitializeAsync(client).ConfigureAwait(false);
        await ExampleHelpers.EnsureChatGptLoginAsync(client).ConfigureAwait(false);

        var result = await client.ListModelsAsync().ConfigureAwait(false);
        if (result.Models.Count == 0) {
            Console.WriteLine("No models returned by the server.");
            return;
        }

        Console.WriteLine("Available models:");
        foreach (var model in result.Models) {
            var name = string.IsNullOrWhiteSpace(model.DisplayName) ? model.Id : model.DisplayName;
            Console.WriteLine($"- {model.Id} ({name})");
        }
    }
}
