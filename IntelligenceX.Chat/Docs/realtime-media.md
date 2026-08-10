# ChatGPT Realtime, voice, and images

IntelligenceX can use an existing ChatGPT/Codex sign-in for chat, image generation, and short-lived Realtime credentials. The Realtime client connects to `gpt-realtime-2.1` over WebSocket and handles streamed text and audio events.

Applications using a user's ChatGPT account should connect directly to OpenAI:

1. The user explicitly signs in to their own ChatGPT account.
2. The app stores the OAuth session in the operating system's protected credential storage.
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
    // Hosts should provide an IAuthBundleStore backed by protected credential storage.
    AuthStore = protectedAuthStore,
    LoadCodexAuthJson = false
};

using var realtime = new OpenAIRealtimeClient(chatGptOptions: chatGpt);
using var session = await realtime.ConnectAsync(
    new OpenAIRealtimeSessionOptions {
        Model = "gpt-realtime-2.1",
        OutputModalities = new[] { "audio" },
        Voice = "marin",
        InputTranscription = new OpenAIRealtimeTranscriptionOptions {
            Model = OpenAIRealtimeTranscriptionModels.GptLiveTranscribe,
            Prompt = "Transcribe the spoken language without translation.",
            LanguageHints = new[] { "en", "pl" },
            Keywords = new[] { "CasaRay", "Home Assistant" },
            Delay = OpenAIRealtimeTranscriptionDelay.High
        },
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
integrations can use the existing Codex auth store; other hosts should provide an adapter backed by their
platform's protected credential storage.

The same session can accept microphone buffers through `AppendAudioAsync` and `CommitAudioAsync`. `SendImageAsync` sends a public URL or data URL as Realtime image input.

Use `gpt-live-transcribe` for a continuously open microphone. Use `gpt-transcribe` for completed audio or
committed WebSocket turns when maximum accuracy matters more than immediate partial text. IX also exposes
the documented alternatives: `gpt-4o-transcribe`, `gpt-4o-mini-transcribe`,
`gpt-4o-mini-transcribe-2025-12-15`, `gpt-realtime-whisper`, and `whisper-1`. The diarization model remains a
file-transcription choice rather than a Realtime input model.

IX models language, prompt, keyword, and delay support independently: `gpt-live-transcribe` accepts the full
live context, `gpt-transcribe` accepts plural languages and keywords without delay, and
`gpt-realtime-whisper` accepts singular language plus delay without a prompt. Custom model ids can override
each capability explicitly. Completion events expose the final transcript and detected languages, while
transcription delta events flow through `TextDelta` for live captions.

WebSocket is available for native .NET clients and lower-level integrations. OpenAI recommends WebRTC for
mobile clients because it handles media and changing network conditions more robustly. Native mobile hosts
should reuse the same ChatGPT authentication and Realtime session configuration behind a thin WebRTC
transport adapter.

## Host integration boundary

Hosts should not depend on another product's UI service or a vendor-operated replacement. A thin local
platform adapter should use:

- a user-initiated system-browser OAuth flow appropriate for the host;
- operating-system protected storage for access and refresh credentials;
- a native HTTP client for direct ChatGPT Responses, image-generation, and Realtime credential requests;
- WebRTC for production voice, with WebSocket available where the lower-level transport is useful;
- the shared IntelligenceX capability/action contracts for tool descriptions, confirmation, and result mapping.

This keeps reusable AI behavior and safety contracts in IntelligenceX without adding an intermediary to the
network path. Each host adapter owns only platform authentication, secure storage, transport, microphone,
and playback.

## Privacy contract

- The app communicates with OpenAI directly.
- Prompts, conversations, microphone audio, generated images, tool results, and credentials do not pass
  through an IntelligenceX or vendor-operated service.
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

Because the Realtime voice capability and platform adapters are new, validate the complete authorization,
callback, token refresh, revocation, reconnect, interruption, and quota-exhaustion flows on each target
platform before release. This is client compatibility and release hardening; it does not require or justify
an intermediary relay.
