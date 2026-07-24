# ChatGPT Realtime, voice, and images

IntelligenceX can use an existing ChatGPT/Codex sign-in for chat, image generation, and short-lived Realtime credentials. The Realtime client connects to `gpt-realtime-2.1` over WebSocket and handles streamed text and audio events.

The credential boundary matters for mobile apps:

1. A trusted backend loads or refreshes the ChatGPT OAuth session.
2. The backend calls `CreateClientSecretAsync` and returns only the short-lived `ek_...` value, expiry, and model.
3. The app opens the WebSocket with that credential.
4. The app discards the credential when the connection closes or it expires.

Do not put the persisted ChatGPT access token or refresh token in an iOS app, logs, app settings, or an app-to-service protocol.

## Mint a credential in a trusted backend

```csharp
using IntelligenceX.OpenAI.Realtime;

using var realtime = new OpenAIRealtimeClient();
var credential = await realtime.CreateClientSecretAsync(
    new OpenAIRealtimeSessionOptions {
        Model = "gpt-realtime-2.1",
        OutputModalities = new[] { "audio" },
        Voice = "marin",
        ClientSecretLifetime = TimeSpan.FromMinutes(2)
    },
    cancellationToken);

// Return only credential.Value, credential.ExpiresAt, and credential.Model.
```

`OpenAIRealtimeClient` uses `OpenAIRealtimeOptions.ApiKey` when one is supplied. Otherwise it uses the same ChatGPT OAuth store as the native IntelligenceX OpenAI transport.

## Connect from a .NET or iOS client

```csharp
using IntelligenceX.OpenAI.Realtime;

var credential = new OpenAIRealtimeClientSecret(
    valueFromBackend,
    expiresAtFromBackend,
    modelFromBackend);

using var realtime = new OpenAIRealtimeClient();
using var session = await realtime.ConnectAsync(credential, cancellationToken);

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

The same session can accept microphone buffers through `AppendAudioAsync` and `CommitAudioAsync`. `SendImageAsync` sends a public URL or data URL as Realtime image input.

WebSocket is a good fit for native .NET clients and server-to-server use. OpenAI recommends WebRTC for browser and mobile clients when its media handling and network recovery are a better fit. The short-lived credential flow is the same trust boundary for either transport.

## Chat and image generation

Regular IX chat continues to use the ChatGPT Responses backend. It supports text and image inputs, and the built-in image-generation tool uses OpenAI's current `gpt-image-2` capability. `EasyChatResult.Images` exposes URL, path, base64, and MIME information to library consumers.

The desktop chat service now carries generated-image URL/path references in `ChatResultMessage.Images`. Large base64 payloads remain in the provider/core layer instead of being copied into NDJSON frames.

The ChatGPT OAuth Realtime mint path is currently accepted for ChatGPT/Codex sessions, but OpenAI's public API documentation still describes API-key authorization for this endpoint. Treat subscription-backed minting as a capability that may be account- or rollout-dependent, and keep the failure visible rather than silently switching credentials.
