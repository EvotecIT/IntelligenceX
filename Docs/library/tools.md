# Tool Calling (OpenAI Native)

Native tool calling lets the model request local tool execution and continue the response chain with tool outputs.
Use `ToolRunner` for the simplest flow or drive the loop manually if you need full control.

## Example tool

```csharp
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Tools;

public sealed class EchoTool : ITool {
    private static readonly ToolDefinition DefinitionValue = new(
        "echo",
        "Echo back the provided text.",
        new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("text", new JsonObject().Add("type", "string")))
            .Add("required", new JsonArray().Add("text"))
            .Add("additionalProperties", false));

    public ToolDefinition Definition => DefinitionValue;

    public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(arguments?.GetString("text") ?? string.Empty);
    }
}
```

## ToolRunner example

```csharp
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

var options = new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.Native
};
using var client = await IntelligenceXClient.ConnectAsync(options);
await client.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));

var registry = new ToolRegistry();
registry.Register(new EchoTool());

var input = ChatInput.FromText("Call the echo tool with text 'hello'.");
var chatOptions = new ChatOptions {
    Model = "gpt-5.3-codex",
    ParallelToolCalls = true
};

var result = await ToolRunner.RunAsync(
    client,
    input,
    chatOptions,
    registry,
    new ToolRunnerOptions { MaxRounds = 3, ParallelToolCalls = true });

Console.WriteLine(EasyChatResult.FromTurn(result.FinalTurn).Text);
```

If you enable parallel tool calls, ensure your tools are thread-safe.

## Manual tool loop (advanced)

```csharp
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

var registry = new ToolRegistry();
registry.Register(new EchoTool());

var input = ChatInput.FromText("Call the echo tool with text 'hello'.");
var options = new ChatOptions {
    Model = "gpt-5.3-codex",
    Tools = registry.GetDefinitions(),
    ToolChoice = ToolChoice.Auto
};

var turn = await client.ChatAsync(input, options);
var calls = ToolCallParser.Extract(turn);
if (calls.Count > 0) {
    var call = calls[0];
    if (registry.TryGet(call.Name, out var tool)) {
        var output = await tool.InvokeAsync(call.Arguments, CancellationToken.None);
        var followUp = new ChatInput().AddToolOutput(call.CallId, output ?? string.Empty);
        options.PreviousResponseId = turn.ResponseId;
        var finalTurn = await client.ChatAsync(followUp, options);
        Console.WriteLine(EasyChatResult.FromTurn(finalTurn).Text);
    }
}
```
