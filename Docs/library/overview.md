# .NET Library Overview

The core library provides a Codex app-server client, ChatGPT auth helpers, and a lightweight Copilot client.

Providers: `Docs/library/providers.md`

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

## Easy session (one-liner)

```csharp
using IntelligenceX.OpenAI;

var result = await Easy.ChatAsync("Hello!");
Console.WriteLine(result.Text);
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
