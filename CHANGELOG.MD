# Changelog

## Unreleased
- IntelligenceX.Chat.Host now defaults to parallel tool calls (`ParallelToolCalls = true`).
  Use `--no-parallel-tools` to force serial tool execution.
- Swift: lower Realtime voice start-up latency with opt-in APIs; default behaviour is unchanged.
  - `IXRealtimeWebRTCSession.prewarm(priority:)` creates the WebRTC peer connection factory in the background before the first `connect()`.
  - `IXRealtimeAudioSessionReservation.prepare(profile:)` / `release()` activate the voice audio session ahead of `connect()` with reference-counted ownership.
  - `IXURLSessionRealtimeWebSocketConnector(session:)` reuses a caller-owned `URLSession`; `defaultSessionConfiguration()` exposes the default policy.
  - `IXRealtimeClientSecretPrefetcher` mints a client secret ahead of time, hands it out once while enough lifetime remains, joins in-flight mints, and supports `invalidate()`; `IXRealtimeClientSecretRequest.minimal(options:scope:)` mints a content-free secret.
- Swift: `IXCodexResponseOptions.promptCacheKey` sets a stable `prompt_cache_key` across conversations, and `IXCodexUsage.cachedInputTokens` reports `input_tokens_details.cached_tokens`.

## 0.1.0
- Initial library scaffold.
