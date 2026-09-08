using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.Examples;

internal sealed class ExampleCopilotChat : IExample {
    public string Name => "copilot-chat";
    public string Description => "Use native Copilot HTTP with a GitHub credential; no CLI is required.";

    public async Task RunAsync() {
        string model = Environment.GetEnvironmentVariable("COPILOT_MODEL")
            ?? throw new InvalidOperationException("Set COPILOT_MODEL to an available model and COPILOT_GITHUB_TOKEN to your authorized GitHub credential.");
        var options = new IntelligenceXClientOptions { TransportKind = OpenAITransportKind.CopilotNative, DefaultModel = model };
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await using var client = await IntelligenceXClient.ConnectAsync(options, deadline.Token).ConfigureAwait(false);
        var result = await client.ChatAsync(ChatInput.FromText("Say hello from native Copilot."),
            new ChatOptions { Ephemeral = true }, deadline.Token).ConfigureAwait(false);
        Console.WriteLine(string.Concat(result.Outputs.Select(output => output.Text)));
    }
}