# Usage Telemetry Foundation

This document defines the first-phase architecture for provider-neutral usage telemetry in IntelligenceX.

## Goals

- Support exact token accounting when providers expose local artifacts or turn usage.
- Support online quota, credit, and usage-window overlays when providers expose account APIs.
- Merge usage imported from Windows, WSL, macOS, recovered folders, and manually-added roots without double counting.
- Keep the ingestion model reusable for IX-owned features such as reviewer and chat.
- Stay expandable to OpenAI-compatible and Anthropic-compatible local runtimes such as LM Studio and Ollama.

## Separation of Concerns

The subsystem treats these as separate dimensions:

- Provider: Codex, Claude, OpenAI, Anthropic, GitHub, LM Studio, Ollama, other.
- Source kind: local logs, recovered folder, CLI probe, OAuth API, web session, compatible API, internal IX.
- Identity: provider account id, account label, person label, machine label.
- Truth level: exact, inferred, estimated, unknown.
- Rendering: heatmap, totals, burn rate, pace, cost, duration.

## Canonical Ledger

All ingestion paths normalize into usage events with the same fields:

- Provider and adapter identifiers.
- Source root identifier.
- Provider account id and optional user-managed account label.
- Session, thread, turn, and response identifiers when available.
- Timestamp, model, and optional surface.
- Input, cached-input, output, reasoning, and total token counts.
- Duration in milliseconds.
- Cost in USD when known.
- Truth level and raw hash for provenance.

This ledger is the source of truth for aggregates and visuals.

## Dedupe Strategy

Providers do not guarantee the same identifiers everywhere, so the ledger must dedupe using a priority order:

1. `provider + provider_account_id + session_id + turn_id`
2. `provider + response_id`
3. `provider + raw_hash`

When two records dedupe to the same canonical event, the store merges sparse fields instead of double counting them.

## Source Roots

Source roots are first-class records rather than implicit home-directory assumptions.

Examples:

- `~/.codex/sessions`
- `~/.claude/projects`
- `$CODEX_HOME/archived_sessions`
- `/mnt/c/Users/.../Windows.old/Users/.../.codex`
- manually-added backup folders

Each source root records:

- provider
- source kind
- normalized path
- optional platform hint
- optional machine label
- optional account hint

This allows one account to span many roots and many machines.

## Adapters

Each provider can expose multiple adapters.

Examples:

- `CodexSessionLogAdapter`
- `CodexUsageApiAdapter`
- `ClaudeProjectsJsonlAdapter`
- `ClaudeOAuthUsageAdapter`
- `OpenAICompatibleUsageAdapter`
- `IxInternalUsageAdapter`

Adapters should focus on import, not storage or visualization.

## Account Resolution

Account identity is not derived from folder paths alone.

Resolution order:

1. Strong provider ids such as account id, organization id, or email found in artifacts or auth files.
2. Auth metadata associated with the source root.
3. Manual account bindings.
4. Unknown account bucket.

Manual bindings remain important for recovered folders and providers with weak identity signals.

## Storage Shape

Planned persistence tables:

- `source_roots`
- `raw_artifacts`
- `usage_events`
- `account_bindings`
- `daily_usage_aggregates`

The current foundation now includes:

- provider-neutral telemetry contracts
- in-memory and SQLite-backed source-root and usage-event stores
- in-memory and SQLite-backed raw-artifact stores for incremental re-import tracking
- a first Codex local-log importer
- a first Claude local-log importer
- a provider registry and import coordinator for manual and discovered roots
- a provider-neutral daily aggregate builder for future heatmaps and burn-rate views
- a provider-neutral summary builder for totals, peak-day, rolling-window, and top-breakdown views
- a provider-neutral usage heatmap document builder on top of canonical daily aggregates
- a SQLite-ledger-backed `intelligencex heatmap usage` preview path for canonical usage events
- a provider-neutral usage overview builder with summary cards plus reusable account/provider/person heatmaps
- a telemetry CLI `overview` export path that can write `overview.json`, a bundled `index.html`, and per-heatmap SVG/JSON artifacts
- adapter-level unchanged-artifact skipping with a `--force` reimport escape hatch for rebinding or parser refreshes
- IX-internal turn-completed telemetry emitted by `IntelligenceXClient`
- optional telemetry feature and surface labels flowing through `ChatOptions` and `EasyChatOptions`
- an `InternalIxUsageRecorder` that writes successful IX-owned turns into the canonical ledger

Next phases remain focused on more providers and aggregates rather than reworking the foundation.

## Phase Order

1. Canonical models, adapter contracts, and merge rules.
2. IX internal usage adapter and recorder.
3. Burn-rate and summary views fed by canonical daily aggregates.
4. Online quota and credit overlays.
5. OpenAI-compatible and Anthropic-compatible local-runtime adapters.
