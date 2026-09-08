---
title: Providers
description: Compare IntelligenceX provider transports across native ChatGPT, app-server, compatible HTTP, and Copilot integrations.
---

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
- Inline image inputs are supported when the selected endpoint and model accept vision messages. Capability selection is explicit; an endpoint URL alone does not establish image or schema support.

## Copilot

Copilot uses direct HTTPS through `IntelligenceXClient`. The SDK requires no Copilot CLI, native runtime, or additional provider package. It discovers the selected model's advertised protocol and uses Responses or Chat Completions through the shared conversation engine.

```csharp
using IntelligenceX.Copilot.Native;
using IntelligenceX.OpenAI;

var options = new IntelligenceXClientOptions {
    TransportKind = OpenAITransportKind.CopilotNative,
    DefaultModel = "gpt-5.4",
    CopilotOptions = new CopilotNativeOptions {
        GitHubToken = Environment.GetEnvironmentVariable("COPILOT_GITHUB_TOKEN")
    }
};
using var client = await IntelligenceXClient.ConnectAsync(options);
var models = await client.ListModelsAsync();
var turn = await client.ChatAsync("Summarize the supplied text.");
Console.WriteLine(EasyChatResult.FromTurn(turn).Text);
```

Choose a model returned by `ListModelsAsync`; availability depends on the account. Streaming, tool calls, inline images, structured-output requests, local conversation history, and usage use the same client contracts as the other native providers. Image and schema support still depend on the selected model. No tools execute unless the host supplies and runs them.

For credentials, supply `GitHubToken`, an asynchronous `TokenProvider`, or an explicit `IAuthBundleStore`. With no explicit source, the SDK checks `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, then `GITHUB_TOKEN`. An explicit store or account selection never falls back to those environment variables. `AccountId` is the numeric GitHub user ID represented as a string; the SDK verifies a pinned identity before inference.

For device sign-in, configure `GitHubClientId` for a registered GitHub app with device flow enabled, then call `LoginCopilotAsync(code => ShowCode(code.UserCode, code.VerificationUri))`. The host owns the UI. Supply an auth store to persist credentials; otherwise they remain in this client instance. Expiring stored credentials renew through the same registered app. Some app types also require `GitHubClientSecret`. GitHub app registration and account entitlement must permit Copilot access; completing sign-in alone does not establish inference entitlement.

For bounded document operations, wrap the connected client in `OpenAIChatTreatmentProvider`. `Ephemeral = true` removes local conversation state after the operation, and `MaxResponseBytes` bounds wire bytes including streaming frames. Native Copilot defaults to a ten-minute request deadline and a 16 MiB response limit. Hosted service retention remains a separate policy.

This transport implements the Copilot client inference protocol, which is not a versioned public GitHub REST inference API. It does not use the GitHub Copilot management API or promise protocol stability. Unsupported models and requests fail explicitly; the SDK does not install or fall back to a CLI.

### Migrating from the retired Copilot clients

Replace `OpenAITransportKind.CopilotCli`, `CopilotClient`, `CopilotChatClient`, and the experimental `CopilotDirectClient` with `IntelligenceXClient` configured as above. Replace `CopilotTreatmentProvider` with `OpenAIChatTreatmentProvider` over that client. Remove CLI paths, launchers, installers, environment forwarding, and direct-wrapper configuration. Use `copilot-native` in saved configuration. Legacy `copilot-cli` selections are rejected rather than selecting another provider.

Custom `IAuthBundleStore` implementations must implement `RemoveAsync(provider, accountId, cancellationToken)` for selective logout. A null account removes only the provider's accountless entry. Other providers and accounts must remain stored. The shared store accepts credentials without refresh tokens; renewal is attempted only for expiring credentials that need it.
