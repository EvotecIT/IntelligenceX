# Providers

## OpenAI (ChatGPT native transport)

- Uses ChatGPT OAuth login and stores tokens under `~/.intelligencex/auth.json`
- Best for users with an existing ChatGPT subscription
- Works with the `EasySession` and `IntelligenceXClient` helpers
- Supports native tool calling via `ToolRunner` and `ToolRegistry`

```csharp
using IntelligenceX.OpenAI;

var session = await EasySession.StartAsync();
var result = await session.AskAsync("Summarize the latest PR");
Console.WriteLine(result.Text);
```

## OpenAI (app-server / Codex)

- Launches the Codex app-server process locally
- Best when you want full app-server features (threads, tools, MCP)
- Works via `AppServerClient` or `IntelligenceXClient` with AppServer transport

```csharp
using IntelligenceX.OpenAI.AppServer;

await using var client = await AppServerClient.StartAsync();
await client.InitializeAsync(new ClientInfo("IntelligenceX", "Demo", "1.0"));
var thread = await client.StartThreadAsync("gpt-5.2-codex");
await client.StartTurnAsync(thread.Id, "Hello from app-server");
```

## Copilot

- Optional provider for users with GitHub Copilot
- Uses Copilot CLI (experimental path)
- Native Copilot client is planned
- Experimental direct HTTP client is available for custom endpoints (unsupported)

```csharp
using IntelligenceX.Copilot;

await using var client = await CopilotClient.StartAsync();
var status = await client.GetStatusAsync();
Console.WriteLine(status.Version);
```

```csharp
using IntelligenceX.Copilot.Direct;

var options = new CopilotDirectOptions {
    Url = "https://example.internal/copilot/chat",
    Token = Environment.GetEnvironmentVariable("COPILOT_DIRECT_TOKEN")
};
using var direct = new CopilotDirectClient(options);
var response = await direct.ChatAsync("Hello", "gpt-5.2-codex");
Console.WriteLine(response);
```

```csharp
using IntelligenceX.Copilot;

var options = new CopilotChatClientOptions {
    Transport = CopilotTransportKind.Cli,
    DefaultModel = "gpt-5.2-codex"
};

await using var chat = await CopilotChatClient.StartAsync(options);
var answer = await chat.ChatAsync("Summarize the latest PR");
Console.WriteLine(answer);
```

```csharp
using IntelligenceX.Copilot;

var options = new CopilotChatClientOptions {
    Transport = CopilotTransportKind.Direct,
    DefaultModel = "gpt-5.2-codex"
};
options.Direct.Url = "https://example.internal/copilot/chat";
options.Direct.Token = Environment.GetEnvironmentVariable("COPILOT_DIRECT_TOKEN");

await using var chat = await CopilotChatClient.StartAsync(options);
var answer = await chat.ChatAsync("Summarize the latest PR");
Console.WriteLine(answer);
```
