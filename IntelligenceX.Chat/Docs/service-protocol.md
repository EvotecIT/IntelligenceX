# Service Protocol (NDJSON)

`IntelligenceX.Chat.Service` exposes a local, typed protocol over a named pipe.

Transport:

- Named pipe: `--pipe <NAME>` (default: `intelligencex.chat`)
- Framing: newline-delimited JSON (one JSON object per line)
- Encoding: UTF-8 text via `StreamReader`/`StreamWriter`

This is intentionally small and AOT-friendly:

- Messages are typed and serialized with `System.Text.Json` source generation (`IntelligenceX.Chat.Abstractions.Serialization.ChatServiceJsonContext`).
- Requests are deserialized as `IntelligenceX.Chat.Abstractions.Protocol.ChatServiceRequest`.
- Responses and events are serialized as `IntelligenceX.Chat.Abstractions.Protocol.ChatServiceMessage`.

## Requests

All requests include:

- `type`: discriminator
- `requestId`: correlation id

Supported:

- `hello`
- `ensure_login` (cached login check)
- `chatgpt_login_start`
- `chatgpt_login_prompt_response`
- `chatgpt_login_cancel`
- `list_tools`
- `chat`

## Messages

All messages include:

- `type`: discriminator
- `kind`: `response` or `event`
- `requestId`: optional correlation id (always present for responses; present for chat delta events)

Login flow messages:

- `chatgpt_login_started` (response; contains `loginId`)
- `chatgpt_login_url` (event; contains OAuth `url`)
- `chatgpt_login_prompt` (event; contains `promptId` + prompt text)
- `chatgpt_login_completed` (event; contains `ok` + optional error)

Chat execution messages:

- `chat_status` (event; progress status such as `thinking`, `tool_call`, `tool_running`, `tool_completed`)
- `chat_delta` (event; streaming text deltas)
- `chat_result` (response; final text + optional tool trace)

## Example

Hello request:

```json
{"type":"hello","requestId":"1"}
```

Hello response:

```json
{"type":"hello","kind":"response","requestId":"1","name":"IntelligenceX.Chat.Service","version":"0.0.0.0","processId":"12345","policy":{"readOnly":true,"allowedRoots":[],"packs":[{"id":"system","name":"System","tier":0,"enabled":true,"isDangerous":false}],"dangerousToolsEnabled":false,"maxToolRounds":3,"parallelTools":true,"maxTableRows":20,"maxSample":10,"redact":false}}
```

Chat request:

```json
{"type":"chat","requestId":"2","text":"List files in C:\\\\Support\\\\GitHub","options":{"maxToolRounds":3,"parallelTools":true,"turnTimeoutSeconds":0,"toolTimeoutSeconds":0}}
```

During execution, the service emits streaming events:

```json
{"type":"chat_status","kind":"event","requestId":"2","threadId":"thread1","status":"thinking"}
{"type":"chat_status","kind":"event","requestId":"2","threadId":"thread1","status":"tool_running","toolName":"fs_list","toolCallId":"call1"}
{"type":"chat_delta","kind":"event","requestId":"2","threadId":"thread1","text":"..."}
```

Final response:

```json
{"type":"chat_result","kind":"response","requestId":"2","threadId":"thread1","text":"...","tools":{"calls":[],"outputs":[]}}
```

## Tool Output Contract

Tools return free-form string outputs. When tool outputs are JSON, the service extracts a subset of fields for UI traces (for example `summary_markdown`, `meta`, `render`).

See: `Docs/tool-output-contract.md`

## Tool Paging Contract (Cursor/Offset)

This is a convention for tool inputs/outputs (not a pipe framing concern). Tools that can return large lists should:

- Accept a `cursor` (string) or `offset` (integer), plus `page_size` and a `max_results` cap.
- Return:
  - `count`: number of items returned in `results`
  - `is_truncated`: whether the tool stopped early due to caps
  - `has_more`: whether more results exist
  - `next_cursor`: cursor token to fetch the next page (when `has_more=true`)

Client/assistant behavior:

- If `has_more=true` and `next_cursor` is non-empty, call the tool again with `cursor=next_cursor` to fetch the next page.
- Prefer presenting a preview table/sample first, then ask whether to fetch more pages or refine scope (OU scoping, tighter filters).

## ChatGPT Login Example

Start login:

```json
{"type":"chatgpt_login_start","requestId":"10","useLocalListener":true,"timeoutSeconds":180}
```

Response:

```json
{"type":"chatgpt_login_started","kind":"response","requestId":"10","loginId":"..."}
```

Service emits URL:

```json
{"type":"chatgpt_login_url","kind":"event","requestId":"10","loginId":"...","url":"https://auth.openai.com/oauth/authorize?..."}
```

If a local listener couldn't capture the OAuth code, the service prompts:

```json
{"type":"chatgpt_login_prompt","kind":"event","requestId":"10","loginId":"...","promptId":"...","prompt":"Paste the redirect URL or authorization code:"}
```

Client responds to the prompt:

```json
{"type":"chatgpt_login_prompt_response","requestId":"11","loginId":"...","promptId":"...","input":"http://localhost:1455/auth/callback?code=...&state=..."}
```

Service emits completion:

```json
{"type":"chatgpt_login_completed","kind":"event","requestId":"10","loginId":"...","ok":true}
```
