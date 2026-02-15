# Chat With Local Providers (OpenAI-Compatible HTTP)

IntelligenceX can talk to local (or self-hosted) models that expose an OpenAI-compatible HTTP API, using the `compatible-http` transport.

This is intended for providers that support (at minimum):
- `POST /v1/chat/completions`
- (optional but recommended) `GET /v1/models`

Examples: Ollama, LM Studio, llama.cpp server, and similar OpenAI-compat endpoints.

## Chat App (Easiest Local Setup)

For the WinUI desktop app (`Build/Run-ChatApp.ps1`):

1. Open **Options -> Profile -> Model Runtime**.
2. Click **Connect LM Studio** (primary flow).
3. The app switches to compatible HTTP using LM Studio defaults and refreshes model discovery.
4. Choose a model from **Discovered models** and click **Apply Runtime** when needed.
5. Open **Show Advanced Runtime** only for transport/base URL/API key/manual model overrides.

Notes:
- `Refresh Models` applies pending runtime field changes first, then refreshes discovery.
- For `compatible-http`, if the current model is empty or invalid, the app auto-selects the first discovered model.
- Leaving API key empty keeps the currently saved key unchanged.
- Use **Clear Saved API Key** to remove the saved key from the active profile.

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

## Tool Calling Support

IntelligenceX will send OpenAI tool definitions (`tools`) and expects tool calls in `message.tool_calls`.

Provider compatibility varies:
- Some local servers fully support tool calling.
- Others ignore `tools` or return tool calls in non-standard shapes.

When tool calling is not supported by the provider, the chat still works but tools will not be invoked.

## Current Limitations

- Image inputs are not supported for `compatible-http` yet (text + tools only).
