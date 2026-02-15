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
var thread = await client.StartThreadAsync("gpt-5.3-codex");
await client.StartTurnAsync(thread.Id, "Hello from app-server");
```

## OpenAI-compatible HTTP (local providers)

- Uses an OpenAI-compatible HTTP endpoint (OpenAI Chat Completions-style)
- Useful for local/self-hosted model servers (Ollama, LM Studio, llama.cpp server, etc.)
- Does not require ChatGPT OAuth

```csharp
using IntelligenceX.OpenAI;

var options = new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.CompatibleHttp,
    DefaultModel = "llama3.1"
};
options.CompatibleHttpOptions.BaseUrl = "http://127.0.0.1:11434";
options.CompatibleHttpOptions.AllowInsecureHttp = true; // loopback http:// opt-in
options.CompatibleHttpOptions.Streaming = true;

await using var client = await IntelligenceXClient.ConnectAsync(options);
var turn = await client.ChatAsync("Summarize the last PR");
Console.WriteLine(EasyChatResult.FromTurn(turn).Text);
```

Notes:
- `BaseUrl` may be either `http://host:port` or `http://host:port/v1`; it will be normalized to `/v1/`.
- Image inputs are not supported for `CompatibleHttp` yet (text + tools only).

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
var response = await direct.ChatAsync("Hello", "gpt-5.3-codex");
Console.WriteLine(response);
```

```csharp
using IntelligenceX.Copilot;

var options = new CopilotChatClientOptions {
    Transport = CopilotTransportKind.Cli,
    DefaultModel = "gpt-5.3-codex"
};

await using var chat = await CopilotChatClient.StartAsync(options);
var answer = await chat.ChatAsync("Summarize the latest PR");
Console.WriteLine(answer);
```

```csharp
using IntelligenceX.Copilot;

var options = new CopilotChatClientOptions {
    Transport = CopilotTransportKind.Direct,
    DefaultModel = "gpt-5.3-codex"
};
options.Direct.Url = "https://example.internal/copilot/chat";
options.Direct.Token = Environment.GetEnvironmentVariable("COPILOT_DIRECT_TOKEN");

await using var chat = await CopilotChatClient.StartAsync(options);
var answer = await chat.ChatAsync("Summarize the latest PR");
Console.WriteLine(answer);
```
