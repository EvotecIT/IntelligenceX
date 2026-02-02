# .NET Library Overview

The core library provides a Codex app-server client, ChatGPT auth helpers, and a lightweight Copilot client.

Providers: `./providers.md`

## Quick start (app-server)

```csharp
using IntelligenceX.OpenAI.AppServer;

var client = await AppServerClient.StartAsync(new AppServerOptions {
    ExecutablePath = "codex",
    Arguments = "app-server"
});

await client.InitializeAsync(new ClientInfo("IntelligenceX", "Demo", "0.1.0"));
var login = await client.StartChatGptLoginAsync();
Console.WriteLine($"Login URL: {login.AuthUrl}");
await client.WaitForLoginCompletionAsync(login.LoginId);

var thread = await client.StartThreadAsync("gpt-5.2-codex");
await client.StartTurnAsync(thread.Id, "Hello from IntelligenceX");
```

## Quick start (native ChatGPT)

```csharp
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Native;

var options = new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.Native
};
options.NativeOptions.UserAgent = "IntelligenceX/0.1.0";
await using var client = await IntelligenceXClient.ConnectAsync(options);

await client.LoginChatGptAndWaitAsync(url => Console.WriteLine(url));
var turn = await client.ChatAsync("Summarize the latest PR");
Console.WriteLine(turn.Outputs.Count);
```

## Easy session (one-liner)

```csharp
using IntelligenceX.OpenAI;

var result = await Easy.ChatAsync("Hello!");
Console.WriteLine(result.Text);
```

## Streaming deltas

```csharp
using IntelligenceX.OpenAI;

var session = await EasySession.StartAsync();
using var subscription = session.SubscribeDelta(Console.Write);
await session.ChatAsync("Stream a short answer.");
```

## Usage & limits

```csharp
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

using var usage = new ChatGptUsageService(new OpenAINativeOptions());
var report = await usage.GetReportAsync(includeEvents: true);
Console.WriteLine(report.Snapshot.PlanType);
Console.WriteLine(report.Snapshot.RateLimit?.PrimaryWindow?.UsedPercent);
```

## Config overrides

Add `.intelligencex/config.json` (or set `INTELLIGENCEX_CONFIG_PATH`) to avoid hardcoding.

```json
{
  "openai": {
    "defaultModel": "gpt-5.2-codex",
    "instructions": "You are a helpful assistant.",
    "reasoningEffort": "medium",
    "reasoningSummary": "auto",
    "textVerbosity": "medium",
    "appServerPath": "codex",
    "appServerArgs": "app-server"
  },
  "copilot": {
    "cliPath": "copilot",
    "autoInstall": false
  }
}
```
