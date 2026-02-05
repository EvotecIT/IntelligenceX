# Tool Calling (OpenAI Native)

Native tool calling lets the model request local tool execution and continue the response chain with tool outputs.
Use `ToolRunner` for the simplest flow or drive the loop manually if you need full control.

## ToolRunner example

```csharp
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.Tools;
using IntelligenceX.Tools.System;

var options = new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.Native
};
using var client = await IntelligenceXClient.ConnectAsync(options);
await client.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));

var registry = new ToolRegistry();
registry.Register(new WslStatusTool());

var input = ChatInput.FromText("Is WSL running? Summarize the distribution status.");
var chatOptions = new ChatOptions {
    Model = "gpt-5.2-codex",
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
using IntelligenceX.OpenAI.Tools;
using IntelligenceX.Tools.System;

var registry = new ToolRegistry();
registry.Register(new WslStatusTool());

var input = ChatInput.FromText("Check WSL status.");
var options = new ChatOptions {
    Model = "gpt-5.2-codex",
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
