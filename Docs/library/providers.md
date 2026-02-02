# Providers

## OpenAI (ChatGPT native transport)

- Uses ChatGPT OAuth login and stores tokens under `~/.intelligencex/auth.json`
- Best for users with an existing ChatGPT subscription
- Works with the `EasySession` and `IntelligenceXClient` helpers

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

```csharp
using IntelligenceX.Copilot;

await using var client = await CopilotClient.StartAsync();
var status = await client.GetStatusAsync();
Console.WriteLine(status.Version);
```
