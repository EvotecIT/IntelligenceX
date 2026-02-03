using System;
using System.Threading.Tasks;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Tools;
using IntelligenceX.Tools.System;

namespace IntelligenceX.Examples;

internal sealed class ExampleToolRunner : IExample {
    public string Name => "Tool Runner (Native)";
    public string Description => "Calls a local WSL status tool via native tool calling.";

    public async Task RunAsync() {
        var options = new IntelligenceXClientOptions {
            TransportKind = OpenAITransportKind.Native
        };
        using var client = await IntelligenceXClient.ConnectAsync(options).ConfigureAwait(false);
        await ExampleHelpers.InitializeAsync(client).ConfigureAwait(false);
        await ExampleHelpers.EnsureChatGptLoginAsync(client).ConfigureAwait(false);

        var registry = new ToolRegistry();
        registry.Register(new WslStatusTool());

        var input = ChatInput.FromText("Is WSL running? Summarize the distribution status.");
        var chatOptions = new ChatOptions {
            Model = "gpt-5.2-codex"
        };

        var result = await ToolRunner.RunAsync(client, input, chatOptions, registry).ConfigureAwait(false);
        var summary = EasyChatResult.FromTurn(result.FinalTurn);

        Console.WriteLine();
        Console.WriteLine(summary.Text);
    }
}
