# Changelog

## Unreleased
- Add a recent-only in-process work watcher with safe JSONL append checkpoints, independent conditional GitHub refresh, and a two-day default for native consumers.
- Evaluate known unfinished turns after collection using the latest same-root Desktop/log activity, preserving explicit completion, interruption and waiting signals.
- Add metadata-only Codex and Claude work snapshots, GitHub PR/check and review signals, attention rules, and `agents status --json`.
- Add generated snapshot JSON and a .NET 10 Native AOT smoke consumer for native applications.
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
