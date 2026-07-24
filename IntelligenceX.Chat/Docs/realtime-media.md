# ChatGPT Realtime, voice, and images

IntelligenceX can use an existing ChatGPT/Codex sign-in for chat, image generation, and short-lived Realtime credentials. The Realtime client connects to `gpt-realtime-2.1` over WebSocket and handles streamed text and audio events.

CasaRay, Tactra, and other user-owned apps should connect directly to OpenAI:

1. The user explicitly signs in to their own ChatGPT account.
2. The app stores the OAuth session in device-protected storage, such as Apple Keychain.
3. The app calls OpenAI directly to create its short-lived `ek_...` Realtime credential.
4. The same app opens the Realtime connection directly to OpenAI.
5. The app discards the short-lived credential when the connection closes or it expires.

No Evotec account, relay, or hosted IntelligenceX service is required. Prompts, audio, images, tool results,
and ChatGPT credentials do not pass through Evotec infrastructure. The app must never log credentials or
store them in plain-text settings.

## Connect directly from the user's device

```csharp
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Realtime;

var chatGpt = new OpenAINativeOptions {
    // CasaRay and Tactra should provide an IAuthBundleStore backed by Apple Keychain.
    AuthStore = keychainAuthStore,
    LoadCodexAuthJson = false
};

using var realtime = new OpenAIRealtimeClient(chatGptOptions: chatGpt);
using var session = await realtime.ConnectAsync(
    new OpenAIRealtimeSessionOptions {
        Model = "gpt-realtime-2.1",
        OutputModalities = new[] { "audio" },
        Voice = "marin",
        ClientSecretLifetime = TimeSpan.FromMinutes(2)
    },
    cancellationToken);

await session.SendTextAsync("Say hello in Polish.", cancellationToken: cancellationToken);

while (await session.ReceiveEventAsync(cancellationToken) is { } serverEvent) {
    if (serverEvent.TextDelta is { } text) {
        RenderText(text);
    }
    if (serverEvent.AudioDelta is { } base64Audio) {
        PlayAudio(Convert.FromBase64String(base64Audio));
    }
    if (serverEvent.ErrorMessage is { } error) {
        ReportError(error);
    }
}
```

`OpenAIRealtimeClient` uses `OpenAIRealtimeOptions.ApiKey` only when the user explicitly configures one.
Otherwise it uses the user's ChatGPT OAuth session from the configured `IAuthBundleStore`. Desktop Codex
integrations can use the existing Codex auth store; Apple apps should use their own Keychain-backed adapter.

The same session can accept microphone buffers through `AppendAudioAsync` and `CommitAudioAsync`. `SendImageAsync` sends a public URL or data URL as Realtime image input.

WebSocket is available for native .NET clients and lower-level integrations. OpenAI recommends WebRTC for
mobile clients because it handles media and changing network conditions more robustly. CasaRay and Tactra
should therefore reuse the same local ChatGPT auth and Realtime session configuration while using a thin
Apple WebRTC transport adapter for production voice.

## Apple integration boundary

CasaRay and Tactra are native Swift applications, so they should not connect to the Windows-shaped
IntelligenceX Chat service or an Evotec-hosted replacement. Their thin local adapter should use:

- a user-initiated system-browser OAuth flow, using `ASWebAuthenticationSession` when the current
  ChatGPT/Codex redirect contract supports the app callback and a local loopback callback otherwise;
- Apple Keychain for the resulting access and refresh credentials;
- `URLSession` for direct ChatGPT Responses, image-generation, and Realtime credential requests;
- WebRTC for production voice, with `URLSessionWebSocketTask` available where the WebSocket transport is useful;
- the shared IntelligenceX capability/action contracts for tool descriptions, confirmation, and result mapping.

This keeps reusable AI behavior and safety contracts in IntelligenceX without putting Evotec in the network
path. The Swift adapter owns only Apple authentication, secure storage, transport, microphone, and playback.

## Privacy contract

- The app communicates with OpenAI directly.
- Evotec cannot see prompts, conversations, microphone audio, generated images, tool results, or credentials.
- ChatGPT OAuth credentials remain in device-protected storage and can be removed by disconnecting the account.
- IntelligenceX usage telemetry remains opt-in and is not required for ChatGPT, image, or Realtime features.
- Integrations such as Home Assistant communicate only with destinations the user configures.

## Chat and image generation

Regular IX chat communicates directly with OpenAI's ChatGPT Responses endpoint. It supports text and image inputs, and the built-in image-generation tool uses OpenAI's current `gpt-image-2` capability. `EasyChatResult.Images` exposes URL, path, base64, and MIME information to library consumers.

The desktop chat service now carries generated-image URL/path references in `ChatResultMessage.Images`. Large base64 payloads remain in the provider/core layer instead of being copied into NDJSON frames.

ChatGPT/Codex OAuth is a user-owned subscription capability, not an API-key fallback or an unofficial
entitlement. Voice uses its plan-specific allowance, while work started through Voice uses the user's existing
Codex usage budget. Surface plan limits and rollout errors to the user instead of silently switching identities
or credentials.

Because the Realtime voice capability and the Apple adapters are new, validate the complete authorization,
callback, token refresh, revocation, reconnect, interruption, and quota-exhaustion flows on physical devices
before release. This is client compatibility and release hardening; it does not require or justify an Evotec
relay.
