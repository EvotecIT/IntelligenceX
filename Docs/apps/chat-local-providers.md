---
title: Chat Runtime Modes and Local Providers
description: Configure IX Chat runtime modes for ChatGPT, Copilot, LM Studio, Azure OpenAI, Ollama, and other OpenAI-compatible providers.
---

# Chat Runtime Modes (ChatGPT, Copilot, LM Studio, Azure, Other Providers)

IX Chat supports three runtime transports in the desktop app:

| Runtime Mode | Transport | Auth | Typical Use |
|---|---|---|---|
| ChatGPT Runtime | `native` | ChatGPT sign-in | OpenAI-native chat runtime |
| Copilot Subscription | `copilot-cli` | GitHub Copilot sign-in | Use Copilot subscription without API keys |
| Compatible HTTP | `compatible-http` | Optional API key | LM Studio, Ollama, Azure OpenAI, and other OpenAI-compatible endpoints |

Important:
- Runtime selection and sign-in are related but distinct.
- API keys are used only in `compatible-http`.
- `copilot-cli` uses Copilot subscription authentication, not API keys.
- You can stay signed in to ChatGPT while using non-native runtime modes.

## Chat App (Runtime Selection)

For the WinUI desktop app (`Build/Run-ChatApp.ps1`):

1. Open **Options -> Runtime**.
2. Use one of the primary actions:
   - **Use ChatGPT Runtime** (`native`)
   - **Use LM Studio Runtime** (`compatible-http` + LM Studio base URL)
   - **Use Copilot Subscription** (`copilot-cli`)
3. Runtime actions apply immediately in-process and show an in-progress state; runtime switches do not auto-restart the sidecar.
4. Click **Refresh Models** after switching runtime or changing endpoint details when you want a forced re-probe.
5. Use **Show Advanced Runtime** when you need explicit transport/base URL/API key/manual model overrides.

Advanced presets:
- **Use LM Studio Runtime**: `http://127.0.0.1:1234/v1`
- **Use Ollama Runtime**: `http://127.0.0.1:11434`
- **Use Copilot Endpoint (API key)**: `https://api.githubcopilot.com/v1` (compatible-http path, API key required)

Notes:
- `Refresh Models` applies pending runtime field changes first, then refreshes discovery.
- For `compatible-http` and `copilot-cli`, the app auto-selects the first discovered model only when no model is set. Explicit/manual model IDs are preserved.
- If you choose `Manual model input` in **Discovered models**, type the exact model id in the **Model** field and click **Apply Runtime**.
- Leaving API key empty keeps the currently saved compatible-http key unchanged.
- Use **Clear Saved API Key** to remove the saved compatible-http key from the active profile.
- Compatible HTTP auth modes: `Bearer` (API key), `Basic` (username/password), or `None`.
- Anthropic/Gemini entries are bridge presets (experimental): IX Chat targets your configured compatible endpoint and does not do native subscription login itself.
- ChatGPT sign-in and account switching are native-runtime controls. In non-native modes (`compatible-http`/`copilot-cli`) the menu hides sign-in actions and chat is not gated on ChatGPT login state.
- The panel shows active runtime/model status so you can confirm what is currently used.
- While a runtime switch is running, runtime buttons are temporarily disabled to avoid duplicate apply requests.

Reasoning controls:
- `native` and most `compatible-http` presets expose reasoning controls (`minimal|low|medium|high|xhigh`) as provider hints.
- `copilot-cli` currently does not expose reasoning controls.
- Experimental Anthropic/Gemini bridge presets currently use provider-default reasoning.

## Model Discovery and Runtime Detection

- Auto Detect probes localhost endpoints for LM Studio (`127.0.0.1:1234`) and Ollama (`127.0.0.1:11434`).
- If localhost is not available, IX Chat also probes your currently configured `compatible-http` base URL.
- This helps external endpoints (for example remote LM Studio or Azure/OpenAI-compatible gateways) reflect real availability.
- For LM Studio endpoints, IX Chat also reads LM Studio catalog metadata (state, quantization, architecture, context lengths, capabilities) when available and surfaces it in the runtime model picker/state note.

Troubleshooting:
- `Couldn't connect to local runtime after startup: Timed out waiting for service pipe.` indicates the selected runtime transport did not become reachable in time.
- For LM Studio, verify Local Server is running and `http://127.0.0.1:1234/v1/models` returns data before retrying.

## Security Model

- `http://` is rejected by default.
- For loopback `http://127.0.0.1/...` or `http://localhost/...`, you must opt in with `--openai-allow-insecure-http`.
- For non-loopback `http://` (not recommended), you must opt in with `--openai-allow-insecure-http-non-loopback`.

## Chat Host (Recommended For Local Use)

The simplest way to dogfood locally from this repo is the REPL host (`IntelligenceX.Chat.Host`).

Ollama example:

```powershell
pwsh ./Build/Run-Chat.ps1 `
  -AllowRoot C:\Support\GitHub `
  -OpenAITransport compatible-http `
  -OpenAIBaseUrl http://127.0.0.1:11434 `
  -OpenAIAllowInsecureHttp `
  -Model llama3.1
```

LM Studio example:

```powershell
pwsh ./Build/Run-Chat.ps1 `
  -AllowRoot C:\Support\GitHub `
  -OpenAITransport compatible-http `
  -OpenAIBaseUrl http://127.0.0.1:1234/v1 `
  -OpenAIAllowInsecureHttp `
  -Model <your-model>
```

Notes:
- `-OpenAIBaseUrl` can be either `http://host:port` or `http://host:port/v1`. IntelligenceX normalizes it to `/v1/` internally.
- For local providers, `-OpenAIApiKey` is usually optional (many servers ignore it).

Copilot subscription example:

```powershell
pwsh ./Build/Run-Chat.ps1 `
  -AllowRoot C:\Support\GitHub `
  -OpenAITransport copilot-cli `
  -Model gpt-5.3-codex
```

## Chat Service (When Embedding In An App)

`IntelligenceX.Chat.Service` is a JSONL/named-pipe service designed to be started/managed by a UI (desktop app) or other supervisor.

Example:

```powershell
dotnet run --project IntelligenceX.Chat/IntelligenceX.Chat.Service/IntelligenceX.Chat.Service.csproj --framework net10.0-windows -- `
  --openai-transport compatible-http `
  --openai-base-url http://127.0.0.1:11434 `
  --openai-allow-insecure-http `
  --model llama3.1 `
  --allow-root C:\Support\GitHub
```

Copilot subscription service example:

```powershell
dotnet run --project IntelligenceX.Chat/IntelligenceX.Chat.Service/IntelligenceX.Chat.Service.csproj --framework net10.0-windows -- `
  --openai-transport copilot-cli `
  --model gpt-5.3-codex `
  --allow-root C:\Support\GitHub
```

## Tool Calling Support

IntelligenceX will send OpenAI tool definitions (`tools`) and expects tool calls in `message.tool_calls`.

Provider compatibility varies:
- Some local servers fully support tool calling.
- Others ignore `tools` or return tool calls in non-standard shapes.

When tool calling is not supported by the provider, the chat still works but tools will not be invoked.

## Current Limitations

- Image inputs are not supported for `compatible-http` yet (text + tools only).
